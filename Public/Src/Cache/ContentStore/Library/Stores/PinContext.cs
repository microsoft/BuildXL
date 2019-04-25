// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Synchronization;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Context for pinning content. Uses the provided callback for unpinning upon disposal.
    /// </summary>
    public sealed class PinContext : IDisposable
    {
        private readonly Dictionary<ContentHash, int> _pinnedHashes = new Dictionary<ContentHash, int>();
        private readonly Action<IEnumerable<KeyValuePair<ContentHash, int>>> _unpinAction;
        private readonly BackgroundTaskTracker _taskTracker;
        private readonly object _lock = new object();
        private Func<IEnumerable<KeyValuePair<ContentHash, int>>, Task> _unpinAsync;
        private bool _disposed;

        /// <summary>
        ///     Initializes a new instance of the <see cref="PinContext" /> class.
        /// </summary>
        public PinContext(BackgroundTaskTracker taskTracker, Action<IEnumerable<KeyValuePair<ContentHash, int>>> unpinAction)
        {
            Contract.Requires(unpinAction != null);
            Contract.Requires(taskTracker != null);

            _unpinAction = unpinAction;
            _taskTracker = taskTracker;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PinContext" /> class.
        /// </summary>
        public PinContext(BackgroundTaskTracker taskTracker, Func<IEnumerable<KeyValuePair<ContentHash, int>>, Task> unpinAsync)
        {
            Contract.Requires(unpinAsync != null);
            Contract.Requires(taskTracker != null);

            _unpinAsync = unpinAsync;
            _taskTracker = taskTracker;
        }

        /// <summary>
        ///     Add pin for specified content.
        /// </summary>
        public void AddPin(ContentHash contentHash)
        {
            lock (_lock)
            {
                ThrowIfDisposed();

                if (_pinnedHashes.ContainsKey(contentHash))
                {
                    _pinnedHashes[contentHash]++;
                }
                else
                {
                    _pinnedHashes[contentHash] = 1;
                }
            }
        }

        /// <summary>
        ///     Checks if the PinContext has pinned the given hash.
        /// </summary>
        /// <param name="contentHash">The hash to check for.</param>
        /// <returns>True if the hash is pinned by the PinContext; false if it is not.</returns>
        public bool Contains(ContentHash contentHash)
        {
            lock (_lock)
            {
                ThrowIfDisposed();
                return _pinnedHashes.ContainsKey(contentHash);
            }
        }

        /// <summary>
        ///     Return collection of content hashes that are currently pinned.
        /// </summary>
        public IEnumerable<ContentHash> GetContentHashes()
        {
            var contentHashes = new List<ContentHash>(_pinnedHashes.Count);

            lock (_lock)
            {
                ThrowIfDisposed();

                contentHashes.AddRange(_pinnedHashes.Keys);
            }

            return contentHashes;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("PinContext");
            }
        }

        /// <summary>
        /// Dispose manually, asynchronously.
        /// </summary>
        public async Task DisposeAsync()
        {
            Func<IEnumerable<KeyValuePair<ContentHash, int>>, Task> unpinAsync = null;

            lock (_lock)
            {
                if (!_disposed)
                {
                    unpinAsync = _unpinAsync;
                    _unpinAsync = null;
                    _disposed = true;
                }
            }

            if (unpinAsync != null)
            {
                await unpinAsync(_pinnedHashes);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (_lock)
            {
                if (!_disposed)
                {
                    if (_unpinAsync != null)
                    {
                        _taskTracker.Add(_unpinAsync(_pinnedHashes));
                    }

                    _unpinAction?.Invoke(_pinnedHashes);
                    _disposed = true;
                }
            }
        }
    }

    /// <summary>
    ///     Set of parameters for requesting that content be pinned or asserting that content should already be pinned.
    /// </summary>
    public readonly struct PinRequest
    {
        /// <summary>
        ///     The pin context to which content will be pinned.
        /// </summary>
        public readonly PinContext PinContext;

        /// <summary>
        ///     If true, will return an error if the content was not already pinned to any context.
        /// </summary>
        public readonly bool VerifyAlreadyPinned;

        /// <summary>
        ///     If provided, will return an error if the content was not already pinned to this context.
        /// </summary>
        public readonly PinContext VerifyPinContext;

        /// <summary>
        ///     Initializes a new instance of the <see cref="PinRequest" /> struct.
        /// </summary>
        /// <param name="pinContext">The pin context to which content will be pinned.</param>
        /// <param name="verifyAlreadyPinned">If true, will return an error if the content was not already pinned to any context.</param>
        /// <param name="verifyPinContext">If provided, will return an error if the content was not already pinned to this context.</param>
        public PinRequest(PinContext pinContext, bool verifyAlreadyPinned = false, PinContext verifyPinContext = null)
        {
            PinContext = pinContext;
            VerifyAlreadyPinned = verifyAlreadyPinned;
            VerifyPinContext = verifyPinContext;
        }
    }
}
