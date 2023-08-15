// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Distributed.Blob
{
    /// <summary>
    /// This uniquely describes a namespace in a blob cache. Each namespace is garbage-collected
    /// as a separate cache from other namespaces
    /// </summary>
    public readonly record struct BlobNamespaceId(string Universe, string Namespace)
    {
        public override string ToString() => $"{Universe}-{Namespace}";
    }
}
