// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.BasicFilesystem
{
    /// <summary>
    /// Failure to access a enumerate strong fingerprints for a weak fingerprint
    /// </summary>
    public sealed class StrongFingerprintEnumerationFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;
        private readonly WeakFingerprintHash m_weak;
        private readonly Exception m_rootCause;

        /// <summary>
        /// Create the failure, including the weak fingerprint for which enumeration failed
        /// </summary>
        /// <param name="cacheId">The cacheId where the failure happened</param>
        /// <param name="weak">The weak fingerprint for which enumeration was attempted</param>
        /// <param name="rootCause">Optional root cause exception</param>
        public StrongFingerprintEnumerationFailure(string cacheId, WeakFingerprintHash weak, Exception rootCause)
        {
            Contract.Requires(cacheId != null);
            Contract.Requires(weak != null);
            Contract.Requires(rootCause != null);

            m_cacheId = cacheId;
            m_weak = weak;
            m_rootCause = rootCause;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "Cache [{0}] failed to enumerate the StrongFingerprints for [{1}]\nRoot cause: [{2}]", m_cacheId, m_weak, m_rootCause);
        }
    }
}
