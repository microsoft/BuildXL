// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using Microsoft.VisualStudio.Services.BlobStore.Common;

namespace Tool.DropDaemon
{
    /// <summary>
    ///     A straightforward implementation of <see cref="IDropFile"/> interface.
    /// </summary>
    /// <remarks>
    ///     Immutable.
    /// </remarks>
    public sealed class DropFile : IDropFile
    {
        /// <inheritdoc />
        public BlobIdentifier BlobIdentifier { get; }

        /// <inheritdoc />
        public long? FileSize { get; }

        /// <inheritdoc />
        public string RelativePath { get; }

        /// <nodoc />
        public DropFile(string relativePath, long? fileSize, BlobIdentifier blobId)
        {
            Contract.Requires(!string.IsNullOrEmpty(relativePath));
            Contract.Requires(blobId != null);

            BlobIdentifier = blobId;
            FileSize = fileSize;
            RelativePath = relativePath;
        }
    }
}
