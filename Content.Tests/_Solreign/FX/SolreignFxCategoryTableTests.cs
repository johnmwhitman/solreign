#nullable enable
using Content.Shared._Solreign.FX;
using NUnit.Framework;

namespace Content.Tests._Solreign.FX;

/// <summary>
///     <see cref="SolreignFxCategoryTable"/>'s effect-id → category mapping is total over the exact
///     10-id <see cref="SolreignFxWireAllowlist.V1"/> set W1 shipped — every pool/lease/budget/
///     profile-policy lookup in W2 depends on this never silently missing an allowlisted id.
/// </summary>
[TestFixture]
[TestOf(typeof(SolreignFxCategoryTable))]
public sealed class SolreignFxCategoryTableTests
{
    [Test]
    public void TryResolveCategory_EveryAllowlistedId_Resolves()
    {
        foreach (var id in SolreignFxWireAllowlist.V1)
        {
            Assert.That(SolreignFxCategoryTable.TryResolveCategory(id, out _), Is.True, $"'{id}' is a member of SolreignFxWireAllowlist.V1 but has no category mapping");
        }
    }

    [Test]
    public void TryResolveCategory_UnknownId_ReturnsFalseWithoutThrowing()
    {
        Assert.That(SolreignFxCategoryTable.TryResolveCategory("not_a_real_id", out _), Is.False);
        Assert.That(SolreignFxCategoryTable.TryResolveCategory(null, out _), Is.False);
        Assert.That(SolreignFxCategoryTable.TryResolveCategory("", out _), Is.False);
    }

    [Test]
    public void TryResolveCategory_TransformationAndItsGenericVariant_MapToTheSameCategory()
    {
        SolreignFxCategoryTable.TryResolveCategory("transformation", out var detail);
        SolreignFxCategoryTable.TryResolveCategory("transformation_generic", out var generic);

        Assert.That(detail, Is.EqualTo(generic));
        Assert.That(detail, Is.EqualTo(SolreignFxCategory.Transformation));
    }

    [Test]
    public void GetDefaults_TransformationRow_IsAllZeroPerSpec()
    {
        var defaults = SolreignFxCategoryTable.GetDefaults(SolreignFxCategory.Transformation);

        Assert.Multiple(() =>
        {
            Assert.That(defaults.EntitiesPerCue, Is.EqualTo(0));
            Assert.That(defaults.ConcurrentCap, Is.EqualTo(0));
            Assert.That(defaults.LightsPerCue, Is.EqualTo(0));
        });
    }

    [Test]
    public void GetDefaults_EveryNonTransformationCategory_HasAPositiveConcurrentCap()
    {
        foreach (SolreignFxCategory category in System.Enum.GetValues<SolreignFxCategory>())
        {
            if (category == SolreignFxCategory.Transformation)
                continue;

            var defaults = SolreignFxCategoryTable.GetDefaults(category);
            Assert.That(defaults.ConcurrentCap, Is.GreaterThan(0), $"{category} should carry a real, positive concurrent cap per spec §3's table");
        }
    }

    [TestCase(SolreignFxCategory.ImpactLight, 1, 24, 0, 0.4f)]
    [TestCase(SolreignFxCategory.ImpactHeavy, 1, 16, 1, 0.8f)]
    [TestCase(SolreignFxCategory.Electrical, 1, 12, 1, 1.2f)]
    [TestCase(SolreignFxCategory.Dust, 6, 60, 0, 3.0f)]
    [TestCase(SolreignFxCategory.Smoke, 3, 30, 0, 4.0f)]
    [TestCase(SolreignFxCategory.CastRing, 1, 8, 1, 2.5f)]
    [TestCase(SolreignFxCategory.StaminaBreak, 1, 24, 1, 0.6f)]
    public void GetDefaults_MatchesSpecTableVerbatim(SolreignFxCategory category, int entities, int cap, int lights, float duration)
    {
        var defaults = SolreignFxCategoryTable.GetDefaults(category);

        Assert.Multiple(() =>
        {
            Assert.That(defaults.EntitiesPerCue, Is.EqualTo(entities));
            Assert.That(defaults.ConcurrentCap, Is.EqualTo(cap));
            Assert.That(defaults.LightsPerCue, Is.EqualTo(lights));
            Assert.That(defaults.DurationCapSeconds, Is.EqualTo(duration).Within(1e-6));
        });
    }
}
