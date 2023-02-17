// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using static BuildXL.Utilities.Core.FormattableStringEx;

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
