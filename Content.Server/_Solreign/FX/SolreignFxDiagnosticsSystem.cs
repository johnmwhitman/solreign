using System;
using Content.Server.GameTicking.Events;
using Content.Shared._Solreign.FX;
using Content.Shared.GameTicking;
using Robust.Shared.GameObjects;
using Robust.Shared.Random;

namespace Content.Server._Solreign.FX;

/// <summary>
///     The "new, tiny <c>SolreignFxDiagnosticsSystem</c>" spec §1.3a.5 names as the real
///     implementation of <see cref="ISolreignFxCorrelationSource"/>: a server-assigned, deterministic
///     <see cref="SolreignFxCueV1.Seed"/> off <see cref="IRobustRandom"/>, and TWO independent
///     monotonic counters (broadcast-stream / targeted-stream, spec §1.3a.5, grk #6) so a bystander
///     can never observe a gap in the broadcast sequence at the exact point a session-targeted
///     detail cue was minted. Both counters reset at round start — the same "salted per round" idiom
///     the codebase's own Wingmates opaque-token system already uses (per the W1 spec citation of
///     that precedent).
/// </summary>
public sealed partial class SolreignFxDiagnosticsSystem : EntitySystem, ISolreignFxCorrelationSource
{
    [Dependency] private IRobustRandom _random = default!;

    private uint _broadcastCounter;
    private uint _targetedCounter;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    private void OnRoundStarting(RoundStartingEvent ev) => ResetCounters();

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev) => ResetCounters();

    private void ResetCounters()
    {
        _broadcastCounter = 0;
        _targetedCounter = 0;
    }

    /// <inheritdoc/>
    public uint NextSeed()
    {
        // Full 32 bits of raw entropy, never signed-Next()-derived (spec §1.2/cdx #18 — the wire
        // seed is a raw uint with a frozen mixing function; the SOURCE of that raw uint must not
        // itself introduce a signed-domain bias/overflow hazard, hence 4 raw bytes rather than
        // `(uint) _random.Next()`, which would only ever produce values in [0, int.MaxValue).
        var bytes = new byte[4];
        _random.NextBytes(bytes);
        return BitConverter.ToUInt32(bytes);
    }

    /// <inheritdoc/>
    public uint NextBroadcastCorrelationId() => unchecked(_broadcastCounter++);

    /// <inheritdoc/>
    public uint NextTargetedCorrelationId() => unchecked(_targetedCounter++);

    /// <summary>Test visibility only.</summary>
    internal (uint Broadcast, uint Targeted) CountersForTests => (_broadcastCounter, _targetedCounter);
}
