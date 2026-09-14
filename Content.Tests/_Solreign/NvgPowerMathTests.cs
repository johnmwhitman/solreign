using Content.Shared._Solreign.NightVision;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
[TestOf(typeof(NvgPowerMath))]
public sealed class NvgPowerMathTests
{
    // The shipped tuning: PowerCellSmall (360 J) behind the standard lenses (1.2 W)
    // and the executive lenses (0.3 W). See Resources/Prototypes/_Solreign/Entities/nvg.yml.
    private const float SmallCell = 360f;
    private const float StandardDraw = 1.2f;
    private const float ExecutiveDraw = 0.3f;

    // --- RuntimeSeconds ---

    [Test]
    public void RuntimeSeconds_StandardLenses_FiveMinutes()
    {
        Assert.That(NvgPowerMath.RuntimeSeconds(SmallCell, StandardDraw), Is.EqualTo(300f).Within(1e-4));
    }

    [Test]
    public void RuntimeSeconds_ExecutiveLenses_TwentyMinutes()
    {
        Assert.That(NvgPowerMath.RuntimeSeconds(SmallCell, ExecutiveDraw), Is.EqualTo(1200f).Within(1e-3));
    }

    [Test]
    public void RuntimeSeconds_EmptyCell_IsZero()
    {
        Assert.That(NvgPowerMath.RuntimeSeconds(0f, StandardDraw), Is.EqualTo(0f));
    }

    [Test]
    public void RuntimeSeconds_NegativeCharge_IsZero()
    {
        Assert.That(NvgPowerMath.RuntimeSeconds(-5f, StandardDraw), Is.EqualTo(0f));
    }

    [Test]
    public void RuntimeSeconds_ZeroDraw_NeverDies()
    {
        Assert.That(float.IsPositiveInfinity(NvgPowerMath.RuntimeSeconds(SmallCell, 0f)), Is.True);
    }

    [Test]
    public void RuntimeSeconds_NegativeDraw_NeverDies()
    {
        Assert.That(float.IsPositiveInfinity(NvgPowerMath.RuntimeSeconds(SmallCell, -1f)), Is.True);
    }

    // --- ChargeAfter ---

    [Test]
    public void ChargeAfter_OneMinuteStandardDraw_DrainsLinearly()
    {
        // 360 J - 1.2 W * 60 s = 288 J.
        Assert.That(NvgPowerMath.ChargeAfter(SmallCell, StandardDraw, 60f), Is.EqualTo(288f).Within(1e-3));
    }

    [Test]
    public void ChargeAfter_PastEmpty_FloorsAtZero()
    {
        Assert.That(NvgPowerMath.ChargeAfter(SmallCell, StandardDraw, 600f), Is.EqualTo(0f));
    }

    [Test]
    public void ChargeAfter_ZeroSeconds_Unchanged()
    {
        Assert.That(NvgPowerMath.ChargeAfter(SmallCell, StandardDraw, 0f), Is.EqualTo(SmallCell));
    }

    [Test]
    public void ChargeAfter_NegativeSeconds_DoesNotRefund()
    {
        Assert.That(NvgPowerMath.ChargeAfter(SmallCell, StandardDraw, -60f), Is.EqualTo(SmallCell));
    }

    [Test]
    public void ChargeAfter_ZeroDraw_Unchanged()
    {
        Assert.That(NvgPowerMath.ChargeAfter(SmallCell, 0f, 600f), Is.EqualTo(SmallCell));
    }

    [Test]
    public void ChargeAfter_NegativeCharge_ClampsToZero()
    {
        Assert.That(NvgPowerMath.ChargeAfter(-10f, StandardDraw, 60f), Is.EqualTo(0f));
    }

    [Test]
    public void ChargeAfter_FullRuntime_LandsExactlyEmpty()
    {
        var runtime = NvgPowerMath.RuntimeSeconds(SmallCell, StandardDraw);
        Assert.That(NvgPowerMath.ChargeAfter(SmallCell, StandardDraw, runtime), Is.EqualTo(0f).Within(1e-3));
    }

    // --- ChargeFraction ---

    [Test]
    public void ChargeFraction_HalfCell_IsHalf()
    {
        Assert.That(NvgPowerMath.ChargeFraction(180f, SmallCell), Is.EqualTo(0.5f).Within(1e-6));
    }

    [Test]
    public void ChargeFraction_Overcharge_ClampsToOne()
    {
        Assert.That(NvgPowerMath.ChargeFraction(720f, SmallCell), Is.EqualTo(1f));
    }

    [Test]
    public void ChargeFraction_NegativeCharge_ClampsToZero()
    {
        Assert.That(NvgPowerMath.ChargeFraction(-10f, SmallCell), Is.EqualTo(0f));
    }

    [Test]
    public void ChargeFraction_ZeroCapacity_IsZero()
    {
        Assert.That(NvgPowerMath.ChargeFraction(100f, 0f), Is.EqualTo(0f));
    }

    // --- FlickerStrength (low-cell static ramp) ---

    [Test]
    public void FlickerStrength_AboveThreshold_NoStatic()
    {
        Assert.That(NvgPowerMath.FlickerStrength(0.9f, 0.25f), Is.EqualTo(0f));
    }

    [Test]
    public void FlickerStrength_AtThreshold_NoStatic()
    {
        Assert.That(NvgPowerMath.FlickerStrength(0.25f, 0.25f), Is.EqualTo(0f));
    }

    [Test]
    public void FlickerStrength_HalfwayIntoThreshold_IsHalf()
    {
        Assert.That(NvgPowerMath.FlickerStrength(0.125f, 0.25f), Is.EqualTo(0.5f).Within(1e-6));
    }

    [Test]
    public void FlickerStrength_DeadCell_IsFull()
    {
        Assert.That(NvgPowerMath.FlickerStrength(0f, 0.25f), Is.EqualTo(1f));
    }

    [Test]
    public void FlickerStrength_ZeroThreshold_DisablesRamp()
    {
        Assert.That(NvgPowerMath.FlickerStrength(0f, 0f), Is.EqualTo(0f));
    }

    [Test]
    public void FlickerStrength_NegativeFraction_ClampsToFull()
    {
        Assert.That(NvgPowerMath.FlickerStrength(-1f, 0.25f), Is.EqualTo(1f));
    }
}
