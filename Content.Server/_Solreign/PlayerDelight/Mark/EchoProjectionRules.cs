using System;

namespace Content.Server._Solreign.PlayerDelight.Mark;

/// <summary>
///     Pure stage → prototype-id mapping for the closed 4-stage Echo table (Echoes of the
///     Departed, v14, EOD-spec §11), mirroring <see cref="MarkProjectionRules"/>. The table is
///     closed BY CONSTRUCTION: four stages, four prototype ids (<c>echo_garden.yml</c>), nothing
///     else — there is no "kind" axis for an Echo (unlike Mark's 3x4 table), since every Echo is
///     the same PROVIDENCE-authored grave-marker object at a different weathering stage.
/// </summary>
public static class EchoProjectionRules
{
    /// <summary>
    ///     The entity prototype id for a stage (<c>SolreignEcho0</c> .. <c>SolreignEcho3</c> — the
    ///     echo_garden.yml set).
    /// </summary>
    public static string PrototypeFor(int stage)
    {
        if (stage < 0 || stage > MarkAgeRules.MaxStage)
            throw new ArgumentOutOfRangeException(nameof(stage), stage, "Echo stage outside the closed 0..3 table");

        return $"SolreignEcho{stage}";
    }
}
