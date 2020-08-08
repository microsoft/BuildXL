// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Storage.Fingerprints
{
    /// <summary>
    /// Class version of abstract CoreHashingHelper.
    /// </summary>
    public sealed class BasicHashingHelper : CoreHashingHelperBase
    {
        /// <summary>
        /// Class constructor.
        /// </summary>
        public BasicHashingHelper(bool recordFingerprintString, HashAlgorithmType hashAlgorithmType = HashAlgorithmType.SHA1Managed)
            : base(recordFingerprintString, hashAlgorithmType)
        {
        }
    }
}
