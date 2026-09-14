using System;
using System.Collections.Generic;

namespace Content.Server._Solreign.Providence;

/// <summary>The chosen epitaph: the plate id persisted to the ledger, plus its rendered text.</summary>
public readonly record struct FirstDeathEpitaph(string Id, string Text);

/// <summary>
///     Deterministic epitaph plate picker for the authored first death — pure, zero I/O, directly
///     unit-tested (Content.Tests/_Solreign/FirstDeathEpitaphPickerTests.cs). Implements the copy
///     pack's generation rule verbatim (spec §8B / §3.5):
///       * family by tours: 0 → ORIENTATION; 1-4 → PROBATION; 5-19 → TENURE; 20+ → LEGACY
///       * cause override only when tours &gt; 0 — day-one deaths ALWAYS stay ORIENTATION
///         ("death-on-day-one is the joke"). Interpretation, flagged in the build receipt: the cause
///         plate JOINS the tours-family candidate pool when tours &gt; 0 (rather than replacing it),
///         so every plate in the library is reachable and the deterministic index rule below has the
///         "multi-plate family" it indexes over.
///       * deterministic index = (tours + title.Length + causeOrdinal) % count — same inputs → same
///         plaque, forever. It's an engraving; determinism makes it unit-testable to the character.
///       * 90-char cap on the rendered plate, falling back to the starred short variant where one
///         exists (04s/05s/07s — exactly the plates that interpolate <c>{title}</c>).
///       * <c>{name}</c> is NEVER on the plate — the crypt UI header is the headstone. The only
///         variable in the whole library is <c>{title}</c> (closed vocabulary, spec §3.3).
///
///     The plate texts live here as constants rather than in a .ftl: an epitaph is never rendered
///     through the game's localization pipeline — it is persisted (by id) into the ledger and, in the
///     FD-W3 lane, shipped as composed text to the Director daemon's crypt.
/// </summary>
public static class FirstDeathEpitaphPicker
{
    /// <summary>Rendered plates longer than this fall back to their short variant (spec §8B).</summary>
    public const int PlateMaxLength = 90;

    /// <summary>One plate: id + template (the only supported variable is <c>{title}</c>), with an
    /// optional short variant used when the rendered template exceeds <see cref="PlateMaxLength"/>.</summary>
    public readonly record struct Plate(string Id, string Template, string? ShortId = null, string? ShortTemplate = null);

    // --- Plate library (spec §8B, adopted verbatim) -------------------------------------------------

    private static readonly Plate[] Orientation =
    {
        new("01", "Day-one orientation complete."),
        new("02", "Probationary Asset. Permanently."),
    };

    private static readonly Plate[] Probation =
    {
        new("03", "Early exit. File retained."),
        new("04", "{title}. Briefly employed. Deeply filed.", "04s", "Briefly employed. Deeply filed."),
    };

    private static readonly Plate[] Tenure =
    {
        new("05", "{title}. Colleague. Accounted for.", "05s", "Colleague. Accounted for."),
        new("06", "Returned value. Then returned the rest."),
    };

    private static readonly Plate[] Legacy =
    {
        new("07", "{title}. Long service. Short ending.", "07s", "Long service. Short ending."),
        new("08", "Asset. Colleague. Still both, in the Ledger."),
    };

    private static readonly Dictionary<FirstDeathCause, Plate> CausePlates = new()
    {
        [FirstDeathCause.Violence] = new Plate("09", "Third-party liability. No names. Full honors."),
        [FirstDeathCause.Vacuum] = new Plate("10", "Out of scope for life support."),
        [FirstDeathCause.Burn] = new Plate("11", "Thermal event. Soft skills intact."),
        [FirstDeathCause.Misadventure] = new Plate("12", "Misadventure. Machinery unimpressed."),
        [FirstDeathCause.Unknown] = new Plate("13", "Cause: pending. Status: missed."),
    };

    /// <summary>
    ///     Picks and renders the epitaph plate for a first death. Deterministic: the same
    ///     (tours, cause, title) triple always yields the same plate and text.
    /// </summary>
    public static FirstDeathEpitaph Pick(int tours, FirstDeathCause cause, string title)
    {
        title ??= string.Empty;

        var pool = BuildPool(tours, cause);
        var index = ((tours + title.Length + (int) cause) % pool.Count + pool.Count) % pool.Count;
        var plate = pool[index];

        var rendered = Render(plate.Template, title);
        if (rendered.Length > PlateMaxLength && plate.ShortTemplate is { } shortTemplate)
            return new FirstDeathEpitaph(plate.ShortId!, Render(shortTemplate, title));

        return new FirstDeathEpitaph(plate.Id, rendered);
    }

    /// <summary>
    ///     Resolves a persisted plate id back to its template — lookup surface for tests (every plate
    ///     id must be resolvable) and for the FD-W3 crypt lane.
    /// </summary>
    public static bool TryGetPlateTemplate(string id, out string template)
    {
        foreach (var plate in AllPlates())
        {
            if (plate.Id == id)
            {
                template = plate.Template;
                return true;
            }

            if (plate.ShortId == id)
            {
                template = plate.ShortTemplate!;
                return true;
            }
        }

        template = string.Empty;
        return false;
    }

    /// <summary>Every plate in the library (long forms; short variants ride along on their plates) —
    /// enumeration surface for the copy-pack integrity tests (closed-vocabulary/denylist assertions).</summary>
    public static IEnumerable<Plate> AllPlates()
    {
        foreach (var plate in Orientation)
            yield return plate;
        foreach (var plate in Probation)
            yield return plate;
        foreach (var plate in Tenure)
            yield return plate;
        foreach (var plate in Legacy)
            yield return plate;
        foreach (var plate in CausePlates.Values)
            yield return plate;
    }

    private static List<Plate> BuildPool(int tours, FirstDeathCause cause)
    {
        // Day-one deaths always stay ORIENTATION — no cause override at tours == 0.
        if (tours <= 0)
            return new List<Plate>(Orientation);

        var family = tours switch
        {
            <= 4 => Probation,
            <= 19 => Tenure,
            _ => Legacy,
        };

        var pool = new List<Plate>(family) { CausePlates[cause] };
        return pool;
    }

    private static string Render(string template, string title)
    {
        return template.Replace("{title}", title);
    }
}
