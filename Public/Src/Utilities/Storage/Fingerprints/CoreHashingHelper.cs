// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Storage.Fingerprints
{
    /// <summary>
    /// Helper class for computing fingerprints.
    /// </summary>
    public sealed class CoreHashingHelper : CoreHashingHelperBase
    {
        /// <nodoc />
        public CoreHashingHelper(bool recordFingerprintString)
            : base(recordFingerprintString)
        {
        }
    }
}
