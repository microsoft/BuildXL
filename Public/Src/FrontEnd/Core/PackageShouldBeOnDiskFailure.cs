// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Package should be restored on disk but is not available.
    /// </summary>
    public sealed class PackageShouldBeOnDiskFailure : PackageDownloadFailure
    {
        /// <nodoc />
        public PackageShouldBeOnDiskFailure(string packageIdentifier, string targetLocation)
            : base(packageIdentifier, targetLocation, PackageDownloadFailure.FailureType.PackageOnDiskIsNotAvailable)
        {
        }
        
        /// <inheritdoc />
        public override string Describe()
        {
            return I($"Failed to restore package '{PackageIdentifier}'. The package should be restored manually at '{TargetLocation}'.");
        }
    }
}
