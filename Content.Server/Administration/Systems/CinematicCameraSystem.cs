using System.Numerics;
using Content.Server._Solreign.Director;
using Content.Server.Camera;
using Content.Shared.CCVar;
using Content.Shared.Camera;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server.Administration.Systems
{
    public sealed partial class CinematicCameraSystem : EntitySystem
    {
        [Dependency] private IPlayerManager _playerManager = default!;
        [Dependency] private CameraRecoilSystem _recoilSystem = default!;
        [Dependency] private IConfigurationManager _config = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<DirectorEventReceivedEvent>(OnDirectorEvent);
        }

        private void OnDirectorEvent(DirectorEventReceivedEvent ev)
        {
            // Codex MEDIUM #3: independent gate, same rationale as SentientFactionSystem — this
            // subscriber must not act on an event that was queued before
            // solreign.oracle.enabled flipped off.
            if (!DirectorChannel.IsReady(_config, CCVars.SolreignOracleEnabled))
                return;

            if (ev.Event.action == "camera_shake")
            {
                foreach (var session in _playerManager.NetworkedSessions)
                {
                    if (session.UserId.UserId.ToString() == ev.Event.targetPlayerId && session.AttachedEntity != null)
                    {
                        var uid = session.AttachedEntity.Value;
                        EnsureComp<CameraRecoilComponent>(uid);
                        _recoilSystem.KickCamera(uid, new Vector2(2f, 2f));
                    }
                }
            }
        }
    }
}
