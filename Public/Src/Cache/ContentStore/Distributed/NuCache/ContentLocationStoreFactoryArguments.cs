// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.Services;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public sealed class ContentLocationStoreFactoryArguments
    {
        public required DistributedContentCopier Copier { get; init; }

        public required IClock Clock { get; init; }

        public required GrpcConnectionPool ConnectionPool { get; init; }

        public required ContentLocationStoreServicesDependencies Dependencies { get; init; }
    }
}
