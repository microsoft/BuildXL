// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities;
using System.Diagnostics.ContractsLight;
using System;

namespace BuildXL.Cache.Roxis.Common
{
    public abstract class CommandResult
    {
        public abstract CommandType Type { get; }

        public static CommandResult Deserialize(BuildXLReader reader)
        {
            CommandType commandType = (CommandType)reader.ReadByte();

            CommandResult? result = null;
            switch (commandType)
            {
                case CommandType.Get:
                    result = new GetResult()
                    {
                        Value = reader.ReadNullableByteArray(),
                    };

                    break;
                case CommandType.Set:
                    result = new SetResult()
                    {
                        Set = reader.ReadBoolean(),
                    };
                    break;
                case CommandType.CompareExchange:
                    result = new CompareExchangeResult()
                    {
                        Previous = reader.ReadNullableByteArray(),
                        Exchanged = reader.ReadBoolean(),
                    };
                    break;
                case CommandType.CompareRemove:
                    result = new CompareRemoveResult()
                    {
                        Value = reader.ReadNullableByteArray(),
                    };
                    break;
                case CommandType.Remove:
                    result = new RemoveResult();
                    break;
                case CommandType.PrefixEnumerate:
                    result = new PrefixEnumerateResult(reader.ReadReadOnlyList(r =>
                    {
                        var key = r.ReadNullableByteArray();
                        var value = r.ReadNullableByteArray();
                        return new KeyValuePair<ByteString, ByteString>(key, value);
                    }));

                    break;
            }
            Contract.AssertNotNull(result);

            return result;
        }

        public void Serialize(BuildXLWriter writer)
        {
            writer.Write((byte)Type);

            switch (Type)
            {
                case CommandType.Get:
                    writer.WriteNullableByteArray(((GetResult)this).Value);
                    break;
                case CommandType.Set:
                {
                    var typedResult = (SetResult)this;
                    writer.Write(typedResult.Set);
                    break;
                }
                case CommandType.CompareExchange:
                {
                    var typedResult = (CompareExchangeResult)this;
                    writer.WriteNullableByteArray(typedResult.Previous);
                    writer.Write(typedResult.Exchanged);
                    break;
                }
                case CommandType.CompareRemove:
                    writer.WriteNullableByteArray(((CompareRemoveResult)this).Value);
                    break;
                case CommandType.Remove:
                    break;
                case CommandType.PrefixEnumerate:
                    writer.WriteReadOnlyList(((PrefixEnumerateResult)this).Pairs, (w, kvp) =>
                    {
                        w.WriteNullableByteArray(kvp.Key);
                        w.WriteNullableByteArray(kvp.Value);
                    });
                    break;
            }
        }
    }

    public class GetResult : CommandResult
    {
        public override CommandType Type => CommandType.Get;

        public ByteString? Value { get; set; }
    }

    public class SetResult : CommandResult
    {
        public override CommandType Type => CommandType.Set;

        public bool Set { get; set; }
    }

    public class CompareExchangeResult : CommandResult
    {
        public override CommandType Type => CommandType.CompareExchange;

        public ByteString? Previous { get; set; }

        public bool Exchanged { get; set; }

        public CompareExchangeResult() { }

        public CompareExchangeResult(ByteString? previous, bool exchanged)
        {
            Previous = previous;
            Exchanged = exchanged;
        }
    }

    public class CompareRemoveResult : CommandResult
    {
        public override CommandType Type => CommandType.CompareRemove;

        public ByteString? Value { get; set; }
    }

    public class RemoveResult : CommandResult
    {
        public override CommandType Type => CommandType.Remove;
    }

    public class PrefixEnumerateResult : CommandResult
    {
        public override CommandType Type => CommandType.PrefixEnumerate;

        public IReadOnlyList<KeyValuePair<ByteString, ByteString>> Pairs { get; }

        public PrefixEnumerateResult(IReadOnlyList<KeyValuePair<ByteString, ByteString>> pairs)
        {
            Pairs = pairs;
        }
    }
}
