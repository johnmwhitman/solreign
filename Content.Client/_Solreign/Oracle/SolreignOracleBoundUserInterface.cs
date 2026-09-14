using Content.Client._Solreign.Oracle.UI;
using Content.Shared.Administration.Systems;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Solreign.Oracle;

/// <summary>
///     Client half of the Solreign Directive Terminal (the Oracle front door). Copies the
///     <c>SolreignContractsBoardBoundUserInterface</c> idiom: create the window on open, translate
///     its one button press into the one BUI message. Deliberately stateless in the other
///     direction — this BUI never overrides <see cref="UpdateState"/> and the window never renders
///     a reply, because the Oracle's answer is delivered later, out of band, as a server-driven
///     popup on the player entity (see <c>SolreignOracleSystem.Update</c>'s
///     <c>_pendingOracleResponses</c> drain in Content.Server). Building reply display into this
///     window would duplicate that existing delivery path.
/// </summary>
[UsedImplicitly]
public sealed class SolreignOracleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SolreignOracleWindow? _menu;

    public SolreignOracleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<SolreignOracleWindow>();

        _menu.OnSendPressed += text => SendMessage(new SolreignOracleMessage(text));
    }
}
