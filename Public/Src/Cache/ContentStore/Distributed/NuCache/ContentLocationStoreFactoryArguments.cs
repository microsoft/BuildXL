// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.Services;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public record ContentLocationStoreFactoryArguments
    {
        public DistributedContentCopier Copier { get; init; }

        public IClock Clock { get; init; }

        public GrpcConnectionPool ConnectionPool { get; init; }

        public ContentLocationStoreServicesDependencies Dependencies { get; init; } = new ContentLocationStoreServicesDependencies();
    }
}
