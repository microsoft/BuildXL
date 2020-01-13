// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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