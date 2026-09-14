#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Antag;
using Content.Server._Solreign.Changeling;
using Content.Shared._Solreign.Changeling;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Client.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Solreign;

/// <summary>
///     System-layer coverage for <see cref="SolreignChangelingSystem"/>'s real Absorb -> Transform ->
///     Revert cycle (spec docs/specs/2026-07-11-changeling-spec.md §2.1-§2.2) and the Transform
///     fail-safe branch. Pure absorb-precondition logic already lives in
///     <c>ChangelingIdentityRulesTests</c> (Content.Tests); this file drives the actual ECS do-after +
///     instant-action path.
///
///     Absorb is started via the real <see cref="Content.Shared.DoAfter.SharedDoAfterSystem.TryStartDoAfter"/>
///     — the exact call the system's own private <c>StartAbsorbDoAfter</c> makes — rather than
///     hand-constructing a <see cref="SolreignChangelingAbsorbDoAfterEvent"/> directly (its
///     <c>.User</c>/<c>.Target</c> are computed from a <c>DoAfterEvent.DoAfter</c> back-reference the
///     real <c>DoAfterSystem</c> stamps on completion). <c>NeedHand = false</c> on this do-after means
///     no Hands-system setup is needed. Transform/Revert themselves are plain
///     <see cref="Content.Shared.Actions.InstantActionEvent"/>s with no payload, so they ARE raised
///     directly — mirroring exactly how <c>SharedActionsSystem.PerformAction</c> itself dispatches them
///     (<c>RaiseLocalEvent(target, (object) ev, broadcast: true)</c>), which is the one place upstream
///     already treats a bare action-event construction as legitimate rather than a do-after shortcut.
///
///     The target is driven into real <see cref="Content.Shared.Mobs.MobState.Critical"/> via actual
///     damage (<c>DamageableSystem.SetDamage</c> to <c>MobThresholdSystem</c>'s own crit threshold —
///     the same technique <c>DefibrillatorTest.KillAndReviveTest</c> uses), not a hand-set MobState —
///     <see cref="SolreignChangelingComponent"/>'s <c>KnownAliases</c>/<c>TrueForm</c>/<c>Transformed</c>
///     fields are <c>[Access(typeof(SolreignChangelingSystem))]</c>-locked to Read for this test class,
///     so nothing here writes them directly; every state change is driven through the real subscribers.
/// </summary>
[TestFixture]
public sealed class SolreignChangelingTransformCycleIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
        // Force a brand-new pair rather than a recycled one -- see
        // SolreignWerewolfCycleAndFailSafeIntegrationTest's doc comment for the concrete repro of a
        // reused pair carrying a leftover async Content.Server.NPC.HTN job into a later test's long
        // WaitRunTicks window. Cheap insurance against the same class of flake here too.
        Fresh = true,
    };

    private static readonly ProtoId<DamageTypePrototype> BluntDamageTypeId = "Blunt";

    [Test]
    public async Task Absorb_ThenTransform_AppliesAlias_ThenRevert_RestoresTrueForm()
    {
        var server = Server;
        var entMan = server.EntMan;
        var doAfter = server.System<SharedDoAfterSystem>();
        var changelingSys = server.System<SolreignChangelingSystem>();
        var damageable = server.System<DamageableSystem>();
        var mobThresholds = server.System<MobThresholdSystem>();

        EntityUid changelingUid = default;
        EntityUid targetUid = default;
        float absorbSeconds = 0f;

        await server.WaitPost(() =>
        {
            var mapSystem = entMan.System<SharedMapSystem>();
            mapSystem.CreateMap(out var mapId);
            var coords = new MapCoordinates(0f, 0f, mapId);

            changelingUid = entMan.SpawnEntity("MobHuman", coords);
            targetUid = entMan.SpawnEntity("MobHuman", coords);

            entMan.System<MetaDataSystem>()
                .SetEntityName(changelingUid, "Changeling Prime");
            entMan.System<MetaDataSystem>()
                .SetEntityName(targetUid, "Absorb Target");

            var changeling = entMan.EnsureComponent<SolreignChangelingComponent>(changelingUid);
            absorbSeconds = changeling.AbsorbDoAfterSeconds;

            Assert.That(changelingSys.GetKnownAliasCount(changelingUid), Is.EqualTo(0),
                "A freshly-drafted changeling must start with no absorbed identities.");

            // Real crit, same technique as DefibrillatorTest.KillAndReviveTest: damage to exactly the
            // MobThresholdSystem-reported Critical threshold, not a hand-set MobStateComponent.
            var targetDamageable = entMan.GetComponent<DamageableComponent>(targetUid);
            var critThreshold = mobThresholds.GetThresholdForState(targetUid, Content.Shared.Mobs.MobState.Critical);
            var critDamage = new DamageSpecifier(SProtoMan.Index(BluntDamageTypeId), critThreshold);
            damageable.SetDamage((targetUid, targetDamageable), critDamage);
        });

        await server.WaitRunTicks(6); // let MobThresholdSystem process the damage into a state change

        await server.WaitAssertion(() =>
        {
            var mobState = entMan.GetComponent<Content.Shared.Mobs.Components.MobStateComponent>(targetUid);
            Assert.That(mobState.CurrentState, Is.EqualTo(Content.Shared.Mobs.MobState.Critical),
                "Absorb target must actually be Critical (real damage path) before the do-after below can " +
                "succeed — ChangelingIdentityRules.CanAbsorb denies anything else.");
        });

        await server.WaitPost(() =>
        {
            var args = new DoAfterArgs(entMan, changelingUid, absorbSeconds,
                new SolreignChangelingAbsorbDoAfterEvent(), changelingUid, target: targetUid)
            {
                BreakOnMove = true,
                NeedHand = false,
            };

            Assert.That(doAfter.TryStartDoAfter(args), Is.True,
                "The real absorb do-after failed to start at all.");
        });

        await server.WaitRunTicks((int) (absorbSeconds * 30f) + 30);

        await server.WaitAssertion(() =>
        {
            Assert.That(changelingSys.GetKnownAliasCount(changelingUid), Is.EqualTo(1),
                "The real absorb do-after completed but OnAbsorbDoAfter never recorded a Known Alias.");
            Assert.That(changelingSys.TryGetKnownAliases(changelingUid, out var aliases), Is.True);
            Assert.That(aliases[0], Is.EqualTo("Absorb Target"),
                "The recorded alias's display name should be the absorbed target's captured name.");
        });

        await server.WaitPost(() =>
        {
            var ev = new SolreignChangelingTransformActionEvent { Performer = changelingUid, Handled = false };
            entMan.EventBus.RaiseLocalEvent(changelingUid, (object) ev, broadcast: true);
        });

        await server.WaitAssertion(() =>
        {
            var changeling = entMan.GetComponent<SolreignChangelingComponent>(changelingUid);
            Assert.That(changeling.Transformed, Is.True,
                "OnTransformAction should have applied the (only) known alias and set Transformed.");
            Assert.That(entMan.GetComponent<MetaDataComponent>(changelingUid).EntityName, Is.EqualTo("Absorb Target"),
                "ApplySnapshot should have renamed the changeling to the absorbed alias's captured name.");
        });

        await server.WaitPost(() =>
        {
            var ev = new SolreignChangelingRevertActionEvent { Performer = changelingUid, Handled = false };
            entMan.EventBus.RaiseLocalEvent(changelingUid, (object) ev, broadcast: true);
        });

        await server.WaitAssertion(() =>
        {
            var changeling = entMan.GetComponent<SolreignChangelingComponent>(changelingUid);
            Assert.That(changeling.Transformed, Is.False,
                "OnRevertAction should have restored TrueForm and cleared Transformed.");
            Assert.That(entMan.GetComponent<MetaDataComponent>(changelingUid).EntityName, Is.EqualTo("Changeling Prime"),
                "Revert must restore the changeling's own captured name, not leave the alias's name applied.");
        });
    }

    [Test]
    public async Task Transform_WithNoKnownAliases_FailsSafe_NoopAndStaysInTrueForm()
    {
        var server = Server;
        var entMan = server.EntMan;
        var changelingSys = server.System<SolreignChangelingSystem>();

        EntityUid changelingUid = default;

        await server.WaitPost(() =>
        {
            changelingUid = entMan.SpawnEntity("MobHuman", MapCoordinates.Nullspace);
            entMan.System<MetaDataSystem>()
                .SetEntityName(changelingUid, "Never Absorbed Anyone");
            entMan.EnsureComponent<SolreignChangelingComponent>(changelingUid);

            Assert.That(changelingSys.GetKnownAliasCount(changelingUid), Is.EqualTo(0),
                "This test's whole premise is an empty Known Aliases roster.");
        });

        await server.WaitPost(() =>
        {
            var ev = new SolreignChangelingTransformActionEvent { Performer = changelingUid, Handled = false };
            Assert.DoesNotThrow(() => entMan.EventBus.RaiseLocalEvent(changelingUid, (object) ev, broadcast: true),
                "ChangelingIdentityRules.CanTransform(0) denying the transform must fail safe (a popup and an " +
                "early return), never throw.");
        });

        await server.WaitAssertion(() =>
        {
            var changeling = entMan.GetComponent<SolreignChangelingComponent>(changelingUid);
            Assert.That(changeling.Transformed, Is.False,
                "Transform must stay a no-op with nothing in the Known Aliases roster to transform into.");
            Assert.That(entMan.GetComponent<MetaDataComponent>(changelingUid).EntityName, Is.EqualTo("Never Absorbed Anyone"),
                "A failed-safe Transform must never touch the entity's identity.");
            Assert.That(entMan.EntityExists(changelingUid), Is.True,
                "A missing-identity Transform attempt must fail safe, not crash the entity/round.");
        });
    }

    /// <summary>
    ///     W4 (FX Language v1 changeling arm-blade consumer, spec §6). The literal shipped bug: the
    ///     server networks <c>enum.SolreignChangelingVisuals.ArmBladeExtended</c> via
    ///     <see cref="Robust.Shared.GameObjects.SharedAppearanceSystem.SetData"/> but nothing consumed
    ///     it client-side (zero <c>GenericVisualizer</c> YAML anywhere, confirmed by the spec's own
    ///     grep). This test drives the REAL <see cref="SolreignChangelingArmBladeToggleActionEvent"/>
    ///     path server-side and asserts the fix on a real connected client's own
    ///     <see cref="SpriteComponent"/> — not just that the server-side appearance data changed
    ///     (W1-era coverage already existed for that indirectly), but that the client's
    ///     <c>armBladeLayer</c> sprite layer actually flips visible/hidden in lockstep with
    ///     Extend/Retract.
    ///
    ///     The actual client-side fix is
    ///     <see cref="Content.Client._Solreign.Changeling.SolreignChangelingArmBladeVisualsSystem"/>,
    ///     NOT a `GenericVisualizer` YAML component (a first attempt used exactly that, granted via
    ///     both <c>MobLing</c> and the antagSpecifier's component list — grk adversarial review
    ///     caught that `GenericVisualizerComponent` is a `Robust.Client`-only, non-networked type
    ///     the server's component factory resolves as Unknown, so a role-grant component list can
    ///     never actually deliver it to any client; only prototype-driven spawning, which a REAL
    ///     changeling never uses, happened to work — see the W4 receipt,
    ///     <c>docs/receipts/fx-w4/FX-W4-2026-07-16.md</c>, for the full account). This test uses
    ///     <c>MobLing</c> purely as a convenient pre-existing humanoid body (same one this file
    ///     already uses for Absorb/Transform/Revert coverage); the sibling test below,
    ///     <see cref="ExtendArmBlade_OnARealAntagGrantedMobHumanBody_TogglesArmBladeLayerToo"/>,
    ///     proves the fix on a body granted the antagSpecifier's REAL components the way an actual
    ///     round would.
    /// </summary>
    [Test]
    public async Task ExtendArmBlade_TogglesArmBladeLayerOnARealConnectedClient_ThenRetractHidesItAgain()
    {
        var server = Server;
        var entMan = server.EntMan;

        EntityUid changelingUid = default;
        Robust.Shared.GameObjects.NetEntity changelingNet = default;

        await server.WaitPost(() =>
        {
            var mapSystem = entMan.System<SharedMapSystem>();
            mapSystem.CreateMap(out var mapId);
            var coords = new MapCoordinates(0f, 0f, mapId);

            changelingUid = entMan.SpawnEntity("MobLing", coords);
            entMan.EnsureComponent<SolreignChangelingComponent>(changelingUid);
            changelingNet = entMan.GetNetEntity(changelingUid);

            // Reattach the pool's already-connected test client to THIS entity so its own client
            // instance actually replicates it (and, being the player's own attached entity, is
            // guaranteed in PVS regardless of position) -- same technique
            // PlayerDelightTwoClientWireIntegrationTest.PrepareBeacon already uses.
            var players = server.ResolveDependency<IPlayerManager>();
            Assert.That(ServerSession, Is.Not.Null, "This test needs the pool's connected client session.");
            Assert.That(players.SetAttachedEntity(ServerSession!, changelingUid), Is.True);
        });

        await Pair.RunTicksSync(10); // let the client observe its newly (re)attached entity

        // No armBladeLayer assertion here yet: GenericVisualizerSystem only reserves a layer
        // (LayerMapReserveBlank) reactively, the first time SolreignChangelingVisuals.ArmBladeExtended's
        // appearance DATA actually changes (Content.Server's ExtendArmBlade is the first-ever
        // SetData call for this key -- there is no spawn-time default write). So before the first
        // Extend, the layer legitimately does not exist yet on the client at all; asserting
        // "exists but hidden" here would be asserting something the engine has no reason to have
        // done yet, not a property of the fix.

        await server.WaitPost(() =>
        {
            var ev = new SolreignChangelingArmBladeToggleActionEvent { Performer = changelingUid, Handled = false };
            entMan.EventBus.RaiseLocalEvent(changelingUid, (object) ev, broadcast: true);
        });
        await Pair.RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            var clientUid = Client.EntMan.GetEntity(changelingNet);
            var spriteSystem = Client.System<SpriteSystem>();
            Assert.That(spriteSystem.LayerMapTryGet(clientUid, "armBladeLayer", out var index, false), Is.True);
            var sprite = Client.EntMan.GetComponent<SpriteComponent>(clientUid);
            Assert.That(sprite[index].Visible, Is.True,
                "Extending the arm blade must make the armBladeLayer visible on a real connected client -- this IS the literal shipped-bug fix.");
            Assert.That(sprite[index].RsiState.ToString(), Is.EqualTo("inhand-right"));
        });

        await server.WaitAssertion(() =>
        {
            var changeling = entMan.GetComponent<SolreignChangelingComponent>(changelingUid);
            Assert.That(changeling.ArmBladeExtended, Is.True,
                "The server-side flag (the mechanic that already worked pre-W4) must still flip too.");
        });

        await server.WaitPost(() =>
        {
            var ev = new SolreignChangelingArmBladeToggleActionEvent { Performer = changelingUid, Handled = false };
            entMan.EventBus.RaiseLocalEvent(changelingUid, (object) ev, broadcast: true);
        });
        await Pair.RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            var clientUid = Client.EntMan.GetEntity(changelingNet);
            var spriteSystem = Client.System<SpriteSystem>();
            Assert.That(spriteSystem.LayerMapTryGet(clientUid, "armBladeLayer", out var index, false), Is.True);
            var sprite = Client.EntMan.GetComponent<SpriteComponent>(clientUid);
            Assert.That(sprite[index].Visible, Is.False, "Retracting must hide the armBladeLayer again.");
        });
    }

    /// <summary>
    ///     Regression coverage for a bug grk adversarial review caught in this same worktree: a
    ///     first attempt at the fix above granted a <c>GenericVisualizer</c> component via
    ///     <c>Resources/Prototypes/Roles/Antags/changeling.yml</c>'s antagSpecifier — which only
    ///     ever worked for the <c>MobLing</c> preview entity, because a REAL changeling is granted
    ///     its components via <see cref="AntagSelectionSystem.AssignAntagComponents"/> onto
    ///     whatever body it already had (here, a plain <c>MobHuman</c>), NOT by spawning a
    ///     dedicated species entity — and <c>GenericVisualizerComponent</c> is a
    ///     <c>Robust.Client</c>-only, non-networked type that a server-side component grant can
    ///     never actually deliver to any client (see the W4 receipt for the full account). This
    ///     test proves the ACTUAL fix
    ///     (<see cref="Content.Client._Solreign.Changeling.SolreignChangelingArmBladeVisualsSystem"/>,
    ///     which reacts to the appearance flag on ANY entity, no component grant required) works on
    ///     exactly the kind of body a real changeling actually has.
    /// </summary>
    [Test]
    public async Task ExtendArmBlade_OnARealAntagGrantedMobHumanBody_TogglesArmBladeLayerToo()
    {
        var server = Server;
        var entMan = server.EntMan;

        EntityUid changelingUid = default;
        Robust.Shared.GameObjects.NetEntity changelingNet = default;

        await server.WaitPost(() =>
        {
            var mapSystem = entMan.System<SharedMapSystem>();
            mapSystem.CreateMap(out var mapId);
            var coords = new MapCoordinates(0f, 0f, mapId);

            // A plain MobHuman -- NOT MobLing -- with the real antagSpecifier's component grant
            // applied exactly the way the real round rule does it (AntagSelectionSystem itself
            // calls this same method, Content.Server/Antag/AntagSelectionSystem.cs line ~784).
            changelingUid = entMan.SpawnEntity("MobHuman", coords);
            var antagSelection = entMan.System<AntagSelectionSystem>();
            antagSelection.AssignAntagComponents(changelingUid, "Changeling");

            // AssignAntagComponents grants ChangelingDevour/ChangelingIdentity/ChangelingTransform
            // etc. -- SolreignChangelingComponent itself is added separately by the round rule
            // elsewhere in the real flow, same as this file's other tests already model.
            entMan.EnsureComponent<SolreignChangelingComponent>(changelingUid);
            changelingNet = entMan.GetNetEntity(changelingUid);

            var players = server.ResolveDependency<IPlayerManager>();
            Assert.That(ServerSession, Is.Not.Null, "This test needs the pool's connected client session.");
            Assert.That(players.SetAttachedEntity(ServerSession!, changelingUid), Is.True);
        });

        await Pair.RunTicksSync(10);

        await server.WaitPost(() =>
        {
            var ev = new SolreignChangelingArmBladeToggleActionEvent { Performer = changelingUid, Handled = false };
            entMan.EventBus.RaiseLocalEvent(changelingUid, (object) ev, broadcast: true);
        });
        await Pair.RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            var clientUid = Client.EntMan.GetEntity(changelingNet);
            var spriteSystem = Client.System<SpriteSystem>();
            Assert.That(spriteSystem.LayerMapTryGet(clientUid, "armBladeLayer", out var index, false), Is.True,
                "The armBladeLayer must appear on a REAL antag-granted MobHuman body, not just the MobLing preview entity.");
            var sprite = Client.EntMan.GetComponent<SpriteComponent>(clientUid);
            Assert.That(sprite[index].Visible, Is.True);
            Assert.That(sprite[index].RsiState.ToString(), Is.EqualTo("inhand-right"));
        });
    }
}
