using Content.Server._Solreign.SeasonLedger;
using Content.Shared._Solreign.Memorial;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using System.Linq;

namespace Content.Server._Solreign.Memorial;

public sealed partial class MemorialConsoleSystem : EntitySystem
{
    /// <summary>Most recent deaths shown on a memorial console.</summary>
    private const int MemorialDisplayLimit = 200;

    // SeasonLedgerStore is a plain class, not an IoC service — injecting it directly threw
    // UnregisteredDependencyException at startup. Systems reach the ledger through its
    // EntitySystem facade, same as SolreignAntagRotationGate does.
    [Dependency] private SeasonLedgerSystem _ledger = default!;
    [Dependency] private UserInterfaceSystem _uiSystem = default!;
    [Dependency] private Robust.Shared.Audio.Systems.SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MemorialConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
    }

    /// <remarks>
    ///     <para>
    ///     This handler is <c>async void</c> because the engine's event signature is void and the
    ///     ledger read is genuinely asynchronous. That makes the try/catch load-bearing rather than
    ///     defensive: an exception escaping an <c>async void</c> has no caller to receive it and
    ///     reaches the runtime as an UNHANDLED exception, which takes the server down. A player
    ///     opening a wall console must never be able to do that, and this console only became
    ///     reachable when it was placed on the seven rotation maps — before that the hazard was
    ///     real but unreachable, which is exactly how it survived review.
    ///     </para>
    ///     <para>
    ///     The entity is re-checked after the await as well. Awaiting yields to the game loop, so
    ///     the console can be deleted (round restart, explosion, admin delete) while the ledger read
    ///     is in flight, and SetUiState against a dead entity throws on a thread with no handler.
    ///     </para>
    /// </remarks>
    private async void OnUiOpened(EntityUid uid, MemorialConsoleComponent component, BoundUIOpenedEvent args)
    {
        if (args.UiKey is not MemorialConsoleUiKey)
            return;

        _audio.PlayPvs(new Robust.Shared.Audio.SoundPathSpecifier("/Audio/Effects/zzzt.ogg"), uid, Robust.Shared.Audio.AudioParams.Default.WithVolume(-4f));
        RaiseNetworkEvent(new Content.Shared._Solreign.FX.SolreignScreenFxEvent(), Robust.Shared.Player.Filter.Entities(args.Actor));

        try
        {
            // Master's GetAllFirstDeathsAsync takes an explicit cap (wave-6 predated it). A memorial
            // console is read by one player at a wall, so an unbounded read would grow without limit
            // as the season accumulates deaths; 200 is far more than anyone scrolls and keeps the
            // round-start projection cost bounded.
            var deaths = await _ledger.GetAllFirstDeathsAsync(MemorialDisplayLimit);

            // The console may have died while we were awaiting; see remarks.
            if (Deleted(uid))
                return;

            var records = deaths.Select(d => new MemorialRecord(
                d.CharacterName,
                d.Cause,
                d.ToursAtDeath,
                d.TitleAtDeath,
                d.EpitaphId,
                d.DiedAtUtc)).ToList();

            var state = new MemorialConsoleState(records);
            _uiSystem.SetUiState(uid, MemorialConsoleUiKey.Key, state);
        }
        catch (Exception e)
        {
            // Degrade to an empty memorial rather than killing the round. The player sees a
            // console with no names, which reads in-fiction as records being unavailable, and
            // the operator gets the real reason in the log.
            Log.Error($"Memorial console {ToPrettyString(uid)} failed to read the Season Ledger: {e}");

            if (Deleted(uid))
                return;

            try
            {
                _uiSystem.SetUiState(uid, MemorialConsoleUiKey.Key, new MemorialConsoleState(new List<MemorialRecord>()));
            }
            catch (Exception inner)
            {
                // Nothing further to try — swallow deliberately so the failure to REPORT a failure
                // cannot itself become the unhandled exception this whole handler exists to prevent.
                Log.Error($"Memorial console {ToPrettyString(uid)} could not present its fallback state: {inner}");
            }
        }
    }
}
