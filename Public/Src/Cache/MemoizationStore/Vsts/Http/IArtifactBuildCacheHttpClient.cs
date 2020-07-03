// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.MemoizationStore.VstsInterfaces;
using Microsoft.VisualStudio.Services.Content.Common;

namespace BuildXL.Cache.MemoizationStore.Vsts.Http
{
    /// <summary>
    /// <see cref="ArtifactHttpClientFactory"/>.CreateVssHttpClient now requires that its first generic parameter be an IArtifactHttpClient.
    /// <see cref="IBuildCacheHttpClient"/> is no longer an IArtifactHttpClient due to the work to sever MemoizationStoreVstsInterfaces' dependency on VSTS.
    /// This is an interface combining <see cref="IArtifactHttpClient"/> and <see cref="IBuildCacheHttpClient"/>
    /// so that <see cref="ItemBuildCacheHttpClient"/> continues to be usable with CreateVssHttpClient.
    /// The eventual plan was to sever MemoizationStoreVsts' dependency on VSTS as well, which will require the [Blob|Item]BuildCacheHttpClientFactory
    /// to come from VSO and be passed in upon construction in BuildXL and CloudBuild.
    /// </summary>
    public interface IArtifactBuildCacheHttpClient : IBuildCacheHttpClient, IArtifactHttpClient
    {
    }
}
