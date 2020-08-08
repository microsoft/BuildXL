// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;
using System.Diagnostics.ContractsLight;
using System;

namespace BuildXL.Cache.Roxis.Common
{
    /// <summary>
    /// Minimum unit of work that Roxis supports. There are several commands, which can be seen at the end of the file.
    /// </summary>
    public class Command
    {
        public virtual CommandType Type { get; }

        public ByteString Key { get; set; }

        public static Command Deserialize(BuildXLReader reader)
        {
            CommandType commandType = (CommandType)reader.ReadByte();
            var key = reader.ReadNullableByteArray();

            Command? command = null;
            switch (commandType)
            {
                case CommandType.Get:
                    command = new GetCommand();
                    break;
                case CommandType.Set:
                    command = new SetCommand()
                    {
                        Value = reader.ReadNullableByteArray(),
                        ExpiryTimeUtc = reader.ReadNullableStruct(r => r.ReadDateTime()),
                        Overwrite = reader.ReadBoolean(),
                    };

                    break;
                case CommandType.CompareExchange:
                    command = new CompareExchangeCommand()
                    {
                        Value = reader.ReadNullableByteArray(),
                        CompareKey = reader.ReadNullableByteArray(),
                        CompareKeyValue = reader.ReadNullableByteArray(),
                        Comparand = reader.ReadNullableByteArray(),
                        ExpiryTimeUtc = reader.ReadNullableStruct(r => r.ReadDateTime()),
                    };

                    break;
                case CommandType.CompareRemove:
                    command = new CompareRemoveCommand()
                    {
                        Comparand = reader.ReadNullableByteArray(),
                    };

                    break;
                case CommandType.Remove:
                    command = new RemoveCommand();
                    break;
                case CommandType.PrefixEnumerate:
                    command = new PrefixEnumerateCommand();
                    break;
            }

            Contract.Assert(command != null);
            command.Key = key;
            return command;
        }

        public void Serialize(BuildXLWriter writer)
        {
            writer.Write((byte)Type);
            writer.WriteNullableByteArray(Key);
            switch (Type)
            {
                case CommandType.Get:
                    break;
                case CommandType.Set:
                {
                    var typedCommand = (SetCommand)this;
                    writer.WriteNullableByteArray(typedCommand.Value);
                    writer.Write(typedCommand.ExpiryTimeUtc, (w, v) => w.Write(v));
                    writer.Write(typedCommand.Overwrite);
                    break;
                }
                case CommandType.CompareExchange:
                {
                    var typedCommand = (CompareExchangeCommand)this;
                    writer.WriteNullableByteArray(typedCommand.Value);
                    writer.WriteNullableByteArray(typedCommand.CompareKey);
                    writer.WriteNullableByteArray(typedCommand.CompareKeyValue);
                    writer.WriteNullableByteArray(typedCommand.Comparand);
                    writer.Write(typedCommand.ExpiryTimeUtc, (w, v) => w.Write(v));
                    break;
                }
                case CommandType.CompareRemove:
                    writer.WriteNullableByteArray(((CompareRemoveCommand)this).Comparand);
                    break;
                case CommandType.Remove:
                    break;
                case CommandType.PrefixEnumerate:
                    break;
            }
        }
    }

    public class GetCommand : Command
    {
        public override CommandType Type => CommandType.Get;
    }

    public class SetCommand : Command
    {
        public override CommandType Type => CommandType.Set;

        public ByteString Value;
        public DateTime? ExpiryTimeUtc;
        public bool Overwrite;
    }

    public class CompareExchangeCommand : Command
    {
        public override CommandType Type => CommandType.CompareExchange;

        public ByteString Value;
        public ByteString? CompareKey;
        public ByteString? CompareKeyValue;
        public ByteString? Comparand;
        public DateTime? ExpiryTimeUtc;
    }

    public class CompareRemoveCommand : Command
    {
        public override CommandType Type => CommandType.CompareRemove;

        public ByteString Comparand;
    }

    public class RemoveCommand : Command
    {
        public override CommandType Type => CommandType.Remove;
    }

    public class PrefixEnumerateCommand : Command
    {
        public override CommandType Type => CommandType.PrefixEnumerate;
    }
}
