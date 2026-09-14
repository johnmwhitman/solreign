using System;
using Robust.Shared.Serialization;

namespace Content.Shared._Solreign.Library;

/// <summary>UI key for the Station Archive submission bound user interface (a plain title+body
/// form). Reading an already-materialized work uses the EXISTING <c>PaperUiKey</c> BUI on the
/// spawned book item itself — this key covers submission only.</summary>
[Serializable, NetSerializable]
public enum SolreignLibrarySubmitUiKey : byte
{
    Key = 0,
}

/// <summary>
///     Submit a written work to the Annex this BUI is open on. <see cref="Title"/> and
///     <see cref="Body"/> are both classifier-gated at write time
///     (<c>SolreignLibrarySystem.FireSubmit</c>) — nothing here is trusted client-side beyond the
///     live char-count clamp the window itself applies (the <c>SolreignNoticeboardWindow</c>
///     idiom); the server re-sanitizes and re-caps both fields regardless of what the client sent.
/// </summary>
[Serializable, NetSerializable]
public sealed class SolreignLibrarySubmitMessage : BoundUserInterfaceMessage
{
    public string Title;
    public string Body;

    public SolreignLibrarySubmitMessage(string title, string body)
    {
        Title = title;
        Body = body;
    }
}
