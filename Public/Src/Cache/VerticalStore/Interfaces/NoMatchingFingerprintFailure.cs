// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Utilities;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Indicates that a query for a fingerprint has failed.
    /// </summary>
    public class NoMatchingFingerprintFailure : CacheBaseFailure
    {
        private readonly StrongFingerprint m_strong;

        /// <summary>
        /// .ctr
        /// </summary>
        /// <param name="strong">The fingerprint that was not found.</param>
        /// <param name="innerFailure">Optional inner / root failure</param>
        public NoMatchingFingerprintFailure(StrongFingerprint strong, Failure innerFailure = null)
            : base(innerFailure)
        {
            Contract.Requires(strong != null);

            m_strong = strong;
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "Fingerprint {0} was not found.", m_strong);
        }
    }
}
