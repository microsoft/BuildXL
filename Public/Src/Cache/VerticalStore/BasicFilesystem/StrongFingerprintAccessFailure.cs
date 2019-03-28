// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.BasicFilesystem
{
    /// <summary>
    /// Failure to access a StrongFingerprint
    /// </summary>
    public sealed class StrongFingerprintAccessFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;
        private readonly StrongFingerprint m_strong;
        private readonly Exception m_rootCause;

        /// <summary>
        /// Create the failure, including the StrongFingerprint that failed
        /// </summary>
        /// <param name="cacheId">The cacheId where the failure happened</param>
        /// <param name="strong">The StrongFingerprint that failed</param>
        /// <param name="rootCause">Optional root cause exception</param>
        public StrongFingerprintAccessFailure(string cacheId, StrongFingerprint strong, Exception rootCause)
        {
            Contract.Requires(cacheId != null);
            Contract.Requires(strong != null);
            Contract.Requires(rootCause != null);

            m_cacheId = cacheId;
            m_strong = strong;
            m_rootCause = rootCause;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "Cache [{0}] failed to access StrongFingerprint [{1}]\nRoot cause: [{2}]", m_cacheId, m_strong, m_rootCause);
        }
    }
}
