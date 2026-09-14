using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;

namespace Content.Shared._Solreign.Memorial;

[Serializable, NetSerializable]
public sealed class MemorialConsoleState : BoundUserInterfaceState
{
    public readonly List<MemorialRecord> Records;

    public MemorialConsoleState(List<MemorialRecord> records)
    {
        Records = records;
    }
}

[Serializable, NetSerializable]
public sealed class MemorialRecord
{
    public string CharacterName;
    public string Cause;
    public int ToursAtDeath;
    public string TitleAtDeath;
    public string EpitaphId;
    public string DiedAtUtc;

    public MemorialRecord(string characterName, string cause, int toursAtDeath, string titleAtDeath, string epitaphId, string diedAtUtc)
    {
        CharacterName = characterName;
        Cause = cause;
        ToursAtDeath = toursAtDeath;
        TitleAtDeath = titleAtDeath;
        EpitaphId = epitaphId;
        DiedAtUtc = diedAtUtc;
    }
}

[Serializable, NetSerializable]
public enum MemorialConsoleUiKey : byte
{
    Key
}
