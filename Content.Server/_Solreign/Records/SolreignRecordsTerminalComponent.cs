using Robust.Shared.GameObjects;

namespace Content.Server._Solreign.Records;

/// <summary>
///     Marker for a "PROVIDENCE Personnel Records" console — wave-2 item einstein-016. Server-only
///     (the <c>SolreignOracleComponent</c> precedent: no client-visible fields, so it never needs to
///     live in Shared). The window is a per-actor private read, not shared board state — see
///     <see cref="SolreignRecordsTerminalSystem"/> and
///     <c>Content.Shared._Solreign.Records.RecordsTerminalSnapshotEvent</c> for why.
/// </summary>
[RegisterComponent]
public sealed partial class SolreignRecordsTerminalComponent : Component
{
}
