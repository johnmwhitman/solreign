using System;
using Content.Shared._Solreign.FX;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Solreign.FX;

/// <summary>
///     The sole implementer of <see cref="ISolreignFxCueRaiser"/> (spec §5.2, grk #2A) — the ONLY
///     code path permitted to put a <see cref="SolreignFxCueV1"/> on the wire. Gameplay systems
///     (W4's changeling consumer and beyond) depend on this system and never call
///     <c>RaiseNetworkEvent</c> on a cue directly; <c>Content.Tests._Solreign.FX.SolreignFxRaisePathStaticAnalysisTests</c>
///     greps the source tree to enforce that.
///
///     Wires together every piece of send-side plumbing W1 forward-declared and W2 builds:
///     the egress budget (<see cref="SolreignFxEgressBudget"/>, spec §3.1), the correlation source
///     (<see cref="SolreignFxDiagnosticsSystem"/>), the validated prototype source (M5 closure,
///     <see cref="SolreignFxPrototypeSource"/>), and the anchor resolver
///     (<see cref="SolreignFxServerAnchorResolver"/>). Also closes H1 on the SEND side: <see cref="RaiseCue"/>
///     refuses outright to broadcast a <see cref="SolreignFxAudienceClassification.DetailOnly"/> id —
///     the client-side half of H1's closure (<c>SolreignFxReceiveGuard</c>) is defense in depth on
///     top of this, not a substitute for it.
/// </summary>
public sealed partial class SolreignFxServerSystem : EntitySystem, ISolreignFxCueRaiser
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SolreignFxDiagnosticsSystem _correlationSource = default!;

    private SolreignFxPrototypeSource _prototypeSource = default!;
    private SolreignFxServerAnchorResolver _anchorResolver = default!;
    private readonly SolreignFxEgressBudget _egressBudget = new();
    private readonly SolreignFxDropAggregator _diagnostics = new();

    private sealed class ServerRawPrototypeLoader : ISolreignFxRawPrototypeLoader
    {
        private readonly IPrototypeManager _protoMan;

        public ServerRawPrototypeLoader(IPrototypeManager protoMan)
        {
            _protoMan = protoMan;
        }

        public bool TryGetRaw(string effectId, out SolreignFxCuePrototype? prototype)
        {
            if (string.IsNullOrEmpty(effectId) || !_protoMan.TryIndex<SolreignFxCuePrototype>(effectId, out var found))
            {
                prototype = null;
                return false;
            }

            prototype = found;
            return true;
        }
    }

    /// <summary>grk review M-A: CVars claimed to be "clamped to hardcoded ceilings" but weren't actually clamped at read time — fixed here.</summary>
    private const int MinEgressTickCap = 1;
    private const int MaxEgressTickCap = 256;
    private const float MinEgressRefillMultiplier = 0f;
    private const float MaxEgressRefillMultiplier = 8f;

    /// <summary>grk review M-B: the drop aggregator was recorded into but never flushed server-side — fixed via a periodic Update flush, same shape as the client's FrameUpdate flush.</summary>
    private static readonly TimeSpan DiagnosticsFlushInterval = TimeSpan.FromSeconds(5);
    private TimeSpan _nextDiagnosticsFlush;

    public override void Initialize()
    {
        base.Initialize();

        _anchorResolver = new SolreignFxServerAnchorResolver(EntityManager, _transform);
        _prototypeSource = new SolreignFxPrototypeSource(new ServerRawPrototypeLoader(_protoMan));

        Subs.CVar(_cfg, CCVars.SolreignFxEgressTickCap,
            v => _egressBudget.GlobalTickCap = Math.Clamp(v, MinEgressTickCap, MaxEgressTickCap), invokeImmediately: true);

        // grk review M-C: RefillMultiplier used to only take effect for a category's FIRST bucket
        // creation — a later CVar change never reached already-created buckets. ResetBuckets()
        // rebuilds them (safe: SolreignFxTokenBucket's own constructor tops a fresh bucket up to
        // full burst immediately, so this never PUNISHES players for an admin's live tuning change).
        Subs.CVar(_cfg, CCVars.SolreignFxEgressRefillMultiplier, v =>
        {
            _egressBudget.RefillMultiplier = Math.Clamp(v, MinEgressRefillMultiplier, MaxEgressRefillMultiplier);
            _egressBudget.ResetBuckets();
        }, invokeImmediately: true);

        _protoMan.PrototypesReloaded += OnPrototypesReloaded;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _protoMan.PrototypesReloaded -= OnPrototypesReloaded;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        _prototypeSource.Invalidate();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextDiagnosticsFlush)
            return;

        _nextDiagnosticsFlush = _timing.CurTime + DiagnosticsFlushInterval;

        foreach (var summary in _diagnostics.Flush(_timing.CurTime.TotalSeconds))
        {
            Log.Warning($"[SolreignFx] {summary.Reason}: {summary.Count} raise(s) refused/dropped in the last interval (sample correlation id {summary.SampleCorrelationId})");
        }
    }

    private static SolreignFxAnchorKey BuildAnchorKey(NetCoordinates? coordinates, NetEntity? entityAnchor)
    {
        if (entityAnchor is { } ea)
            return SolreignFxAnchorKey.FromEntity(ea);

        return coordinates is { } c ? SolreignFxAnchorKey.FromCoordinates(c) : default;
    }

    /// <inheritdoc/>
    public bool RaiseCue(
        ProtoId<SolreignFxCuePrototype> effectId,
        NetCoordinates? coordinates,
        NetEntity? entityAnchor,
        float intensity,
        float scale,
        float duration,
        byte? paletteIndex,
        byte? phase,
        EntityUid pvsSource)
    {
        if (!_cfg.GetCVar(CCVars.SolreignFxCueV1Enabled))
            return false; // dormant by design (spec §9) — not a failure worth diagnosing

        // H1 send-side closure (spec §5.2's API-enforced split, grk #2A): this is the ONLY place
        // that decides broadcast-vs-targeted, so it is the only place that can enforce "a
        // DetailOnly id is NEVER broadcast" structurally rather than by caller discipline.
        if (SolreignFxWireAllowlist.TryGetAudienceClassification(effectId.Id, out var classification)
            && classification == SolreignFxAudienceClassification.DetailOnly)
        {
            DebugTools.Assert(false,
                $"SolreignFxServerSystem.RaiseCue refused a DetailOnly-classified EffectId '{effectId.Id}' — use RaiseSecretRoleCue instead.");
            _diagnostics.Record("RaiseCueRefusedDetailOnly", 0);
            return false;
        }

        if (!SolreignFxCategoryTable.TryResolveCategory(effectId.Id, out var category))
        {
            _diagnostics.Record("RaiseCueUnknownCategory", 0);
            return false;
        }

        // grk review H-A: budget must be consumed AFTER TryCreate succeeds, exactly as spec §3.1
        // states ("checked inside the raise helper, after TryCreate") — NOT before. The original
        // ordering here consumed budget first: if TryCreate then failed (bad anchor, unresolved
        // prototype, numeric reject), the category token/global-tick/coalesce bookkeeping had
        // ALREADY been spent and marked, so a LATER identical-looking raise that tick would return
        // `true` via CoalescedWithinTick even though NOTHING was ever actually put on the wire —
        // a false "it worked" with a silently wasted budget unit. Constructing first means budget
        // is only ever consumed for a cue that is actually about to be raised.
        if (!SolreignFxCueV1.TryCreate(effectId.Id, coordinates, entityAnchor, intensity, scale, duration, paletteIndex, phase,
                _prototypeSource, _anchorResolver, _correlationSource, SolreignFxCueStream.Broadcast, out var cue, out var failureReason)
            || cue is null)
        {
            _diagnostics.Record($"RaiseCueTryCreate:{failureReason}", 0);
            return false;
        }

        var anchorKey = BuildAnchorKey(coordinates, entityAnchor);
        var tick = _timing.CurTick;
        var now = _timing.CurTime.TotalSeconds;

        var budgetResult = _egressBudget.TryConsume(category, effectId.Id, anchorKey, tick, now);
        if (budgetResult == SolreignFxEgressBudget.ConsumeResult.CoalescedWithinTick)
            return true; // an identical (EffectId, anchor) was ALREADY successfully raised this tick

        if (budgetResult != SolreignFxEgressBudget.ConsumeResult.Consumed)
        {
            _diagnostics.Record($"RaiseCueEgress:{budgetResult}", 0);
            return false;
        }

        RaiseNetworkEvent(cue, Filter.Pvs(pvsSource));
        return true;
    }

    /// <inheritdoc/>
    public bool RaiseSecretRoleCue(
        ProtoId<SolreignFxCuePrototype> detailEffectId,
        NetCoordinates? coordinates,
        NetEntity? entityAnchor,
        float intensity,
        float scale,
        float duration,
        byte? paletteIndex,
        byte? phase,
        EntityUid pvsSource,
        ICommonSession actorSession)
    {
        if (!_cfg.GetCVar(CCVars.SolreignFxCueV1Enabled))
            return false;

        if (!_prototypeSource.TryResolve(detailEffectId.Id, out var detailPrototype) || detailPrototype is null)
        {
            _diagnostics.Record("SecretRoleDetailPrototypeUnresolved", 0);
            return false;
        }

        // grk review L-D: a caller passing an entity anchor that ISN'T the actor's own attached
        // entity is a bug on the CALLER's side — it would still fail safe (the actor's own client
        // rejects it via SolreignFxReceiveGuard's anchor-mismatch check, and bystanders only ever
        // see the generic), but that failure would be silent and confusing to debug. Debug-only
        // assert names the caller bug loudly rather than letting it manifest as "the actor's juice
        // just never showed up" three files away.
        DebugTools.Assert(
            entityAnchor is null || actorSession.AttachedEntity is null || GetNetEntity(actorSession.AttachedEntity.Value) == entityAnchor.Value,
            $"SolreignFxServerSystem.RaiseSecretRoleCue called with an entityAnchor that isn't the actor's own attached entity — the actor's own client will reject the detail cue via SolreignFxReceiveGuard.");

        if (!detailPrototype.RequiresRedactedBroadcastVariant || detailPrototype.GenericVariant is not { } genericId)
        {
            DebugTools.Assert(false,
                $"SolreignFxServerSystem.RaiseSecretRoleCue called with EffectId '{detailEffectId.Id}', which has no configured GenericVariant.");
            _diagnostics.Record("SecretRoleNoGenericVariantConfigured", 0);
            return false;
        }

        if (!SolreignFxCategoryTable.TryResolveCategory(detailEffectId.Id, out var detailCategory)
            || !SolreignFxCategoryTable.TryResolveCategory(genericId.Id, out var genericCategory))
        {
            _diagnostics.Record("SecretRoleUnknownCategory", 0);
            return false;
        }

        if (!_prototypeSource.TryResolve(genericId.Id, out var genericPrototype) || genericPrototype is null)
        {
            _diagnostics.Record("SecretRoleGenericPrototypeUnresolved", 0);
            return false;
        }

        // grk review H-A/H-B: construct BOTH cues BEFORE touching the egress budget at all — same
        // "budget checked after TryCreate" ordering fix as RaiseCue, and it also means a doomed
        // TryCreate never burns a budget token or poisons the coalesce set in the first place.
        if (!SolreignFxCueV1.TryCreate(detailEffectId.Id, coordinates, entityAnchor, intensity, scale, duration, paletteIndex, phase,
                _prototypeSource, _anchorResolver, _correlationSource, SolreignFxCueStream.Targeted, out var detailCue, out var detailFailure)
            || detailCue is null)
        {
            _diagnostics.Record($"SecretRoleDetailTryCreate:{detailFailure}", 0);
            return false;
        }

        // Generic/redacted cue: EVERY numeric field scrubbed to the generic prototype's own fixed
        // defaults (spec §5.2 item 1, grk #2 — never the detail cue's real values); palette/phase
        // passed as null so TryCreate falls back to the generic prototype's own declared default
        // entry (never the detail cue's palette/phase, which could itself be identity-revealing).
        // TryCreate always mints an independent seed and a broadcast-stream CorrelationId for this
        // call regardless of what the detail call above already consumed (grk #6).
        if (!SolreignFxCueV1.TryCreate(genericId.Id, coordinates, entityAnchor,
                genericPrototype.DefaultIntensity, genericPrototype.DefaultScale, genericPrototype.DefaultDuration,
                null, null,
                _prototypeSource, _anchorResolver, _correlationSource, SolreignFxCueStream.Broadcast, out var genericCue, out var genericFailure)
            || genericCue is null)
        {
            _diagnostics.Record($"SecretRoleGenericTryCreate:{genericFailure}", 0);
            return false;
        }

        // Both cues exist. NOW consume budget for the pair — atomically, via TryConsumePair (grk
        // review H-A/H-B fix): peeks BOTH categories' availability before committing either, so a
        // failure on either side leaves neither token spent and neither key coalesced. Without
        // this, consuming detail-then-generic sequentially could spend the detail token, then fail
        // the generic check, and incorrectly leave the detail (id, anchor) marked "already covered"
        // for the rest of the tick — a later identical call would then wrongly report success even
        // though NEITHER half of the pair was ever raised.
        var anchorKey = BuildAnchorKey(coordinates, entityAnchor);
        var tick = _timing.CurTick;
        var now = _timing.CurTime.TotalSeconds;

        var pairBudget = _egressBudget.TryConsumePair(
            detailCategory, detailEffectId.Id,
            genericCategory, genericId.Id,
            anchorKey, tick, now);

        if (pairBudget == SolreignFxEgressBudget.ConsumeResult.CoalescedWithinTick)
            return true; // the pair already went out this tick for this exact (detail id, anchor)

        if (pairBudget != SolreignFxEgressBudget.ConsumeResult.Consumed)
        {
            _diagnostics.Record($"SecretRolePairEgress:{pairBudget}", 0);
            return false;
        }

        // Actor exclusion (spec §5.2 item 1, grk #2E): the broadcast filter excludes the actor —
        // they receive only the detail cue below, never a double-render of both.
        RaiseNetworkEvent(genericCue, Filter.Pvs(pvsSource).RemovePlayer(actorSession));
        RaiseNetworkEvent(detailCue, actorSession);
        return true;
    }
}
