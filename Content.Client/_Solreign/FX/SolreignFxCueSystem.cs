using System;
using System.Collections.Generic;
using Content.Shared._Solreign.FX;
using Content.Shared.CCVar;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Solreign.FX;

/// <summary>
///     FX Language v1's real client-side consumer (spec §8 W2 file boundary). Subscribes the
///     networked <see cref="SolreignFxCueV1"/> event, runs the full receive-side trust boundary
///     (<c>TryValidateReceived</c> per spec §1.3b, PLUS this worktree's H1 receive-boundary closure
///     — see <see cref="SolreignFxReceiveGuard"/>), gates on the master kill switch and the
///     accessibility profile (<see cref="SolreignFxProfileGateSystem"/>), and acquires an atomic
///     resource lease (<see cref="SolreignFxLeaseManager"/>) for anything that passes every gate.
///
///     W2 ends at "a lease is held, on the right pooled slot, for the right (clamped) duration,
///     under the right profile" — no <c>SolreignFxCosmeticSpritePrototype</c> exists yet (W3), so
///     there is nothing to actually spawn/reposition/show here. This is the same honestly-scoped
///     "infrastructure, not renderer" boundary W1's own receipt drew for its own scaffolding.
/// </summary>
public sealed partial class SolreignFxCueSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SolreignFxProfileGateSystem _profileGate = default!;

    private SolreignFxPrototypeSource _prototypeSource = default!;
    private SolreignFxClientAnchorResolver _anchorResolver = default!;
    private SolreignFxPool _pool = default!;
    private SolreignFxLeaseManager _leaseManager = default!;
    private readonly SolreignFxDropAggregator _diagnostics = new();

    private sealed class ActiveEffect
    {
        public SolreignFxLeaseManager.Lease Lease;
        public NetEntity? EntityAnchor;
        public double ExpiresAtSeconds;

        /// <summary>
        ///     grk review M-H: a merge (spec §3: "a repeat cue on the same anchor extends/refreshes
        ///     the existing slot") is exempt from the intake cap, and each merge refreshes
        ///     <see cref="ExpiresAtSeconds"/> — a server re-sending the same (EffectId, anchor) every
        ///     frame could otherwise pin one slot "alive" indefinitely. Occupancy never floods (it's
        ///     still exactly one slot), but this worktree adds a hard lifetime ceiling anyway: no
        ///     activation may survive past <c>FirstActivatedAtSeconds + HardLifetimeCeilingSeconds</c>
        ///     regardless of how many times it gets merged/refreshed.
        /// </summary>
        public double FirstActivatedAtSeconds;
    }

    /// <summary>grk review M-H: no single activation (even continuously merge-refreshed) may live past this many seconds — 10x the largest §3 duration cap (4.0s for smoke), rounded up generously.</summary>
    private const double HardLifetimeCeilingSeconds = 40.0;

    private readonly List<ActiveEffect> _active = new();

    private sealed class ClientRawPrototypeLoader : ISolreignFxRawPrototypeLoader
    {
        private readonly IPrototypeManager _protoMan;

        public ClientRawPrototypeLoader(IPrototypeManager protoMan)
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

    public override void Initialize()
    {
        base.Initialize();

        _anchorResolver = new SolreignFxClientAnchorResolver(EntityManager, _transform);
        _prototypeSource = new SolreignFxPrototypeSource(new ClientRawPrototypeLoader(_protoMan));
        RebuildPool();

        Subs.CVar(_cfg, CCVars.SolreignFxClientIntakePerFrame,
            v => _leaseManager.IntakeCapPerFrame = Math.Clamp(v, MinIntakePerFrame, MaxIntakePerFrame), invokeImmediately: true);
        Subs.CVar(_cfg, CCVars.SolreignFxPoolCapScale, _ => RebuildPool(), invokeImmediately: false);

        _protoMan.PrototypesReloaded += OnPrototypesReloaded;
        _profileGate.ProfileChanged += OnProfileChanged;

        SubscribeNetworkEvent<SolreignFxCueV1>(OnCueReceived);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _protoMan.PrototypesReloaded -= OnPrototypesReloaded;
        _profileGate.ProfileChanged -= OnProfileChanged;

        OnSystemShutdown();
    }

    /// <summary>W3 rendering hook: fired from <see cref="Shutdown"/> — the renderer must tear down any overlay it registered with <c>IOverlayManager</c>.</summary>
    partial void OnSystemShutdown();

    /// <summary>grk review M-A: the intake-cap CVar was claimed "clamped" in comments but never actually clamped in code.</summary>
    private const int MinIntakePerFrame = 1;
    private const int MaxIntakePerFrame = 128;

    /// <summary>
    ///     (Re)builds the pool/lease manager from the current <c>solreign.fx.pool_cap_scale</c> CVar
    ///     AND the current accessibility profile's per-category <c>ConcurrentCapMultiplier</c> (spec
    ///     §4's low_vfx/cosmetic_minimal budget-scaling rows — grk review M-D: the profile table was
    ///     computed but never actually applied to pool sizing). Both scale (spec §1.5: "Pool-cap
    ///     CVars are likewise clamped to hardcoded ceilings" — clamped here to <c>[0.1, 4.0]</c>
    ///     independent of whatever the CVar system itself allows) and profile multiplier are
    ///     re-read every call, so this is safe to call from either a CVar-change callback or a
    ///     profile-change callback. Replaces the pool wholesale and drops every currently-active
    ///     lease — simple and safe since W2 has no real renderer yet for anything to visibly
    ///     interrupt; a future W3/W4 revisit can migrate active leases across a resize instead if
    ///     that ever becomes visually noticeable.
    /// </summary>
    private void RebuildPool()
    {
        var cvarScale = Math.Clamp(_cfg.GetCVar(CCVars.SolreignFxPoolCapScale), 0.1f, 4f);
        var capacities = new Dictionary<SolreignFxCategory, int>();

        foreach (SolreignFxCategory category in Enum.GetValues<SolreignFxCategory>())
        {
            var defaults = SolreignFxCategoryTable.GetDefaults(category);
            var profileMultiplier = _profileGate.CurrentBehavior(category).ConcurrentCapMultiplier;
            capacities[category] = (int) MathF.Round(defaults.ConcurrentCap * cvarScale * profileMultiplier);
        }

        // W3 note: RebuildPool() unconditionally replaces the pool/lease manager and drops every
        // currently-active lease (W2's own documented behavior, "simple and safe since W2 has no
        // real renderer yet for anything to visibly interrupt"). Now that W3 DOES have a renderer,
        // every one of those dropped leases must release its render-side resources too, or a
        // profile change / pool-cap-scale CVar change would leak entities/lights/overlays forever.
        foreach (var effect in _active)
            OnLeaseReleasedForRender(effect.Lease);

        _pool = new SolreignFxPool(capacities);
        _leaseManager = new SolreignFxLeaseManager(_pool)
        {
            IntakeCapPerFrame = Math.Clamp(_cfg.GetCVar(CCVars.SolreignFxClientIntakePerFrame), MinIntakePerFrame, MaxIntakePerFrame),
        };
        _active.Clear();

        // W3 hook (docs/specs/FX-LANGUAGE-V1-SPEC-2026-07-16.md §8 W3): the render pool's own
        // per-category entity arrays must be resized in lockstep with THIS pool's capacities, or
        // slot indices between the two would drift. Partial method — a no-op unless W3's rendering
        // partial (SolreignFxCueSystem.Rendering.cs) is compiled into this same assembly.
        OnPoolRebuilt(capacities);
    }

    /// <summary>
    ///     W3 rendering hook (spec §8 W3 file boundary, "per-primitive client render recipes"): fired
    ///     whenever <see cref="RebuildPool"/> constructs a new <see cref="SolreignFxPool"/>, with the
    ///     exact per-category capacities it just used — the render pool must mirror these 1:1 so a
    ///     (category, slotIndex) pair means the same thing on both sides. No-op if W3's rendering
    ///     partial isn't compiled in (e.g. a hypothetical build that only wants W1/W2's plumbing).
    /// </summary>
    partial void OnPoolRebuilt(IReadOnlyDictionary<SolreignFxCategory, int> capacities);

    /// <summary>
    ///     W3 rendering hook: applies pool capacities staged by <see cref="OnPoolRebuilt"/>, from a
    ///     point in the frame where spawning entities is legal. No-op unless the rendering partial
    ///     is compiled in.
    /// </summary>
    partial void ApplyPendingPoolCapacities();

    /// <summary>
    ///     W3 rendering hook: fired immediately after a cue's lease is granted/merged/recycled (spec
    ///     §2's per-primitive render recipes) — the ONLY place new visual state should be created,
    ///     since it is the one place a cue has already passed every trust-boundary/budget/profile
    ///     gate above. Re-resolving the cue's anchor position is the hook implementation's own job
    ///     (kept out of this file to avoid threading extra rendering-only data through W2's own
    ///     lease-bookkeeping method signatures).
    /// </summary>
    partial void OnLeaseAcquiredForRender(SolreignFxCueV1 cue, SolreignFxCategory category, SolreignFxLeaseManager.Lease lease);

    /// <summary>W3 rendering hook: fired whenever a lease is released (natural expiry, anchor died mid-effect, or the master kill switch's mass teardown) — the renderer must park/hide whatever it attached to this (category, slot), mirroring the exact lifetime the logical lease already enforces.</summary>
    partial void OnLeaseReleasedForRender(SolreignFxLeaseManager.Lease lease);

    /// <summary>W3 rendering hook: fired once per client frame from <see cref="FrameUpdate"/> — drives per-frame overlay animation (arc jitter, cast-ring fill progress, smoke decay) and re-positions any entity-anchored visual for anchors that moved this frame.</summary>
    partial void OnFrameTick(float frameTime);

    /// <summary>W3 rendering hook: fired for an accepted <c>transformation</c>/<c>transformation_generic</c> cue, which never touches the pool/lease machinery (spec §3: 0 pooled entities) — the burst-sprite/screen-sting cosmetic layer is entirely this hook's own responsibility, including its own lifetime bookkeeping.</summary>
    partial void OnTransformationCue(SolreignFxCueV1 cue);

    /// <summary>W3 rendering hook: fired when the master kill switch (<c>solreign.fx.cue_v1</c>) transitions to disabled — must zero every render-side resource (grk W3 round-1 review finding H2), not just the logical lease bookkeeping <see cref="OnProfileChanged"/> already clears above.</summary>
    partial void OnKillSwitchDisabled();

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        _prototypeSource.Invalidate();
    }

    private void OnProfileChanged()
    {
        // grk review H-C: the master kill switch (solreign.fx.cue_v1) is documented (spec §9) as
        // "the universal rollback for the whole v1 language at once" — flipping it back to false
        // must restore today's already-legible baseline. The ORIGINAL version of this handler only
        // ever released a lease whose CATEGORY newly computed as fully-dropped under the profile
        // table, which never fires for the kill switch itself (SolreignFxProfilePolicy has no
        // notion of "the whole system is off," only per-category profile rows) — so disabling the
        // kill switch left every already-active lease occupied until its own natural
        // duration/anchor-death expiry, instead of tearing down immediately as the "universal
        // rollback" framing promises. Checked first and unconditionally: disabling the kill switch
        // clears EVERYTHING, never partially.
        if (!_profileGate.CueSystemEnabled)
        {
            foreach (var effect in _active)
            {
                _leaseManager.Release(effect.Lease);
                OnLeaseReleasedForRender(effect.Lease);
            }

            _active.Clear();

            // W3 grk round-1 review finding H2: this branch releases every LOGICAL lease but never
            // called RebuildPool() (that only happens in the enabled branch below), so the render
            // pool's own pooled-sprite entities stayed at whatever size they were the last time the
            // switch was on — a real per-client entity/memory footprint surviving a kill-switch
            // disable, contradicting spec §9's "universal rollback... restores today's already-
            // legible baseline" framing. This hook zeroes the render-side pool the same way
            // OnPoolRebuilt already does for the disabled case, without needing a full RebuildPool().
            OnKillSwitchDisabled();
            return;
        }

        // grk review M-D: the profile table's ConcurrentCapMultiplier (low_vfx/cosmetic_minimal
        // budget scaling) was computed by SolreignFxProfilePolicy but never actually applied to
        // pool sizing — RebuildPool() re-reads it now. This also subsumes the "release anything
        // FullyDropped under the new profile" duty the original version of this method handled
        // narrowly: rebuilding wholesale clears every active lease, which trivially includes any
        // that the new profile would have dropped anyway.
        RebuildPool();
    }

    /// <summary>
    ///     grk review H-D: the per-frame INTAKE cap (<see cref="SolreignFxLeaseManager.IntakeCapPerFrame"/>)
    ///     only bounds fresh/recycled POOL activations — a hostile or buggy server could still force
    ///     unbounded per-cue work (prototype resolution, anchor world-position resolution,
    ///     TryValidateReceived's full re-validation) on every single received event before intake is
    ///     ever consulted, since intake is checked only after validation succeeds. This is a cheap,
    ///     separate ceiling on RAW received-event processing itself — checked before
    ///     <c>TryValidateReceived</c> ever runs — set generously above the activation intake cap
    ///     (4x) so it only bites under an actual flood, never during ordinary bursty play.
    /// </summary>
    private const int MaxRawReceivesPerFrameMultiplier = 4;
    private GameTick _lastReceiveFrameTick;
    private int _rawReceivesThisFrame;

    private void OnCueReceived(SolreignFxCueV1 ev)
    {
        // W4 test-visibility hook: the RAW EffectId string as it arrived on the wire, BEFORE any
        // validation/guard/CVar gate -- literal proof of wire delivery (or non-delivery), stronger
        // than AcceptedCuesForTests below (which only reflects what an honest, fully-patched
        // client chose to accept and would miss a hypothetical server bug that broadcasts a
        // DetailOnly cue to a bystander, since the honest client's own guard would silently drop
        // it before AcceptedCuesForTests ever saw it).
        RecordRawReceivedEffectIdForTests(ev.EffectId.Id);

        if (!_profileGate.CueSystemEnabled)
        {
            _diagnostics.Record("MasterDisabled", ev.CorrelationId);
            return;
        }

        if (_timing.CurTick != _lastReceiveFrameTick)
        {
            _lastReceiveFrameTick = _timing.CurTick;
            _rawReceivesThisFrame = 0;
        }

        var rawCap = _leaseManager.IntakeCapPerFrame * MaxRawReceivesPerFrameMultiplier;
        if (++_rawReceivesThisFrame > rawCap)
        {
            // Deliberately does NOT touch the drop aggregator's per-CorrelationId sampling path —
            // this is the one place a hostile flood could itself try to grow unbounded state, so
            // the counter is a single int, not a per-reason dictionary entry.
            _diagnostics.Record("RawReceiveFloodCap", ev.CorrelationId);
            return;
        }

        if (!SolreignFxCueV1.TryValidateReceived(ev, _prototypeSource, _anchorResolver, out var validated, out var validateFailure)
            || validated is null)
        {
            var classificationKnown = SolreignFxWireAllowlist.TryGetAudienceClassification(
                ev.EffectId.Id,
                out var failureClassification);
            var entityAnchorMissing = ev.EntityAnchor is { } failedEntityAnchor
                && IsExpectedLostBroadcastAnchor(failedEntityAnchor);
            var reason = SolreignFxDropDiagnosticPolicy.IsExpectedMissingAnchorLoss(
                validateFailure,
                classificationKnown,
                failureClassification,
                entityAnchorMissing)
                ? SolreignFxDropDiagnosticPolicy.ExpectedMissingBroadcastAnchorReason
                : $"Validate:{validateFailure}";

            _diagnostics.Record(reason, ev.CorrelationId);
            return;
        }

        var cue = validated;

        if (!SolreignFxWireAllowlist.TryGetAudienceClassification(cue.EffectId.Id, out var classification))
        {
            _diagnostics.Record("UnknownClassification", cue.CorrelationId);
            return;
        }

        // H1 receive-boundary closure (this worktree's obligation per the W1 receipt's disposition):
        // a DetailOnly cue must be entity-anchored to THIS client's own controlled entity, never
        // anything else. See SolreignFxReceiveGuard's remarks for why this is the strongest check
        // actually available at receipt (delivery scope itself isn't observable from the payload).
        EntityUid? resolvedAnchorEntity = null;
        if (cue.EntityAnchor is { } netEntity && TryGetEntity(netEntity, out var maybeUid))
            resolvedAnchorEntity = maybeUid;

        var guardVerdict = SolreignFxReceiveGuard.CheckDetailOnlyAnchor(
            classification, cue.Coordinates, cue.EntityAnchor, resolvedAnchorEntity, _player.LocalEntity);

        if (guardVerdict is not (SolreignFxReceiveGuard.DetailOnlyVerdict.NotApplicable or SolreignFxReceiveGuard.DetailOnlyVerdict.Legitimate))
        {
            _diagnostics.Record($"DetailOnlyGuard:{guardVerdict}", cue.CorrelationId);
            return;
        }

        RecordAcceptedCueForTests(cue);

        if (!SolreignFxCategoryTable.TryResolveCategory(cue.EffectId.Id, out var category))
        {
            _diagnostics.Record("UnknownCategory", cue.CorrelationId);
            return;
        }

        // Transformation rides SharedAppearanceSystem/GenericVisualizer (spec §3: "0 pooled
        // entities... no extra entity") — it never touches the pool/lease machinery, so it gets its
        // own one-shot rendering hook rather than the lease-acquisition one below (there is no lease
        // to acquire: SolreignFxCategoryTable.GetDefaults(Transformation) is all-zero by design).
        // W3's rendering partial owns its own tiny self-expiring bookkeeping for the burst-sprite/
        // screen-sting cosmetic layer this hook triggers.
        if (category == SolreignFxCategory.Transformation)
        {
            OnTransformationCue(cue);
            return;
        }

        var behavior = _profileGate.CurrentBehavior(category);
        if (behavior.FullyDropped)
        {
            // Spec §4.0's structural rule is what makes this safe: a fully-cosmetic category was
            // never the sole carrier of any gameplay-critical information, so dropping it here
            // degrades juice, never information.
            _diagnostics.Record("ProfileDropped", cue.CorrelationId);
            return;
        }

        var anchorKey = cue.EntityAnchor is { } entityAnchor
            ? SolreignFxAnchorKey.FromEntity(entityAnchor)
            : SolreignFxAnchorKey.FromCoordinates(cue.Coordinates!.Value);

        var now = _timing.CurTime.TotalSeconds;
        if (!_leaseManager.TryAcquire(category, cue.EffectId.Id, anchorKey, now, out var lease, out var leaseFailure))
        {
            _diagnostics.Record($"Lease:{leaseFailure}", cue.CorrelationId);
            return;
        }

        var defaults = SolreignFxCategoryTable.GetDefaults(category);
        var durationCap = defaults.DurationCapSeconds > 0f ? defaults.DurationCapSeconds : cue.Duration;
        var effectiveDuration = Math.Min(cue.Duration, durationCap);

        UpsertActiveEffect(lease, cue.EntityAnchor, now, now + effectiveDuration);

        // W3 hook: fired AFTER every trust-boundary/budget/profile gate above already passed and
        // the lease is durably recorded in _active — the one point at which it's safe to create
        // new render-side state for this activation.
        OnLeaseAcquiredForRender(cue, category, lease);
    }

    /// <summary>
    /// A broadcast may arrive after its short-lived anchor was deleted. Robust can retain the
    /// NetEntity mapping briefly while the mapped transform is already mapless/nullspace; that is
    /// the same expected lossy-delivery case as a missing mapping. Missing transforms and non-finite
    /// positions remain malformed-state warnings.
    /// </summary>
    private bool IsExpectedLostBroadcastAnchor(NetEntity anchor)
    {
        if (!TryGetEntity(anchor, out var entity) || Deleted(entity))
            return true;

        return TryComp<TransformComponent>(entity, out var transform)
               && (transform.MapUid is null || transform.MapID == MapId.Nullspace);
    }

    private void UpsertActiveEffect(SolreignFxLeaseManager.Lease lease, NetEntity? entityAnchor, double nowSeconds, double expiresAtSeconds)
    {
        foreach (var effect in _active)
        {
            if (effect.Lease.Category == lease.Category && effect.Lease.SlotIndex == lease.SlotIndex)
            {
                effect.Lease = lease;
                effect.EntityAnchor = entityAnchor;

                // grk review round-2 finding M-new-2: a RECYCLED (or fresh) claim of this slot is a
                // BRAND NEW activation, not a continuation of whatever previously occupied the
                // slot index — it must reset FirstActivatedAtSeconds to now, not inherit the prior
                // occupant's lifetime ceiling (which, since recycling only happens when the pool is
                // already full, could otherwise make the new activation expire almost immediately).
                // Only an actual MERGE (same EffectId+anchor already active in this slot) is a
                // continuation of the SAME logical activation and should keep counting from its
                // original FirstActivatedAtSeconds (spec M-H's hard-ceiling intent).
                if (lease.Kind != SolreignFxPool.ActivationKind.Merged)
                    effect.FirstActivatedAtSeconds = nowSeconds;

                // grk review M-H: a merge/refresh may extend the slot's expiry, but never past the
                // hard lifetime ceiling measured from this activation's FIRST claim of the slot —
                // caps a continuously-refreshed merge to a bounded lifetime regardless of how many
                // times it gets extended.
                var hardCeiling = effect.FirstActivatedAtSeconds + HardLifetimeCeilingSeconds;
                effect.ExpiresAtSeconds = Math.Min(expiresAtSeconds, hardCeiling);
                return;
            }
        }

        _active.Add(new ActiveEffect
        {
            Lease = lease,
            EntityAnchor = entityAnchor,
            ExpiresAtSeconds = expiresAtSeconds,
            FirstActivatedAtSeconds = nowSeconds,
        });
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // Pool sizing spawns entities and is staged rather than done inline, because RebuildPool()
        // runs from Initialize() where spawning is invalid. See _pendingPoolCapacities.
        ApplyPendingPoolCapacities();

        _leaseManager.BeginFrame();

        var now = _timing.CurTime.TotalSeconds;
        for (var i = _active.Count - 1; i >= 0; i--)
        {
            var effect = _active[i];

            // Spec §1.3b.4: anchored effects re-`TryGet` EVERY frame for their lifetime — entity
            // deleted/left PVS/moved maps mid-effect terminates and recycles the slot immediately.
            if (effect.EntityAnchor is { } ea && !_anchorResolver.TryResolveEntityWorldPosition(ea, out _))
            {
                _leaseManager.Release(effect.Lease);
                OnLeaseReleasedForRender(effect.Lease);
                _active.RemoveAt(i);
                continue;
            }

            if (now < effect.ExpiresAtSeconds)
                continue;

            _leaseManager.Release(effect.Lease);
            OnLeaseReleasedForRender(effect.Lease);
            _active.RemoveAt(i);
        }

        // W3 hook: per-frame overlay animation + moving-anchor reposition (spec §2's electrical/
        // cast_ring overlays and every entity-anchored pooled sprite need their world position
        // refreshed every frame, same as the anchor-liveness check above needs it every frame).
        OnFrameTick(frameTime);

        foreach (var summary in _diagnostics.Flush(now))
        {
            var message =
                $"[SolreignFx] {summary.Reason}: {summary.Count} cue(s) dropped in the last interval (sample correlation id {summary.SampleCorrelationId})";

            if (SolreignFxDropDiagnosticPolicy.RequiresWarning(summary.Reason))
                Log.Warning(message);
            else
                Log.Info(message);
        }
    }

    /// <summary>Test visibility only.</summary>
    internal int ActiveEffectCountForTests => _active.Count;

    /// <summary>
    ///     W4 test-visibility hook: every cue this client accepted (passed <c>TryValidateReceived</c>
    ///     AND the H1 detail-only-anchor guard), in receipt order, INCLUDING <c>Transformation</c>
    ///     category cues — which never touch <see cref="_active"/> at all (they ride
    ///     <c>GenericVisualizer</c>, spec §3's all-zero budget row), so <see cref="ActiveEffectCountForTests"/>
    ///     alone can't see them. Needed to write the confidentiality proof (spec §5.3 preview, W4
    ///     mission) without reimplementing this file's own receive pipeline inside test code.
    ///     Capacity-bounded (oldest trimmed) so a long-running client never accumulates this
    ///     unboundedly outside a test.
    /// </summary>
    internal readonly record struct AcceptedCueForTests(
        string EffectId, float Intensity, float Scale, float Duration,
        byte? PaletteIndex, byte? Phase, uint Seed, uint CorrelationId);

    internal readonly List<AcceptedCueForTests> AcceptedCuesForTests = new();
    private const int MaxAcceptedCuesForTests = 256;

    private void RecordAcceptedCueForTests(SolreignFxCueV1 cue)
    {
        AcceptedCuesForTests.Add(new AcceptedCueForTests(
            cue.EffectId.Id, cue.Intensity, cue.Scale, cue.Duration, cue.PaletteIndex, cue.Phase, cue.Seed, cue.CorrelationId));

        if (AcceptedCuesForTests.Count > MaxAcceptedCuesForTests)
            AcceptedCuesForTests.RemoveAt(0);
    }

    /// <summary>
    ///     W4 test-visibility hook: the raw <c>EffectId</c> string of EVERY <see cref="SolreignFxCueV1"/>
    ///     this client's network layer ever delivered to <see cref="OnCueReceived"/> — recorded
    ///     BEFORE the master CVar check, <c>TryValidateReceived</c>, or the H1 guard, so it is the
    ///     strongest wire-delivery proof available: it would catch a hypothetical server bug that
    ///     broadcasts a <c>DetailOnly</c> cue to a bystander even though that bystander's honest
    ///     client would then correctly drop it before <see cref="AcceptedCuesForTests"/> ever saw
    ///     it. Capacity-bounded, same rationale as <see cref="AcceptedCuesForTests"/>.
    /// </summary>
    internal readonly List<string> RawReceivedEffectIdsForTests = new();
    private const int MaxRawReceivedEffectIdsForTests = 256;

    private void RecordRawReceivedEffectIdForTests(string effectId)
    {
        RawReceivedEffectIdsForTests.Add(effectId);

        if (RawReceivedEffectIdsForTests.Count > MaxRawReceivedEffectIdsForTests)
            RawReceivedEffectIdsForTests.RemoveAt(0);
    }

    /// <summary>
    ///     Test-only reset for both W4 test-visibility lists above — lets a test start from a clean
    ///     slate immediately before the action it wants to observe, rather than relying on
    ///     <c>LastOrDefault</c>/<c>Any</c> against whatever accumulated during pool/fixture setup.
    /// </summary>
    internal void ClearTestVisibilityStateForTests()
    {
        AcceptedCuesForTests.Clear();
        RawReceivedEffectIdsForTests.Clear();
    }
}
