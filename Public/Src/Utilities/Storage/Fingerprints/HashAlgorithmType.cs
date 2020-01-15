// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;

namespace BuildXL.Storage.Fingerprints
{
    /// <summary>
    /// Hash algorithm types that <see cref="HashingHelper"/> supports
    /// </summary>
    public enum HashAlgorithmType
    {
        /// <summary>
        /// <see cref="System.Security.Cryptography.SHA1Managed"/>
        /// </summary>
        SHA1Managed = 0, 

        /// <summary>
        /// <see cref="MurmurHashEngine"/>
        /// </summary>
        MurmurHash3 = 1
    }
}