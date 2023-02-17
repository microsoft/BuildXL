// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Utilities.Core;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// A failure from a remote cache
    /// </summary>
    public sealed class RemoteCacheFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;
        private readonly string m_message;

        /// <summary>
        /// Create the failure.
        /// </summary>
        /// <param name="cacheId">The cacheId where the failure happened</param>
        /// <param name="innerFailure">Failure from the remote cache.</param>
        /// <param name="message">Added context</param>
        public RemoteCacheFailure(string cacheId, string message, Failure innerFailure)
            : base(innerFailure)
        {
            Contract.Requires(cacheId != null);
            Contract.Requires(innerFailure != null);

            m_cacheId = cacheId;
            m_message = message == null ? string.Empty : message;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "Cache [{0}] experienced failure {1} from its remote cache. {2}", m_cacheId, InnerFailure.Describe(), m_message);
        }
    }
}
