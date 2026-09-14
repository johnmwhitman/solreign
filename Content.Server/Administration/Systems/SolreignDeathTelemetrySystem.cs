using Content.Shared.Mobs;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Player;

namespace Content.Server.Administration.Systems
{
    /// <summary>
    ///     Single owner of the (ActorComponent, MobStateChangedEvent) subscription for Director
    ///     death telemetry — the entity event bus forbids two systems subscribing to the same
    ///     (component, event) pair, and both the Rivalry and Crypt channels key off the same
    ///     player-death moment. This system holds no policy of its own: it gates to actual deaths
    ///     (AGX-MERGE-PLAN.md item 1b: deaths only, not every mob-state tick) and fans out to
    ///     <see cref="SolreignRivalrySystem.ReportDeath"/> and
    ///     <see cref="SolreignCryptSystem.ReportDeath"/>, each of which applies its own kill-switch
    ///     CVar, fail-closed DirectorChannel gating, and rate limit.
    /// </summary>
    public sealed partial class SolreignDeathTelemetrySystem : EntitySystem
    {
        [Dependency] private SolreignRivalrySystem _rivalry = default!;
        [Dependency] private SolreignCryptSystem _crypt = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<ActorComponent, MobStateChangedEvent>(OnMobStateChanged);
        }

        private void OnMobStateChanged(Entity<ActorComponent> ent, ref MobStateChangedEvent args)
        {
            if (args.NewMobState != MobState.Dead)
                return;

            _rivalry.ReportDeath(ent, args.Origin);
            _crypt.ReportDeath(ent, args.Origin);
        }
    }
}
