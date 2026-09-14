using System;
using System.Collections.Generic;
using Content.Server._Solreign.PlayerDelight.Mark;
using Content.Server._Solreign.SeasonLedger;
using Content.Server._Solreign.Social;

namespace Content.Server._Solreign.Records;

/// <summary>
///     Raw ledger facts for one account, bundled by
///     <c>SeasonLedgerSystem.GetRecordsTerminalDataAsync</c> — everything the Personnel Records
///     Terminal needs, gathered through the ONE store the ledger's thin-delegation idiom mandates
///     (<c>SeasonLedgerSystem.SocialFirsts.cs</c>/<c>.FirstDeath.cs</c>/<c>.Mark.cs</c> doc comments:
///     "other systems reach the ledger through this system rather than standing up a second
///     SeasonLedgerStore against the same SQLite file").
/// </summary>
public readonly record struct RecordsTerminalLedgerSnapshot(
    string DisplayTitle,
    int Tours,
    int RankIndex,
    IReadOnlyList<string> SocialFirstFlags,
    FirstDeathRecord? FirstDeath,
    MarkRecord? Mark);

/// <summary>
///     The DECIDED content of a Personnel Records Terminal read — which facts are present, in what
///     form — separated from its final rendered TEXT the same way <c>MarkCopy</c> separates key
///     selection from <c>Loc.GetString</c>: this struct picks WHAT to show; the ECS layer
///     (<see cref="SolreignRecordsTerminalSystem"/>) is the only thing with <c>Loc</c> access, so it
///     owns turning the plan into the actual .ftl-templated strings that cross the wire. Keeping the
///     split makes every branch below unit-testable without spinning up localization or an entity.
/// </summary>
public readonly record struct RecordsTerminalPlan(
    string DisplayTitle,
    int Tours,
    int RankIndex,
    IReadOnlyList<string> CelebratedSocialFirstFlags,
    bool HasFirstDeath,
    bool HasMark,
    MarkKind MarkKind,
    int MarkStage);

/// <summary>
///     Pure, unit-testable planning logic for the Personnel Records Terminal (wave-2 item
///     einstein-016). No ECS, no I/O, no Loc — mirrors <see cref="TitleRules"/> /
///     <see cref="PersonnelFileRules"/> / <c>ContractBoardView</c>'s "pure decision, dumb render"
///     split. Every branch here is about HONESTY: a field the ledger has no data for renders as an
///     honest absence (empty list / <c>false</c> / no mark), never a fabricated zero or placeholder.
/// </summary>
public static class RecordsTerminalRenderer
{
    /// <summary>
    ///     The closed, ordered subset of <c>social_firsts</c> flags this terminal celebrates. Deliberately
    ///     excludes <see cref="SolreignSocialFirstFlags.WingmatePrompt"/> — that flag is an internal
    ///     once-ever PROMPT marker (SolreignSocialFirstsSystem's <c>Copy</c> table has no celebratory
    ///     reason/chat pair for it), not a personal milestone with its own ceremony copy; surfacing it
    ///     here would either need invented copy (dishonest) or a raw flag id (un-templated). Order is
    ///     fixed so the terminal's social-firsts section never reorders between reads.
    /// </summary>
    public static readonly string[] CelebratedFlagOrder =
    {
        SolreignSocialFirstFlags.ChirpAnswered,
        SolreignSocialFirstFlags.HealedByAnother,
        SolreignSocialFirstFlags.ItemReceived,
    };

    /// <summary>
    ///     Builds the content plan for one account's terminal read. <paramref name="nowUtc"/> is
    ///     injected (never <c>DateTime.UtcNow</c> read internally) so mark-stage computation is
    ///     deterministic and testable — the <see cref="MarkAgeRules"/> convention throughout the Mark
    ///     feature.
    /// </summary>
    public static RecordsTerminalPlan BuildPlan(RecordsTerminalLedgerSnapshot data, DateTime nowUtc)
    {
        var celebrated = new List<string>();
        foreach (var flag in CelebratedFlagOrder)
        {
            if (Contains(data.SocialFirstFlags, flag))
                celebrated.Add(flag);
        }

        var hasMark = false;
        var kind = MarkKind.Sapling;
        var stage = 0;

        if (data.Mark is { } mark)
        {
            // Corrupt/unparseable rows render as "no mark" rather than guessing a kind or stage — the
            // "never fake numbers" rail. In practice every row is written by MarkKinds.ToLedgerString
            // and MarkAgeRules' own "o" format, so this only ever fires against hand-edited/corrupt data.
            if (MarkKinds.TryParse(mark.Kind, out kind) &&
                MarkAgeRules.TryParseLedgerUtc(mark.PlantedUtc, out var planted))
            {
                hasMark = true;
                stage = MarkAgeRules.StageAt(planted, nowUtc);
            }
        }

        return new RecordsTerminalPlan(
            data.DisplayTitle,
            data.Tours,
            data.RankIndex,
            celebrated,
            data.FirstDeath is not null,
            hasMark,
            kind,
            stage);
    }

    private static bool Contains(IReadOnlyList<string> haystack, string needle)
    {
        for (var i = 0; i < haystack.Count; i++)
        {
            if (string.Equals(haystack[i], needle, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
