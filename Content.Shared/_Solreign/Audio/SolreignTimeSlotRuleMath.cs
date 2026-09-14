namespace Content.Shared._Solreign.Audio;

/// <summary>
///     Pure time-slot math for <see cref="SolreignTimeSlotRule"/>. Kept free of IoC/engine types so
///     it is directly unit-testable (Content.Tests/_Solreign/SolreignTimeSlotRuleMathTests.cs), same
///     reasoning as <c>CarpComboRules</c>/<c>JudoComboRules</c>/<c>PeriodicEffectTiming</c>.
///
///     BUGFIX CONTEXT (LJ beta feedback #2, "music starts/stops at random"): stock
///     <c>ContentAudioSystem.AmbientMusic.cs</c>'s <c>UpdateAmbientMusic()</c> refuses to replay the
///     SAME <c>AmbientMusicPrototype</c> twice in a row —
///     <c>if (_musicProto == null || _musicProto == _lastMusicProto) { ...; return; }</c> — with no
///     retry other than waiting out the next random 60-120s ambience-cooldown window and asking
///     again. In any area where only ONE ambientMusic entry ever qualifies (every plain
///     <c>AlwaysTrue</c>-gated hallway/lobby track, which is most of a station), that means: the
///     fallback plays ONCE, then goes silent for the rest of the round unless the listener happens
///     to wander into a specialty zone (Medical/Maintenance/etc) and back — changing what
///     <c>_lastMusicProto</c> holds. From a player's seat, standing still in the bar, that reads
///     exactly like "music starts and stops at random."
///
///     That gate lives in Content.Client/Audio/ContentAudioSystem.AmbientMusic.cs — stock code, out
///     of this lane's assigned paths, and NOT touched here. This rule instead breaks the tie without
///     touching the stock gate: it is true only during its own SLOT of a repeating cycle. Three
///     ambientMusic entries share one priority tier and each claim a different slot of the same
///     SlotCount/SlotDuration cycle (see solreign_ambient.yml), so exactly one is ever true at once —
///     and, critically, WHICH one is true changes on its own as time passes, with no need for the
///     listener to move. Choosing SlotDuration on the same order as (or a little above) the stock
///     60-120s ambience-cooldown window bounds the worst-case silence to one or two retries instead
///     of "until you change rooms."
/// </summary>
public static class SolreignTimeSlotRuleMath
{
    /// <summary>
    ///     Which slot (0..slotCount-1) is active at <paramref name="curTime"/>. Pure function of
    ///     time, so every rule instance sharing the same SlotCount/SlotDuration agrees on the answer
    ///     without any shared mutable state.
    ///
    ///     Defensive clamps (matching JudoComboRules/CarpComboRules's YAML-misconfiguration
    ///     tolerance): a slotCount below 1 clamps to 1 (always slot 0); a non-positive slotDuration
    ///     also collapses to "always slot 0" rather than dividing by zero; a negative curTime (should
    ///     never happen from IGameTiming.CurTime, but the sibling rules classes guard the equivalent
    ///     case) clamps to zero.
    /// </summary>
    public static int CurrentSlot(TimeSpan curTime, int slotCount, TimeSpan slotDuration)
    {
        if (slotCount < 1)
            slotCount = 1;

        if (curTime < TimeSpan.Zero)
            curTime = TimeSpan.Zero;

        if (slotDuration <= TimeSpan.Zero)
            return 0;

        var elapsedSlots = curTime.Ticks / slotDuration.Ticks;
        return (int) (elapsedSlots % slotCount);
    }
}
