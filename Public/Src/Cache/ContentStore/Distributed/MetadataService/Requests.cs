// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using ProtoBuf;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    [ProtoContract]
    [ProtoInclude(10, typeof(GetContentLocationsResponse))]
    [ProtoInclude(11, typeof(RegisterContentLocationsResponse))]
    [ProtoInclude(12, typeof(PutBlobResponse))]
    [ProtoInclude(13, typeof(GetBlobResponse))]
    public abstract record ServiceResponseBase
    {
        public abstract RpcMethodId MethodId { get; }

        public bool Succeeded
        {
            get
            {
                if (ShouldRetry)
                {
                    throw new ClientCanRetryException("Target machine indicated that retry is needed.");
                }

                return ErrorMessage == null;
            }
        }

        [ProtoMember(1)]
        public string ErrorMessage { get; set; }

        [ProtoMember(2)]
        public string Diagnostics { get; init; }

        [ProtoMember(3)]
        public bool PersistRequest { get; init; }

        [ProtoMember(4)]
        public bool ShouldRetry { get; set; }
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
        public string ContextId { get; set; }

        public bool Replaying { get; set; }
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
}
