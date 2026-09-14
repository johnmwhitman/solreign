using System;
using Robust.Shared.Serialization;
using Robust.Shared.GameObjects;

namespace Content.Shared.Administration.Systems
{
    [Serializable, NetSerializable]
    public enum SolreignOracleUiKey : byte
    {
        Key
    }

    [Serializable, NetSerializable]
    public sealed class SolreignOracleMessage : BoundUserInterfaceMessage
    {
        public string Message { get; }

        public SolreignOracleMessage(string message)
        {
            Message = message;
        }
    }
}
