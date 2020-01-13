// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
