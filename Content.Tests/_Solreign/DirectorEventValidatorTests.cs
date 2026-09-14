using Content.Server.Administration.Systems;
using NUnit.Framework;

namespace Content.Tests._Solreign;

/// <summary>
///     Pins the v11 Director-event delivery vocabulary and its bounds: the relic base allowlist
///     must exactly mirror the daemon's vetted RELIC_BASES (orchestrator/relics.py), and
///     IsValidDirectorEvent must accept the new radio_broadcast/spawn_relic actions with bounded
///     custom fields while still rejecting the removed grief vocabulary outright.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignOracleSystem))]
public sealed class DirectorEventValidatorTests
{
    [Test]
    public void RelicAllowlist_PinsExactPrototypeSet()
    {
        var expected = new[]
        {
            "Crowbar",
            "FireExtinguisher",
            "ToolboxMechanicalFilled",
            "Welder",
            "Wrench",
            "Wirecutter",
            "FlashlightLantern",
            "ClothingHeadHatTophat",
        };

        Assert.That(SolreignOracleSystem.RelicPrototypeAllowlist, Is.EquivalentTo(expected));
    }

    [Test]
    public void IsValidDirectorEvent_AcceptsNewActionsAndBoundedCustomFields()
    {
        var radio = new DirectorEvent
        {
            action = "radio_broadcast",
            text = "All hands, report status.",
        };
        Assert.That(SolreignOracleSystem.IsValidDirectorEvent(radio), Is.True);

        var relic = new DirectorEvent
        {
            action = "spawn_relic",
            text = "Crowbar", // base-allowlist membership is enforced at spawn time, not here
            customName = new string('A', SolreignOracleSystem.MaxCustomNameLength),
            customDesc = new string('B', SolreignOracleSystem.MaxCustomDescLength),
        };
        Assert.That(SolreignOracleSystem.IsValidDirectorEvent(relic), Is.True);

        var delivery = new DirectorEvent
        {
            action = "spawn_entity",
            text = "PlushieBee",
            targetPlayerId = "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
        };
        Assert.That(SolreignOracleSystem.IsValidDirectorEvent(delivery), Is.True);
    }

    [Test]
    public void IsValidDirectorEvent_RejectsOversizedCustomFieldsAndGriefVocabulary()
    {
        var overName = new DirectorEvent
        {
            action = "spawn_relic",
            text = "Crowbar",
            customName = new string('X', SolreignOracleSystem.MaxCustomNameLength + 1),
        };
        Assert.That(SolreignOracleSystem.IsValidDirectorEvent(overName), Is.False);

        var overDesc = new DirectorEvent
        {
            action = "spawn_relic",
            text = "Crowbar",
            customDesc = new string('Y', SolreignOracleSystem.MaxCustomDescLength + 1),
        };
        Assert.That(SolreignOracleSystem.IsValidDirectorEvent(overDesc), Is.False);

        // The pay-to-grief vocabulary must stay rejected at the validator, not just unhandled.
        var dropWeapon = new DirectorEvent { EventName = "drop_weapon" };
        Assert.That(SolreignOracleSystem.IsValidDirectorEvent(dropWeapon), Is.False);

        var audiencePurchase = new DirectorEvent { type = "audience_purchase" };
        Assert.That(SolreignOracleSystem.IsValidDirectorEvent(audiencePurchase), Is.False);

        var setCvar = new DirectorEvent { action = "set_cvar" };
        Assert.That(SolreignOracleSystem.IsValidDirectorEvent(setCvar), Is.False);
    }
}
