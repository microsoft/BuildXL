// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Utilities;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Failure to transfer a file between caches.
    /// </summary>
    public sealed class CASTransferFailure : CacheBaseFailure
    {
        private readonly string m_sourceCacheId;
        private readonly string m_destinationCacheId;
        private readonly string m_message;
        private readonly string m_cacheId;
        private readonly CasHash m_fileHash;

        /// <summary>
        /// Create the failure.
        /// </summary>
        /// <param name="cacheId">Id of the cache doing the transfer</param>
        /// <param name="destinationCacheId">Id of the cache that the file would have been moved to.</param>
        /// <param name="fileHash">Hash for the file</param>
        /// <param name="innerFailure">Failure that occurrec when transferring the file</param>
        /// <param name="message">Additional context information about when the failure occurred.</param>
        /// <param name="sourceCacheId">Id of the cache the file would have transferred from.</param>
        public CASTransferFailure(string cacheId, string sourceCacheId, string destinationCacheId, CasHash fileHash, string message, Failure innerFailure)
            : base(innerFailure)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(sourceCacheId));
            Contract.Requires(!string.IsNullOrWhiteSpace(destinationCacheId));
            Contract.Requires(!string.IsNullOrWhiteSpace(cacheId));
            Contract.Requires(innerFailure != null);

            m_sourceCacheId = sourceCacheId;
            m_destinationCacheId = destinationCacheId;
            m_message = message == null ? string.Empty : message;
            m_fileHash = fileHash;
            m_cacheId = cacheId;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "Cache {0} could not transfer file {1} from source cache {2} to destination cache {3}, for reason {4} {5}",
                m_cacheId, m_fileHash, m_sourceCacheId, m_destinationCacheId, InnerFailure.Describe(), m_message);
        }
    }
}
