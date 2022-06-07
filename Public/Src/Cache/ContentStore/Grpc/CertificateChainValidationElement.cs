// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Security.Cryptography.X509Certificates;

namespace BuildXL.Cache.ContentStore.Grpc
{
    /// <summary>
    /// A validation element to compare against when using <see cref="X509Chain"/> validation.
    /// </summary>
    public sealed class CertificateChainValidationElement
    {
        /// <summary>
        /// The subject name including the "CN=" prefix.
        /// </summary>
        public string SubjectName { get; set; }

        /// <summary>
        /// The thumbprint. Comparison will be case-insensitive.
        /// </summary>
        public string Thumbprint { get; set; }

        /// <summary>
        /// The issuer of the current "certificate" for further validation of the chain.
        /// </summary>
        public CertificateChainValidationElement Issuer { get; set; }

        /// <summary>
        /// Runs validation of the <paramref name="certificate"/> against the current instance.
        /// </summary>
        /// <param name="certificate">The certificate to validate.</param>
        /// <param name="error">Potential validation errors.</param>
        /// <returns><c>true</c> if the validation succeeds, <c>false</c> otherwise.</returns>
        public bool Validate(
            X509Certificate2 certificate,
            out string error)
        {
            error = string.Empty;
            if (!string.IsNullOrEmpty(SubjectName) &&
                !string.Equals(SubjectName, certificate.SubjectName.Name, StringComparison.Ordinal))
            {
                error = $"Unexpected subject name '{certificate.SubjectName.Name}'. Expected subject name is '{SubjectName}'.";
                return false;
            }

            if (!string.IsNullOrEmpty(Thumbprint) &&
                !string.Equals(Thumbprint, certificate.Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                // Only return the first 5 characters of the Thumprint for security reasons.
                error = $"Unexpected thumbprint '{certificate.Thumbprint.Substring(0, 5)}'. Expected thumbprint is '{Thumbprint.Substring(0, 5)}'.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Runs validation against the <paramref name="chain"/> starting with this instance as the leaf.
        /// </summary>
        /// <param name="chain">The certificate chain.</param>
        /// <param name="error">Potential validation errors.</param>
        /// <returns><c>true</c> if the validation succeeds, <c>false</c> otherwise.</returns>
        public bool Validate(
            X509Chain chain,
            out string error)
        {
            return Validate(chain, false, out error);
        }

        /// <summary>
        /// Runs validation against the <paramref name="chain"/> starting with this instance as the leaf.
        /// </summary>
        /// <param name="chain">The certificate chain.</param>
        /// <param name="skipFirstChainElement">if <c>true</c>, the first element (leaf) is skipped for the validation.</param>
        /// <param name="error">Potential validation errors.</param>
        /// <returns><c>true</c> if the validation succeeds, <c>false</c> otherwise.</returns>
        public bool Validate(
            X509Chain chain,
            bool skipFirstChainElement,
            out string error)
        {
            CertificateChainValidationElement validationElement = skipFirstChainElement ? null : this;
            error = string.Empty;

            foreach (X509ChainElement ce in chain.ChainElements)
            {
                if (validationElement != null)
                {
                    if (!validationElement.Validate(ce.Certificate, out error))
                    {
                        return false;
                    }

                    if (validationElement.Issuer == null)
                    {
                        break;
                    }

                    validationElement = validationElement.Issuer;
                }

                if (validationElement == null)
                {
                    // Keep this last to ensure that the leaf certificate is skipped.
                    validationElement = this;
                }
            }

            return true;
        }
    }
}
