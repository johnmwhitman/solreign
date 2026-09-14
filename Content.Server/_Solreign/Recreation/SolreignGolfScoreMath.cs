namespace Content.Server._Solreign.Recreation;

/// <summary>
/// Pure stroke-counting rules for mini golf. Kept dependency-free (no EntityUid, no components) so
/// NUnit can exercise it directly, per docs/specs/2026-07-11-recreation-spec.md's "Tests" note and
/// the ground rule that pure logic gets unit coverage. <see cref="SolreignGolfSystem"/> just reads
/// its own ECS state (the ball's current stroke count) into a plain int and calls into here.
/// </summary>
public static class SolreignGolfScoreMath
{
    /// <summary>
    /// A club successfully whacked the ball — record one more stroke. Clamps at 0 so a corrupt/negative
    /// starting count (e.g. hand-edited save data) can't go further negative instead of self-correcting.
    /// </summary>
    public static int RecordStroke(int currentStrokes)
    {
        return (currentStrokes < 0 ? 0 : currentStrokes) + 1;
    }
}
