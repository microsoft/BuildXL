// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// Container's purpose.
/// </summary>
/// <remarks>
/// This is tightly coupled with <see cref="BlobCacheContainerName"/>. 
///
/// The purpose has <see cref="BlobCacheContainerName.PurposeReservedLength"/> characters reserved for it.
///
/// CODESYNC: <see cref="BlobCacheContainerName.Parse"/>
/// </remarks>
public enum BlobCacheContainerPurpose
{
    Content,
    Metadata,
}
