// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    /// Defines operation for placing for VFS
    /// </summary>
    public interface IVfsFilePlacer
    {
        /// <summary>
        /// Places backing file for VFS
        /// </summary>
        Task<PlaceFileResult> PlaceFileAsync(Context context, AbsolutePath path, VfsFilePlacementData placementData, CancellationToken token);
    }
}
