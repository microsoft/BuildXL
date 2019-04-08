// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.InputListFilter
{
    /// <summary>
    /// A failure due to a bad regex in the configuration
    /// </summary>
    public sealed class InputListFilterFailure : CacheBaseFailure
    {
        private readonly StrongFingerprint m_strongFingerprint;
        private readonly string m_message;

        /// <summary>
        /// Failure of an observed input list filter
        /// </summary>
        /// <param name="cacheId">Cache ID where failure happened</param>
        /// <param name="weak">Weak fingerprint component of the strong fingerprint</param>
        /// <param name="casElement">The CasElement component of the strong fingerprint</param>
        /// <param name="hashElement">The HashElement component of the strong fingerprint</param>
        /// <param name="message">Details about the failure</param>
        public InputListFilterFailure(string cacheId, WeakFingerprintHash weak, CasHash casElement, Hash hashElement, string message)
        {
            Contract.Requires(message != null);

            m_strongFingerprint = new StrongFingerprint(weak, casElement, hashElement, cacheId);
            m_message = message;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "Cache [{0}] strong fingerprint {1} : {2}", m_strongFingerprint.CacheId, m_strongFingerprint.ToString(), m_message);
        }
    }
}
