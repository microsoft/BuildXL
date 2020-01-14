// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        public const string RequiredKextVersionNumber = "3.2.99";
    }
}
