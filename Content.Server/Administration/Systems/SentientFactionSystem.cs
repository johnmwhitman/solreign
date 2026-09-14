using System;
using Content.Server._Solreign.Director;
using Content.Server.Chat.Systems;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server.Administration.Systems
{
    [RegisterComponent]
    public sealed partial class SentientFactionComponent : Component
    {
    }

    public sealed partial class SentientFactionSystem : EntitySystem
    {
        [Dependency] private ChatSystem _chat = default!;
        [Dependency] private EntityLookupSystem _lookup = default!;
        [Dependency] private SharedPointLightSystem _pointLight = default!;
        [Dependency] private IConfigurationManager _config = default!;

        // Bounded scratch buffer for decoding Director-supplied base64 audioData (Codex MEDIUM #6)
        // — sized to SolreignOracleSystem.MaxAudioDataLength (200_000 base64 chars), which is an
        // upper bound on decoded byte length since base64 never expands data.
        private const int MaxAudioBytes = 200_000;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<DirectorEventReceivedEvent>(OnDirectorEvent);
        }

        private void OnDirectorEvent(DirectorEventReceivedEvent args)
        {
            // Codex MEDIUM #3: this subscriber previously had no gate of its own — it fired
            // purely off whatever SolreignOracleSystem's poll loop had already queued, so
            // flipping solreign.oracle.enabled off did not stop in-flight npc_command dispatch
            // that was already queued before the flip. Gate independently and immediately before
            // acting, same as the queue-drain recheck in SolreignOracleSystem.Update().
            if (!DirectorChannel.IsReady(_config, CCVars.SolreignOracleEnabled))
                return;

            if (args.Event.action == "npc_command")
            {
                var nent = new NetEntity(args.Event.targetEntityId);
                var uid = GetEntity(nent);

                if (Exists(uid))
                {
                    _chat.TrySendInGameICMessage(uid, args.Event.command, InGameICChatType.Speak, false);

                    if (!string.IsNullOrEmpty(args.Event.audioData))
                    {
                        // Codex MEDIUM #6: Convert.FromBase64String throws FormatException on
                        // malformed input, which would propagate out of this event subscriber and
                        // interrupt the main update path for every other subscriber of the same
                        // event. A correctly HMAC-signed-but-malformed npc_command must not be
                        // able to throw here — decode defensively into a capped buffer instead.
                        var maxDecodedLength = Math.Min(args.Event.audioData.Length, MaxAudioBytes);
                        var buffer = new byte[maxDecodedLength];
                        if (!Convert.TryFromBase64String(args.Event.audioData, buffer, out var bytesWritten))
                        {
                            Robust.Shared.Log.Logger.WarningS("audio", $"Discarding malformed base64 audioData for entity {uid}.");
                        }
                        else
                        {
                            // Mock passing to Audio/VoIP components for 3D positional audio
                            Robust.Shared.Log.Logger.InfoS("audio", $"Playing 3D positional audio ({bytesWritten} bytes) for entity {uid}");

                            float amplitude = bytesWritten > 0 ? (float)buffer[0] / 255f : 0f;
                            var lights = _lookup.GetEntitiesInRange(Transform(uid).Coordinates, 10f);
                            foreach (var lightEnt in lights)
                            {
                                if (TryComp<PointLightComponent>(lightEnt, out var lightComp))
                                {
                                    _pointLight.SetRadius(lightEnt, lightComp.Radius + amplitude, lightComp);
                                    _pointLight.SetEnergy(lightEnt, lightComp.Energy + amplitude, lightComp);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
