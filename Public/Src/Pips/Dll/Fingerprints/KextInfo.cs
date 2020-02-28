// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Pips.Fingerprints
{
    /// <summary>
    /// Static class with some constant definitions pertinent to the sandbox kext.
    /// </summary>
    public static class KextInfo
    {
        /// <summary>
        /// Until some automation for kernel extension building and deployment is in place, this number has to be kept in sync with the 'CFBundleVersion'
        /// inside the Info.plist file of the kernel extension code base. BuildXL will not work if a version mismatch is detected!
        /// </summary>
        public const string RequiredKextVersionNumber = "3.3.99";
    }
}
