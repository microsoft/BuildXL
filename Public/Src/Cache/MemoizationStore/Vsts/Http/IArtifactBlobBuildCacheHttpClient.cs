// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.MemoizationStore.VstsInterfaces;
using Microsoft.VisualStudio.Services.Content.Common;

namespace BuildXL.Cache.MemoizationStore.Vsts.Http
{
    /// <summary>
    /// <see cref="ArtifactHttpClientFactory"/>.CreateVssHttpClient now requires that its first generic parameter be an <see cref="IArtifactHttpClient"/>.
    /// <see cref="IBlobBuildCacheHttpClient"/> is no longer an <see cref="IArtifactHttpClient"/> due to the work to sever MemoizationStoreVstsInterfaces' dependency on VSTS.
    /// This is an interface combining <see cref="IArtifactHttpClient"/> and <see cref="IBlobBuildCacheHttpClient"/>
    /// so that <see cref="ItemBuildCacheHttpClient"/> continues to be usable with CreateVssHttpClient.
    /// The eventual plan was to sever MemoizationStoreVsts' dependency on VSTS as well, which will require the [Blob|Item]BuildCacheHttpClientFactory
    /// to come from VSO and be passed in upon construction in BuildXL and CloudBuild.
    /// </summary>
    public interface IArtifactBlobBuildCacheHttpClient : IBlobBuildCacheHttpClient, IArtifactHttpClient
    {
    }
}
