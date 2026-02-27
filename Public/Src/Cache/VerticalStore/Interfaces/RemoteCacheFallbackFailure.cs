// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Failure representing a remote cache initialization failure where the build falls back to local-only cache.
    /// </summary>
    /// <remarks>
    /// This typed failure allows the engine to distinguish remote cache fallback events from other cache
    /// state degradation failures, enabling dedicated telemetry and monitoring.
    /// </remarks>
    public class RemoteCacheFallbackFailure : CacheBaseFailure
    {
        private readonly string m_message;

        /// <summary>
        /// Creates a new instance of <see cref="RemoteCacheFallbackFailure"/>.
        /// </summary>
        /// <param name="message">A description of the remote cache failure, including actionable information for the user.</param>
        public RemoteCacheFallbackFailure(string message)
        {
            Contract.Requires(message != null);
            m_message = message;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            return m_message;
        }
    }
}
