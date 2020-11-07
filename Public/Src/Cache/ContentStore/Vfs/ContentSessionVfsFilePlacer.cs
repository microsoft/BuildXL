// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Vfs
{
    public static class ContentSessionVfsFilePlacer
    {
        /// <inheritdoc />
        public static Task<PlaceFileResult> PlaceFileAsync(this IReadOnlyContentSession session, Context context, AbsolutePath path, VfsFilePlacementData placementData, CancellationToken token)
        {
            return session.PlaceFileAsync(
                context,
                placementData.Hash,
                path,
                placementData.AccessMode,
                FileReplacementMode.ReplaceExisting,
                placementData.RealizationMode,
                token); 
        }
    }
}
