// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System;

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

public readonly record struct AbsoluteContainerPath(BlobCacheStorageAccountName Account, BlobCacheContainerName Container)
{
    public BlobNamespaceId NamespaceId => Container.NamespaceId;

    public override string ToString()
    {
        return $"{Account}/{Container}";
    }

    public MachineLocation ToMachineLocation()
    {
        return new MachineLocation(new Uri($"azs://{Account}/{Container}"));
    }
}
