using Robust.Shared.Audio;

namespace Content.Server._Solreign.MartialArts;

/// <summary>
///     An ancient scroll that teaches the Way of the Ornamental Carp when used in hand. One-time:
///     the scroll is consumed on a successful lesson and the student is permanently marked with
///     <see cref="SolreignMartialArtistComponent"/>. Using it while already trained refuses
///     politely and does NOT consume the scroll.
/// </summary>
[RegisterComponent, Access(typeof(SolreignMartialArtsSystem))]
public sealed partial class SolreignCarpScrollComponent : Component
{
    /// <summary>
    ///     Whether the scroll crumbles after teaching. On by default (the roadmap toy is one-time);
    ///     admins can spawn a reusable teaching copy by flipping this in VV/YAML.
    /// </summary>
    [DataField]
    public bool Consumed = true;

    /// <summary>Sound played when the lesson takes hold.</summary>
    [DataField]
    public SoundSpecifier LearnSound = new SoundPathSpecifier("/Audio/Effects/unwrap.ogg");
}
