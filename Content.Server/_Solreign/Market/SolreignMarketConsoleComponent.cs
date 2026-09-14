using Robust.Shared.GameObjects;

namespace Content.Server._Solreign.Market;

/// <summary>
///     Marker for a "Requisitions Anonymous" black-market console (wallmount terminal) — a pure
///     marker + BUI anchor, same idiom as <c>SolreignOracleComponent</c>. Unlike
///     <c>SolreignContractsBoardComponent</c> (which holds local sound/timing config for a
///     station-scoped pool), the Market has no local pool to configure: every listing, every
///     price, and every purchase decision lives in the Director daemon, so there is nothing
///     round-local for this component to carry. All state lives in <see cref="SolreignMarketSystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class SolreignMarketConsoleComponent : Component
{
}
