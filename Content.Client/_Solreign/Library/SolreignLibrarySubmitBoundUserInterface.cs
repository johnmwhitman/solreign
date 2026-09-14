using Content.Client._Solreign.Library.UI;
using Content.Shared._Solreign.Library;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Solreign.Library;

/// <summary>
///     Client half of the Station Archive submission form (the
///     <c>SolreignNoticeboardBoundUserInterface</c> idiom): create the window on open, translate
///     its submit action into <see cref="SolreignLibrarySubmitMessage"/>. All rules (quota, length
///     caps, the classifier gate) live server/daemon side — this window is a dumb form.
/// </summary>
[UsedImplicitly]
public sealed class SolreignLibrarySubmitBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SolreignLibrarySubmitWindow? _menu;

    public SolreignLibrarySubmitBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<SolreignLibrarySubmitWindow>();

        _menu.OnWorkSubmitted += (title, body) => SendMessage(new SolreignLibrarySubmitMessage(title, body));
    }
}
