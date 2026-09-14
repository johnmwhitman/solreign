using System;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Solreign.Contracts;

/// <summary>
///     A Solreign Contracts Board — the wall surface where players browse, claim and skip contracts
///     (spec §3.2.1). Print/deny timing and sounds copy the upstream cargo bounty console idiom
///     (<c>CargoBountyConsoleComponent</c>). In Milestone 1 the board is driven by examine + verbs
///     (server-only); the client BUI window lands with the Milestone 2 UI pass.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class SolreignContractsBoardComponent : Component
{
    /// <summary>The paper entity spawned as a printed work order on claim/launch.</summary>
    [DataField]
    public EntProtoId WorkOrderPaperId = "Paper";

    /// <summary>The time at which the board will be able to print a work order again.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextPrintTime = TimeSpan.Zero;

    /// <summary>The time between prints.</summary>
    [DataField]
    public TimeSpan PrintDelay = TimeSpan.FromSeconds(5);

    /// <summary>The sound made when a work order prints.</summary>
    [DataField]
    public SoundSpecifier PrintSound = new SoundPathSpecifier("/Audio/Machines/printer.ogg");

    /// <summary>The sound made when a contract is skipped.</summary>
    [DataField]
    public SoundSpecifier SkipSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");

    /// <summary>The sound made when an interaction is denied (access / cooldown / rank gate).</summary>
    [DataField]
    public SoundSpecifier DenySound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_two.ogg");

    /// <summary>The time at which the board will be able to make the denial sound again.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextDenySoundTime = TimeSpan.Zero;

    /// <summary>The time between playing a denial sound.</summary>
    [DataField]
    public TimeSpan DenySoundDelay = TimeSpan.FromSeconds(2);
}
