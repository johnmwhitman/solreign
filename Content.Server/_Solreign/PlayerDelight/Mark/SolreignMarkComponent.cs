using System;
using Robust.Shared.GameObjects;

namespace Content.Server._Solreign.PlayerDelight.Mark;

/// <summary>
///     One projected mark entity — the round-local physical representation of a <c>mark</c> ledger
///     row (docs/specs/MARK-SPEC-2026-07-17-DRAFT.md §3.3). Attached in CODE at projection/plant
///     time, never in YAML, and never networked: the examine surface is server-composed, so the
///     owner account and the planted character name (owner-private, rail 5) never leave the server.
///     The entity is disposable by design — deleting it costs one round; the DB row is the truth
///     and the next <c>StationPostInitEvent</c> re-projects it.
/// </summary>
[RegisterComponent]
public sealed partial class SolreignMarkComponent : Component
{
    /// <summary>The owning ACCOUNT (the ledger row's <c>user_id</c>) — never a mob, never a mind.</summary>
    public Guid Account;

    /// <summary>Which of the closed three kinds this projection renders.</summary>
    public MarkKind Kind;

    /// <summary>The visual stage (0..<see cref="MarkAgeRules.MaxStage"/>) this entity was projected AT — frozen for the round.</summary>
    public int Stage;

    /// <summary>
    ///     The character name as planted, cached from the ledger row at projection time so examine
    ///     never needs a store round-trip. Rendered ONLY in the owner's private examine suffix.
    /// </summary>
    public string CharacterName = string.Empty;
}
