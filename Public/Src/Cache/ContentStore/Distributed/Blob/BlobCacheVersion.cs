// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// Version number to allow us to do backwards-incompatible changes.
/// </summary>
/// <remarks>
/// This is tightly coupled with <see cref="BlobCacheContainerName"/>.
/// 
/// The version has <see cref="BlobCacheContainerName.VersionReservedLength"/> characters reserved for it.
/// </remarks>
public enum BlobCacheVersion
{
    V0,
}
