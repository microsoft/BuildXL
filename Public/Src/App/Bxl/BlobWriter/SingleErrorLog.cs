// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.Logging;

namespace BuildXL
{
    /// <summary>
    /// An <see cref="ILog"/> that only logs the first error message and ignores the rest.
    /// </summary>
    /// <remarks>
    /// Specifically used for creating an <see cref="AzureBlobStorageLog"/> such that we avoid spamming the console with too many
    /// errors
    /// </remarks>
    internal class SingleErrorLog : ILog
    {
        private readonly Action<string> m_logger;
        private bool m_firstErrorReceived;

        /// <nodoc/>
        public SingleErrorLog(Action<string> logger)
        {
            Contract.Requires(logger != null);

            m_logger = logger;
            m_firstErrorReceived = false;
        }

        /// <summary>
        /// Errors coming from uploading blobs are sometimes miscategorized as 'Info', so
        /// let's receive all of them
        /// </summary>
        public Severity CurrentSeverity => Severity.Diagnostic;

        /// <inheritdoc/>
        public void Dispose()
        {
        }

        /// <inheritdoc/>
        public void Flush()
        {
        }

        /// <summary>
        /// Only writes the first error message and ignores the rest
        /// </summary>
        public void Write(DateTime dateTime, int threadId, Severity severity, string message)
        {
            // Unfortunately, many real errors come miscategorized, so let's find 'error' as part of the message in order to identify them
            if (!m_firstErrorReceived && message.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                m_logger(message);
                m_firstErrorReceived = true;
            }
        }
    }
}