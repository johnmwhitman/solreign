using Content.Server._Solreign.Pets;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(TamingRules))]
public sealed class TamingRulesTests
{
    // --- Evaluate: core state transitions ---

    [Test]
    public void NonFoodItem_IsAlwaysRejected()
    {
        // Regardless of prior tame state, a non-food item never tames.
        Assert.That(TamingRules.Evaluate(isValidFood: false, wasTamed: false, sameOwnerAsFeeder: false),
            Is.EqualTo(TamingOutcome.Rejected));
        Assert.That(TamingRules.Evaluate(isValidFood: false, wasTamed: true, sameOwnerAsFeeder: true),
            Is.EqualTo(TamingOutcome.Rejected));
    }

    [Test]
    public void FoodOnUntamedCritter_IsNewlyTamed()
    {
        Assert.That(TamingRules.Evaluate(isValidFood: true, wasTamed: false, sameOwnerAsFeeder: false),
            Is.EqualTo(TamingOutcome.NewlyTamed));
    }

    [Test]
    public void FoodFromExistingOwner_IsAlreadyBonded()
    {
        Assert.That(TamingRules.Evaluate(isValidFood: true, wasTamed: true, sameOwnerAsFeeder: true),
            Is.EqualTo(TamingOutcome.AlreadyBonded));
    }

    [Test]
    public void FoodFromDifferentFeeder_OnAlreadyTamedCritter_IsRetamed()
    {
        Assert.That(TamingRules.Evaluate(isValidFood: true, wasTamed: true, sameOwnerAsFeeder: false),
            Is.EqualTo(TamingOutcome.Retamed));
    }

    // --- ShouldAssignOwner ---

    [Test]
    public void ShouldAssignOwner_TrueForNewlyTamedAndRetamed()
    {
        Assert.That(TamingRules.ShouldAssignOwner(TamingOutcome.NewlyTamed), Is.True);
        Assert.That(TamingRules.ShouldAssignOwner(TamingOutcome.Retamed), Is.True);
    }

    [Test]
    public void ShouldAssignOwner_FalseForBondedAndRejected()
    {
        Assert.That(TamingRules.ShouldAssignOwner(TamingOutcome.AlreadyBonded), Is.False);
        Assert.That(TamingRules.ShouldAssignOwner(TamingOutcome.Rejected), Is.False);
    }

    // --- ShouldConsumeFood ---

    [Test]
    public void ShouldConsumeFood_FalseOnlyForRejected()
    {
        Assert.That(TamingRules.ShouldConsumeFood(TamingOutcome.Rejected), Is.False);
        Assert.That(TamingRules.ShouldConsumeFood(TamingOutcome.NewlyTamed), Is.True);
        Assert.That(TamingRules.ShouldConsumeFood(TamingOutcome.Retamed), Is.True);
        Assert.That(TamingRules.ShouldConsumeFood(TamingOutcome.AlreadyBonded), Is.True);
    }

    // --- sameOwnerAsFeeder is documented as ignored when wasTamed is false ---

    [Test]
    public void UntamedCritter_IgnoresSameOwnerFlag()
    {
        Assert.That(TamingRules.Evaluate(isValidFood: true, wasTamed: false, sameOwnerAsFeeder: true),
            Is.EqualTo(TamingOutcome.NewlyTamed));
    }

    // --- EvaluateRelease ---

    [Test]
    public void EvaluateRelease_TrueOnlyWhenHasOwner()
    {
        Assert.That(TamingRules.EvaluateRelease(hasOwner: true), Is.True);
        Assert.That(TamingRules.EvaluateRelease(hasOwner: false), Is.False);
    }

    // --- EvaluateFollowToggle ---

    [Test]
    public void EvaluateFollowToggle_ReturnsUntamedWhenNotTamed()
    {
        Assert.That(TamingRules.EvaluateFollowToggle(isTamed: false, currentlyFollowing: true),
            Is.EqualTo(FollowToggleOutcome.Untamed));
    }

    [Test]
    public void EvaluateFollowToggle_TogglesStateWhenTamed()
    {
        Assert.That(TamingRules.EvaluateFollowToggle(isTamed: true, currentlyFollowing: true),
            Is.EqualTo(FollowToggleOutcome.NowStaying));
        Assert.That(TamingRules.EvaluateFollowToggle(isTamed: true, currentlyFollowing: false),
            Is.EqualTo(FollowToggleOutcome.NowFollowing));
    }

    // --- EvaluateCleanup ---

    [Test]
    public void EvaluateCleanup_RejectsInvalidCleaner()
    {
        Assert.That(TamingRules.EvaluateCleanup(isValidCleaner: false, isDirty: true),
            Is.EqualTo(CleanupOutcome.RejectedItem));
    }

    [Test]
    public void EvaluateCleanup_CleansDirtyPetOrReportsAlreadyClean()
    {
        Assert.That(TamingRules.EvaluateCleanup(isValidCleaner: true, isDirty: true),
            Is.EqualTo(CleanupOutcome.Cleaned));
        Assert.That(TamingRules.EvaluateCleanup(isValidCleaner: true, isDirty: false),
            Is.EqualTo(CleanupOutcome.AlreadyClean));
    }

    // --- EvaluateCosmeticStamp ---

    [Test]
    public void EvaluateCosmeticStamp_RequiresTamedStateAndPreventsDuplicates()
    {
        Assert.That(TamingRules.EvaluateCosmeticStamp(isTamed: false, hasStamp: false),
            Is.EqualTo(CosmeticStampOutcome.NoBond));
        Assert.That(TamingRules.EvaluateCosmeticStamp(isTamed: true, hasStamp: true),
            Is.EqualTo(CosmeticStampOutcome.AlreadyStamped));
        Assert.That(TamingRules.EvaluateCosmeticStamp(isTamed: true, hasStamp: false),
            Is.EqualTo(CosmeticStampOutcome.Stamped));
    }
}
