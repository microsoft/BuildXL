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

            return model;
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

        public static void SerializeWithLengthPrefix<T>(Stream stream, T value)
        {
            TypeModel.SerializeWithLengthPrefix(stream, value, typeof(T), PrefixStyle.Fixed32, default);
        }

        public static T DeserializeWithLengthPrefix<T>(Stream stream)
        {
            return (T)TypeModel.DeserializeWithLengthPrefix(stream, null, typeof(T), PrefixStyle.Fixed32, default);
        }
    }
}
