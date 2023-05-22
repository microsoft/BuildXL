// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using ProtoBuf;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    [ProtoContract]
    public struct CheckpointLogId : IComparable<CheckpointLogId>
    {
        [ProtoMember(1)]
        public int Value { get; }

        public CheckpointLogId(int value)
        {
            Value = value;
        }

        public static CheckpointLogId InitialLogId => new(0);

        public static CheckpointLogId MaxValue => new(int.MaxValue);

        public CheckpointLogId Next() => new(Value + 1);

        public CheckpointLogId Prev() => new(Value - 1);

        public string Serialize()
        {
            return Value.ToString();
        }

        public static CheckpointLogId Deserialize(string value)
        {
            return new CheckpointLogId(int.Parse(value));
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public int CompareTo(CheckpointLogId other)
        {
            return Value.CompareTo(other.Value);
        }

        public BlockReference FirstBlock()
        {
            return new BlockReference(this, 0);
        }
    }
}
