// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Utilities.Core;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// A local cache failure.
    /// </summary>
    public sealed class LocalCacheFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;
        private readonly string m_message;

        /// <summary>
        /// Create the failure.
        /// </summary>
        /// <param name="cacheId">The cacheId where the failure happened</param>
        /// <param name="message">Message with added context</param>
        /// <param name="innerFailure">Failure from the local cache</param>
        public LocalCacheFailure(string cacheId, string message, Failure innerFailure)
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
            return string.Format(CultureInfo.InvariantCulture, "Cache [{0}] experienced failure {1} {2} from its local cache. {3}", m_cacheId, InnerFailure.GetType().Name, InnerFailure.Describe(), m_message);
        }
    }
}
