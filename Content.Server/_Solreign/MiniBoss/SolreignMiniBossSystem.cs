using Content.Server.Administration.Logs;
using Content.Server.Chat.Systems;
using Content.Shared.Database;
using Content.Shared.Mobs;

namespace Content.Server._Solreign.MiniBoss;

/// <summary>
///     Shared death-handling for every Solreign mini-boss: on <see cref="MobState.Dead"/>, spawns the
///     configured reward at the corpse, dispatches the defeat announcement, and logs a Ledger-trace
///     admin entry. One system backs both "The Auditor Prime" and "Specimen Zero" — reskins differ only
///     in their <see cref="SolreignMiniBossComponent"/> YAML data, not in behavior.
///
///     Systems touched (binding-rule count): <see cref="ChatSystem"/> (defeat announcement) +
///     <see cref="IAdminLogManager"/> (Ledger-trace admin log) + this system's own entity
///     spawn/query plumbing.
///
///     Ledger trace (integration comment only — no <c>SeasonLedgerSystem</c> file is touched by this
///     wave; same idiom as <c>SolreignLedgerDiscrepancyRule</c>/<c>SolreignHotPotatoSystem</c>): a later
///     wave can swap <see cref="LogDefeat"/>'s admin-log call for a
///     <c>SeasonLedgerSystem.AwardStanding</c>-style hand-off (resolving the killer's account via their
///     mind, same key the Season Ledger already uses) once mini-boss trophies get their own ledger
///     category.
/// </summary>
public sealed partial class SolreignMiniBossSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private ChatSystem _chat = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SolreignMiniBossComponent, MobStateChangedEvent>(OnMiniBossStateChanged);
    }

    private void OnMiniBossStateChanged(EntityUid uid, SolreignMiniBossComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        if (component.DefeatHandled)
            return;

        component.DefeatHandled = true;

        var coords = Transform(uid).Coordinates;

        if (component.RewardPrototype is { } reward)
            Spawn(reward, coords);

        if (component.DefeatAnnouncement is { } locId)
        {
            _chat.DispatchGlobalAnnouncement(
                Loc.GetString(locId),
                SolreignMiniBossAudit.Sender,
                playSound: true,
                colorOverride: SolreignMiniBossAudit.Color);
        }

        LogDefeat(uid, component, args.Origin);
    }

    /// <summary>
    ///     Ledger trace (see class doc): admin-auditable record of the kill today, standing in for the
    ///     real Season Ledger hand-off a later wave will wire. Attribution is best-effort — an
    ///     environmental/unattributed kill still logs the boss's defeat, just without a killer name.
    /// </summary>
    private void LogDefeat(EntityUid boss, SolreignMiniBossComponent component, EntityUid? killer)
    {
        var label = string.IsNullOrEmpty(component.LedgerLabel)
            ? ToPrettyString(boss)
            : component.LedgerLabel;

        var killerName = killer is { } origin && !Deleted(origin)
            ? ToPrettyString(origin)
            : "unattributed";

        _adminLog.Add(LogType.Damaged, LogImpact.Medium,
            $"Solreign mini-boss defeated: {label} ({ToPrettyString(boss)}), killer: {killerName}");
    }
}
