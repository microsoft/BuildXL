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
    /// <summary>
    /// Implementation of <see cref="IVfsFilePlacer"/> backed by <see cref="IReadOnlyContentSession"/>
    /// </summary>
    public class ContentSessionVfsFilePlacer : IVfsFilePlacer
    {
        private readonly IReadOnlyContentSession _contentSession;

        /// <nodoc />
        public ContentSessionVfsFilePlacer(IReadOnlyContentSession contentSession)
        {
            _contentSession = contentSession;
        }

        /// <inheritdoc />
        public Task<PlaceFileResult> PlaceFileAsync(Context context, AbsolutePath path, VfsFilePlacementData placementData, CancellationToken token)
        {
            return _contentSession.PlaceFileAsync(
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
