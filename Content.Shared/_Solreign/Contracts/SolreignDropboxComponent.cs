using Robust.Shared.Audio;
using Robust.Shared.GameObjects;

namespace Content.Shared._Solreign.Contracts;

/// <summary>
///     A Solreign Fulfillment Dropbox — the "pneumatic compliance chute" (spec §3.2.2). Hitting it with a
///     deliverable that matches one of your claimed contracts consumes the item, ticks the contract's
///     progress, plays a cheerful chime and — on completion — pays out instantly. The Company is already
///     proud of you.
/// </summary>
[RegisterComponent]
public sealed partial class SolreignDropboxComponent : Component
{
    /// <summary>The cheerful chime played when a deposit is accepted.</summary>
    [DataField]
    public SoundSpecifier AcceptSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");

    /// <summary>The buzz played when a held item matches none of the depositor's contracts.</summary>
    [DataField]
    public SoundSpecifier RejectSound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_two.ogg");
}
