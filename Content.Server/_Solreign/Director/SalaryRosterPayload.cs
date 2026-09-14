using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Server._Solreign.SeasonLedger;
using Content.Shared.GameTicking;

namespace Content.Server._Solreign.Director;

/// <summary>
///     SR-W-082: builds the OPTIONAL <c>roster</c> extension to the existing round-end payload
///     (<c>SolreignRoundOrchestrationSystem</c>'s <c>POST /api/director/round-end</c>), gated
///     end-to-end by <see cref="Content.Shared.CCVar.CCVars.SolreignSalaryEnabled"/>. Kept pure
///     and static so the payload shape and rank mapping are unit-testable without ECS, config, or
///     HTTP — the same "extract the pure part, unit-test it directly via
///     <c>InternalsVisibleTo("Content.Tests")</c>" idiom <see cref="DirectorChannel"/> already
///     uses for <see cref="DirectorChannel.BuildCanonicalEnvelope"/>.
///
///     Contract (matches the Director daemon side being built in parallel against this exact
///     shape):
///     <code>
///     { "round_number": 123, "roster": [ { "guid": "&lt;NetUserId guid string&gt;", "rank_tier": 0-9, "salary_eligible": true } ] }
///     </code>
///     CVar off =&gt; <see cref="Build"/> called with a <c>null</c> roster produces the
///     pre-SR-W-082 body with no <c>roster</c> key at all — byte-identical to what
///     <c>SolreignRoundOrchestrationSystem.OnRoundEnd</c> sent before this feature existed.
///
///     Only players who clear <see cref="IsSalaryEligible"/> AND have a resolvable account GUID
///     ever make it into the array at all (conservative-by-construction, per the backlog item);
///     <see cref="RosterEntry.SalaryEligible"/> is therefore always <c>true</c> for entries that
///     ARE emitted — it is retained per-entry (rather than collapsed away) purely for daemon-side
///     schema self-documentation/forward-compatibility, matching the field the contract specifies.
/// </summary>
internal static class SalaryRosterPayload
{
    /// <summary>
    ///     One roster entry. <see cref="Guid"/> is always the player's account
    ///     <see cref="Robust.Shared.Network.NetUserId"/> string — NEVER an entity uid.
    ///     Cross-round identity must key by account, since the entity attached to an account is
    ///     round-scoped and gets torn down every round. Mixing the two up is exactly the bug that
    ///     was already found and fixed once in rivalry telemetry ("v11 backbone #3: FIX rivalry
    ///     identity") — do not reintroduce it here.
    /// </summary>
    internal readonly record struct RosterEntry(string Guid, int RankTier, bool SalaryEligible);

    private sealed class RosterEntryDto
    {
        public string guid { get; init; } = string.Empty;
        public int rank_tier { get; init; }
        public bool salary_eligible { get; init; }
    }

    private sealed class RoundEndPayloadDto
    {
        public int round_number { get; init; }

        // WhenWritingNull: CVar-off callers pass roster: null, which must serialize with no
        // "roster" key at all — not "roster":null — so the payload is byte-identical to the
        // pre-SR-W-082 body every existing Director-side consumer/test already expects.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public RosterEntryDto[]? roster { get; init; }
    }

    /// <summary>
    ///     True only if this player both (a) has a resolvable account GUID and (b) actually
    ///     played this round. Reuses the exact same "does this round-end row have an account key"
    ///     signal <c>SeasonLedgerSystem.OnRoundEnd</c> already gates its own persistence on
    ///     (<c>info.PlayerGuid is not {} netUserId =&gt; skip, "observers / disconnected without
    ///     an account key"</c>), plus
    ///     <see cref="RoundEndMessageEvent.RoundEndPlayerInfo.Observer"/> — set true only when the
    ///     mind was ever given the observer role (<c>GameTicker.JoinAsObserver</c> /
    ///     <c>MindRoleObserver</c>), i.e. the account only ever ghosted/spectated and never took a
    ///     job this round. Conservative by construction: anything not provably "present/spawned"
    ///     this round is not eligible.
    /// </summary>
    internal static bool IsSalaryEligible(RoundEndMessageEvent.RoundEndPlayerInfo info)
    {
        return info.PlayerGuid is not null && !info.Observer;
    }

    /// <summary>
    ///     Maps the 11-value <see cref="CorporateRank"/> career ladder (0 = ProbationaryAsset ..
    ///     10 = FounderEmeritus) onto the fixed 0-9 <c>rank_tier</c> scale the contract requires,
    ///     monotonically: tier equals the rank's own ordinal index for every rank except the top
    ///     two, which both collapse onto tier 9. A strictly-increasing 11-into-10 mapping is
    ///     impossible by the pigeonhole principle; collapsing the two MOST prestigious ranks
    ///     together (rather than any lower/more-common pair) keeps every distinction that matters
    ///     for a "modest" salary band intact while staying total, simple, and non-decreasing.
    /// </summary>
    internal static int RankTier(CorporateRank rank)
    {
        var index = (int) rank;
        return index < 9 ? index : 9;
    }

    /// <summary>
    ///     Filters/maps already rank-resolved players down to the roster entries that belong in
    ///     the payload. Pure — callers do the (necessarily async) career-rank lookup themselves
    ///     and hand the resolved pairs in here, so this method (and therefore the eligibility +
    ///     exclusion behavior it implements) stays directly unit-testable without touching the
    ///     ledger store.
    /// </summary>
    internal static List<RosterEntry> BuildEligibleEntries(
        IEnumerable<(RoundEndMessageEvent.RoundEndPlayerInfo Info, CorporateRank Rank)> playersWithRank)
    {
        var entries = new List<RosterEntry>();

        foreach (var (info, rank) in playersWithRank)
        {
            if (!IsSalaryEligible(info) || info.PlayerGuid is not { } netUserId)
                continue;

            entries.Add(new RosterEntry(netUserId.UserId.ToString(), RankTier(rank), true));
        }

        return entries;
    }

    /// <summary>
    ///     Serializes the round-end body. <paramref name="roster"/> null =&gt; no <c>roster</c>
    ///     key at all (the CVar-off / pre-SR-W-082 shape); non-null (even an empty list) =&gt; the
    ///     key is present as a JSON array.
    /// </summary>
    internal static string Build(int roundNumber, IReadOnlyList<RosterEntry>? roster)
    {
        var dto = new RoundEndPayloadDto
        {
            round_number = roundNumber,
            roster = roster?.Select(r => new RosterEntryDto
            {
                guid = r.Guid,
                rank_tier = r.RankTier,
                salary_eligible = r.SalaryEligible,
            }).ToArray(),
        };

        return JsonSerializer.Serialize(dto);
    }
}
