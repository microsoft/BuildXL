// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     A stream that can provide a hash of its content.
    /// </summary>
    public abstract class HashingStream : Stream, IAsyncDisposable
    {
        /// <summary>
        ///     Get the calculated content hash for the stream's content.
        /// </summary>
        public abstract ContentHash GetContentHash();

        /// <summary>
        ///     Get the value task with the calculated content hash for the stream's content.
        /// </summary>
        /// <remarks>
        ///     The resulting task is not completed only when the hashing is done in parallel.
        /// </remarks>
        public abstract ValueTask<ContentHash> GetContentHashAsync();

        /// <summary>
        ///     Gets the amount of time that has been spent hashing.
        /// </summary>
        public abstract TimeSpan TimeSpentHashing { get; }

#if !NET_COREAPP
        /// <inheritdoc />
        public abstract ValueTask DisposeAsync();
#endif
    }
}
