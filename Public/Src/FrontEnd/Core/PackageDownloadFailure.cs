// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Failure details for failed downloads
    /// </summary>
    public class PackageDownloadFailure : Failure
    {
        /// <nodoc />
        public string PackageIdentifier { get; }

        /// <nodoc />
        public string TargetLocation { get; }

        /// <nodoc />
        public FailureType Type { get; }

        /// <nodoc />
        public Exception Exception { get; }

        /// <nodoc />
        public string Error { get; }

        /// <nodoc />
        public PackageDownloadFailure(string packageIdentifier, string targetLocation, FailureType type, Exception exception = null)
        {
            PackageIdentifier = packageIdentifier;
            TargetLocation = targetLocation;
            Type = type;
            Exception = exception;
        }

        /// <nodoc />
        public PackageDownloadFailure(string packageIdentifier, string targetLocation, FailureType type, Failure failure)
            : base(failure)
        {
            PackageIdentifier = packageIdentifier;
            TargetLocation = targetLocation;
            Type = type;
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Globalization", "CA1305:SpecifyIFormatProvider")]
        public override string Describe()
        {
            return I($"Failed to restore package '{PackageIdentifier}' to '{TargetLocation}' due to {Type.ToString()}. ") + (Exception?.Message ?? Error);
        }

        /// <inheritdoc />
        public override BuildXLException CreateException()
        {
            return new BuildXLException(Describe());
        }

        /// <inheritdoc />
        public override BuildXLException Throw()
        {
            throw CreateException();
        }

        /// <nodoc />
        public enum FailureType
        {
            /// <nodoc />
            CacheError,

            /// <nodoc />
            GetFingerprintFromCache,

            /// <nodoc />
            LoadAvailableContentFromCache,

            /// <nodoc />
            MaterializeFromCache,

            /// <nodoc />
            CannotCacheContent,

            /// <nodoc />
            CannotCacheFingerprint,

            /// <nodoc />
            HashingOfPackageFile,

            /// <nodoc />
            FailedToProcessExistingState,

            /// <nodoc />
            PackageOnDiskIsNotAvailable,
        }
    }
}
