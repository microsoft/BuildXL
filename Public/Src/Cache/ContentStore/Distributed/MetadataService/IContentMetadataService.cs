// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ProtoBuf;
using ProtoBuf.Grpc.Configuration;
using ProtoBuf.Meta;
using ProtoBuf.Serializers;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public interface IClientFactory<TClient> : IStartupShutdownSlim
    {
        ValueTask<TClient> CreateClientAsync(OperationContext context);
    }

    [System.ServiceModel.ServiceContract]
    public interface IContentMetadataService
    {
        Task<GetContentLocationsResponse> GetContentLocationsAsync(GetContentLocationsRequest request);

        Task<RegisterContentLocationsResponse> RegisterContentLocationsAsync(RegisterContentLocationsRequest request);

        //Task<PutBlobResponse> PutBlobAsync(PutBlobRequest request);

        //Task<GetBlobResponse> GetBlobAsync(GetBlobRequest request);
    }

    [ProtoContract]
    [ProtoInclude(10, typeof(GetContentLocationsResponse))]
    [ProtoInclude(11, typeof(RegisterContentLocationsResponse))]
    [ProtoInclude(12, typeof(PutBlobResponse))]
    [ProtoInclude(13, typeof(GetBlobResponse))]
    public abstract record ServiceResponseBase
    {
        public abstract RpcMethodId MethodId { get; }

        public bool Succeeded => ErrorMessage == null;

        [ProtoMember(1)]
        public string ErrorMessage { get; init; }

        [ProtoMember(2)]
        public string Diagnostics { get; init; }

        [ProtoMember(3)]
        public bool PersistRequest { get; init; }
    }

    [ProtoContract]
    public record GetContentLocationsResponse : ServiceResponseBase
    {
        public override RpcMethodId MethodId => RpcMethodId.GetContentLocations;

        [ProtoMember(1)]
        public IReadOnlyList<ContentLocationEntry> Entries { get; init; } = new List<ContentLocationEntry>();
    }

    [ProtoContract]
    public record RegisterContentLocationsResponse : ServiceResponseBase
    {
        public override RpcMethodId MethodId => RpcMethodId.RegisterContentLocations;
    }

    [ProtoContract]
    public record PutBlobResponse : ServiceResponseBase
    {
        public override RpcMethodId MethodId => RpcMethodId.PutBlob;
    }

    [ProtoContract]
    public record GetBlobResponse : ServiceResponseBase
    {
        public override RpcMethodId MethodId => RpcMethodId.GetBlob;
    }

    [ProtoContract]
    [ProtoInclude(10, typeof(GetContentLocationsRequest))]
    [ProtoInclude(11, typeof(RegisterContentLocationsRequest))]
    [ProtoInclude(12, typeof(PutBlobRequest))]
    [ProtoInclude(13, typeof(GetBlobRequest))]
    public abstract record ServiceRequestBase
    {
        public abstract RpcMethodId MethodId { get; }

        [ProtoMember(1)]
        public string ContextId { get; init; }
    }

    public enum RpcMethodId
    {
        GetContentLocations = 0,
        RegisterContentLocations = 1,
        PutBlob = 2,
        GetBlob = 3,
    }

    [ProtoContract]
    public record GetContentLocationsRequest : ServiceRequestBase
    {
        public override RpcMethodId MethodId => RpcMethodId.GetContentLocations;

        [ProtoMember(1)]
        public IReadOnlyList<ShortHash> Hashes { get; init; } = new List<ShortHash>();
    }

    [ProtoContract]
    public record RegisterContentLocationsRequest : ServiceRequestBase
    {
        public override RpcMethodId MethodId => RpcMethodId.RegisterContentLocations;

        [ProtoMember(1)]
        public IReadOnlyList<ShortHashWithSize> Hashes { get; init; } = new List<ShortHashWithSize>();

        [ProtoMember(2)]
        public MachineId MachineId { get; init; }
    }

    [ProtoContract]
    public record PutBlobRequest : ServiceRequestBase
    {
        public override RpcMethodId MethodId => RpcMethodId.PutBlob;
    }

    [ProtoContract]
    public record GetBlobRequest : ServiceRequestBase
    {
        public override RpcMethodId MethodId => RpcMethodId.GetBlob;
    }

    public sealed class MetadataServiceSerializer
    {
        public static BinderConfiguration BinderConfiguration { get; } = CreateBinderConfiguration();

        public static RuntimeTypeModel TypeModel { get; } = CreateModel();

        private static BinderConfiguration CreateBinderConfiguration()
        {
            return BinderConfiguration.Create(new[] { ProtoBufMarshallerFactory.Create(CreateModel(), ProtoBufMarshallerFactory.Options.None) });
        }

        public static RuntimeTypeModel CreateModel()
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
            Span<uint> parts = MemoryMarshal.Cast<ShortHash, uint>(hashSpan);
            parts[0] = tuple.part0;
            parts[1] = tuple.part1;
            parts[2] = tuple.part2;
            return hashSpan[0];
        }

        private static (uint, uint, uint) ShortHashToUIntTuple(ShortHash hash)
        {
            Span<ShortHash> hashSpan = stackalloc ShortHash[1];
            hashSpan[0] = hash;
            Span<uint> parts = MemoryMarshal.Cast<ShortHash, uint>(hashSpan);
            return (parts[0], parts[1], parts[2]);
        }
    }
}
