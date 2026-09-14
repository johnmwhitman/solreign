#nullable enable
using Content.Client._Solreign.FX;
using Content.Shared._Solreign.FX;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Tests._Solreign.FX;

/// <summary>
///     H1 regression coverage — grk adversarial review finding H1 from the W1 receipt ("DetailOnly
///     audience classification never checked in TryValidateReceived... tracked as W2's obligation,
///     not W1 debt"). This worktree's receive-side closure: <see cref="SolreignFxReceiveGuard"/>
///     asserts that a <see cref="SolreignFxAudienceClassification.DetailOnly"/> cue is ALWAYS
///     entity-anchored to the receiving client's own controlled entity — anything else (a
///     coordinate anchor, no anchor, an unresolvable anchor, or an anchor resolving to someone
///     else) is a confidentiality anomaly and must be rejected, never rendered.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignFxReceiveGuard))]
public sealed class SolreignFxReceiveGuardTests
{
    private static readonly EntityUid LocalEntity = new(42);
    private static readonly EntityUid OtherEntity = new(99);
    private static readonly NetEntity SomeNetEntity = new(1);
    private static readonly NetCoordinates SomeCoordinates = new(new NetEntity(2), 1f, 1f);

    [Test]
    public void CheckDetailOnlyAnchor_BroadcastClassification_IsNotApplicable()
    {
        var verdict = SolreignFxReceiveGuard.CheckDetailOnlyAnchor(
            SolreignFxAudienceClassification.Broadcast, null, SomeNetEntity, LocalEntity, LocalEntity);

        Assert.That(verdict, Is.EqualTo(SolreignFxReceiveGuard.DetailOnlyVerdict.NotApplicable));
    }

    [Test]
    public void CheckDetailOnlyAnchor_DetailOnly_AnchoredToLocalControlledEntity_IsLegitimate()
    {
        var verdict = SolreignFxReceiveGuard.CheckDetailOnlyAnchor(
            SolreignFxAudienceClassification.DetailOnly, null, SomeNetEntity, LocalEntity, LocalEntity);

        Assert.That(verdict, Is.EqualTo(SolreignFxReceiveGuard.DetailOnlyVerdict.Legitimate));
    }

    [Test]
    public void CheckDetailOnlyAnchor_DetailOnly_CoordinateAnchored_IsRejected()
    {
        // Spec §5's only DetailOnly primitive (transformation) is always entity-anchored to the
        // actor's own body — a coordinate-anchored DetailOnly cue is structurally impossible from a
        // well-behaved server and must be rejected outright.
        var verdict = SolreignFxReceiveGuard.CheckDetailOnlyAnchor(
            SolreignFxAudienceClassification.DetailOnly, SomeCoordinates, null, null, LocalEntity);

        Assert.That(verdict, Is.EqualTo(SolreignFxReceiveGuard.DetailOnlyVerdict.RejectedCoordinateAnchored));
    }

    [Test]
    public void CheckDetailOnlyAnchor_DetailOnly_NoAnchorAtAll_IsRejected()
    {
        var verdict = SolreignFxReceiveGuard.CheckDetailOnlyAnchor(
            SolreignFxAudienceClassification.DetailOnly, null, null, null, LocalEntity);

        Assert.That(verdict, Is.EqualTo(SolreignFxReceiveGuard.DetailOnlyVerdict.RejectedNoAnchor));
    }

    [Test]
    public void CheckDetailOnlyAnchor_DetailOnly_AnchorDoesNotResolveLocally_IsRejected()
    {
        var verdict = SolreignFxReceiveGuard.CheckDetailOnlyAnchor(
            SolreignFxAudienceClassification.DetailOnly, null, SomeNetEntity, null, LocalEntity);

        Assert.That(verdict, Is.EqualTo(SolreignFxReceiveGuard.DetailOnlyVerdict.RejectedAnchorUnresolvable));
    }

    [Test]
    public void CheckDetailOnlyAnchor_DetailOnly_AnchoredToADifferentEntity_IsRejected()
    {
        // The sharpest possible confidentiality-anomaly signal: a buggy/malicious server sent a
        // secret-role detail cue anchored to someone OTHER than this receiving client.
        var verdict = SolreignFxReceiveGuard.CheckDetailOnlyAnchor(
            SolreignFxAudienceClassification.DetailOnly, null, SomeNetEntity, OtherEntity, LocalEntity);

        Assert.That(verdict, Is.EqualTo(SolreignFxReceiveGuard.DetailOnlyVerdict.RejectedAnchorMismatch));
    }

    [Test]
    public void CheckDetailOnlyAnchor_DetailOnly_NoLocalControlledEntity_IsRejected()
    {
        // A lobby/ghost/unattached client can never legitimately be the target of a secret-role
        // detail cue.
        var verdict = SolreignFxReceiveGuard.CheckDetailOnlyAnchor(
            SolreignFxAudienceClassification.DetailOnly, null, SomeNetEntity, OtherEntity, null);

        Assert.That(verdict, Is.EqualTo(SolreignFxReceiveGuard.DetailOnlyVerdict.RejectedAnchorMismatch));
    }
}
