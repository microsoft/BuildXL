// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using ProtoBuf;
using ProtoBuf.Grpc.Configuration;
using ProtoBuf.Meta;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    public static class MetadataServiceSerializer
    {
        public static BinderConfiguration BinderConfiguration { get; } = CreateBinderConfiguration();

        public static RuntimeTypeModel TypeModel { get; } = CreateRuntimeTypeModel();

        private static BinderConfiguration CreateBinderConfiguration()
        {
            return BinderConfiguration.Create(new[] { ProtoBufMarshallerFactory.Create(CreateRuntimeTypeModel(), ProtoBufMarshallerFactory.Options.None) });
        }

        public static RuntimeTypeModel CreateRuntimeTypeModel()
        {
            var model = RuntimeTypeModel.Create(nameof(MetadataServiceSerializer) + ".TypeModel");

            // Surrogates can't use lambdas as translation functions for some reason
            model.SetSurrogate<ShortHash, (uint, uint, uint)>(ShortHashToUIntTuple, UIntTupleToShortHash);
            model.SetSurrogate<MachineId, int>(MachineIdToInt, IntToMachineId);
            model.SetSurrogate<ShortHashWithSize, (ShortHash hash, long size)>(ShortHashWithSizeToSurrogate, SurrogateToShortHashWithSize);
            model.SetSurrogate<ContentLocationEntry, (IReadOnlyCollection<MachineId> locations, long size)>(ContentLocationEntryToSurrogate, SurrogateToContentLocationEntry);

            model.SetSurrogate<Format<uint>, uint>(dataFormat: DataFormat.FixedSize);
            model.SetSurrogate<Format<ulong>, ulong>(dataFormat: DataFormat.FixedSize);

            model.SetSurrogate<ContentHash, HashSurrogate>();
            model.SetSurrogate<Fingerprint, HashSurrogate>();

            model.SetSurrogate<StrongFingerprint, (Fingerprint, Selector)>(Convert, Convert);
            model.SetSurrogate<Selector, (ContentHash, byte[])>(Convert, Convert);

            model.SetSurrogate<MachineLocation, string>(Convert, Convert);
            model.SetSurrogate<Format<BitMachineIdSet>, (byte[] data, int offset)>(Convert, Convert);

            return model;
        }

        private static Format<BitMachineIdSet> Convert((byte[] data, int offset) value)
        {
            if (value.data == null)
            {
                return null;
            }

            return new BitMachineIdSet(value.data, value.offset);
        }

        private static (byte[] data, int offset) Convert(Format<BitMachineIdSet> value)
        {
            return (value.Value?.Data, value.Value?.Offset ?? 0);
        }

        private static MachineLocation Convert(string value)
        {
            return new MachineLocation(value);
        }

        private static string Convert(MachineLocation value)
        {
            return value.Path;
        }

        private static ContentLocationEntry SurrogateToContentLocationEntry((IReadOnlyCollection<MachineId> locations, long size) t)
        {
            var machines = t.locations == null || t.locations.Count == 0
                ? MachineIdSet.Empty
                : MachineIdSet.Empty.Add(t.locations.ToArray());
            return ContentLocationEntry.Create(machines, t.size, DateTime.UtcNow);
        }

        private static (IReadOnlyCollection<MachineId> Locations, long ContentSize) ContentLocationEntryToSurrogate(ContentLocationEntry s)
        {
            return (s?.Locations, s?.ContentSize ?? 0);
        }

        private static ShortHashWithSize SurrogateToShortHashWithSize((ShortHash hash, long size) t)
        {
            return new ShortHashWithSize(t.hash, t.size);
        }

        private static (ShortHash Hash, long Size) ShortHashWithSizeToSurrogate(ShortHashWithSize s)
        {
            return (s.Hash, s.Size);
        }

        private static MachineId IntToMachineId(int index)
        {
            return new MachineId(index);
        }

        private static int MachineIdToInt(MachineId machineId)
        {
            return machineId.Index;
        }

        private static ShortHash UIntTupleToShortHash((uint part0, uint part1, uint part2) tuple)
        {
            Span<ShortHash> hashSpan = stackalloc ShortHash[1];
            var parts = MemoryMarshal.Cast<ShortHash, uint>(hashSpan);
            parts[0] = tuple.part0;
            parts[1] = tuple.part1;
            parts[2] = tuple.part2;
            return hashSpan[0];
        }

        private static (uint, uint, uint) ShortHashToUIntTuple(ShortHash hash)
        {
            Span<ShortHash> hashSpan = stackalloc ShortHash[1];
            hashSpan[0] = hash;
            var parts = MemoryMarshal.Cast<ShortHash, uint>(hashSpan);
            return (parts[0], parts[1], parts[2]);
        }

        private static (ContentHash hash, byte[] output) Convert(Selector selector)
        {
            return (selector.ContentHash, selector.Output);
        }

        private static Selector Convert((ContentHash hash, byte[] output) value)
        {
            return new Selector(value.hash, value.output);
        }

        private static (Fingerprint weakFingerprint, Selector selector) Convert(StrongFingerprint value)
        {
            return (value.WeakFingerprint, value.Selector);
        }

        private static StrongFingerprint Convert((Fingerprint weakFingerprint, Selector selector) value)
        {
            return new StrongFingerprint(value.weakFingerprint, value.selector);
        }

        public static void SerializeWithLengthPrefix<T>(Stream stream, T value)
        {
            TypeModel.SerializeWithLengthPrefix(stream, value, typeof(T), PrefixStyle.Fixed32, default);
        }

        public static T DeserializeWithLengthPrefix<T>(Stream stream)
        {
            return (T)TypeModel.DeserializeWithLengthPrefix(stream, null, typeof(T), PrefixStyle.Fixed32, default);
        }

        [ProtoContract]
        [StructLayout(LayoutKind.Explicit)]
        private struct HashSurrogate
        {
            // Header byte for length or hash type

            [ProtoMember(1)]
            [FieldOffset(0)]
            public byte Header;

            // The next 4 ulongs and byte (33 bytes) overlap with ReadOnlyFixedBytes

            [ProtoMember(2)]
            [FieldOffset(1)]
            public Format<ulong> BytesPart0;

            [ProtoMember(3)]
            [FieldOffset(1 + 8 * 1)]
            public Format<ulong> BytesPart1;

            [ProtoMember(4)]
            [FieldOffset(1 + 8 * 2)]
            public Format<ulong> BytesPart2;

            [ProtoMember(5)]
            [FieldOffset(1 + 8 * 3)]
            public Format<ulong> BytesPart3;

            [ProtoMember(6)]
            [FieldOffset(1 + 8 * 4)]
            public byte BytesPart4;

            [FieldOffset(1)]
            public ReadOnlyFixedBytes Bytes;

            public static implicit operator Fingerprint(HashSurrogate value)
            {
                return new Fingerprint(value.Bytes, value.Header);
            }

            public static implicit operator HashSurrogate(Fingerprint value)
            {
                return new HashSurrogate()
                {
                    Bytes = value.ToFixedBytes(),
                    Header = (byte)value.Length
                };
            }

            public static implicit operator ContentHash(HashSurrogate value)
            {
                return ContentHash.FromFixedBytes((HashType)value.Header, value.Bytes);
            }

            public static implicit operator HashSurrogate(ContentHash value)
            {
                var result = new HashSurrogate()
                {
                    Bytes = value.ToFixedBytes(),
                    Header = (byte)value.HashType
                };

                return result;
            }

        }
    }

    /// <summary>
    /// Defines wrapper which allows custom defined serialization/surrogate to work around implicit behavior
    /// for protobuf-net for a given type.
    /// </summary>
    /// <typeparam name="T">the wrapped type</typeparam>
    public struct Format<T>
    {
        public T Value;

        public static implicit operator T(Format<T> value)
        {
            return value.Value;
        }

        public static implicit operator Format<T>(T value)
        {
            return new Format<T>() { Value = value };
        }

        public override string ToString()
        {
            return Value?.ToString();
        }
    }
}
