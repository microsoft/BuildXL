// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Failure to compute a Hash from a file
    /// </summary>
    public sealed class HashFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;
        private readonly string m_filename;
        private readonly Exception m_rootCause;

        /// <summary>
        /// Create the failure, including the CasHash that failed
        /// </summary>
        /// <param name="cacheId">The cacheId where the failure happened</param>
        /// <param name="filename">The filename that failed</param>
        /// <param name="rootCause">Optional root cause exception</param>
        public HashFailure(string cacheId, string filename, Exception rootCause = null)
        {
            Contract.Requires(cacheId != null);

            m_cacheId = cacheId;
            m_filename = filename;
            m_rootCause = rootCause;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            if (m_rootCause != null)
            {
                return string.Format(CultureInfo.InvariantCulture, "Cache [{0}] could hash [{1}]\nRoot cause: [{2}]", m_cacheId, m_filename, m_rootCause);
            }

            return string.Format(CultureInfo.InvariantCulture, "Cache [{0}] could hash [{1}]", m_cacheId, m_filename);
        }
    }
}
