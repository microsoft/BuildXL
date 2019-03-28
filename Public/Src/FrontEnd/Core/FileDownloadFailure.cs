// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Failure details for failed downloads
    /// </summary>
    public sealed class FileDownloadFailure : Failure
    {
        /// <nodoc />
        public string ToolName { get; }

        /// <nodoc />
        public string Url { get; }

        /// <nodoc />
        public string TargetLocation { get; }

        /// <nodoc />
        public FailureType FailureType { get; }

        /// <nodoc />
        public Exception Exception { get; }

        /// <nodoc />
        public FileDownloadFailure(string toolName, string url, string targetLocation, FailureType type, Exception exception = null)
        {
            ToolName = toolName;
            Url = url;
            TargetLocation = targetLocation;
            FailureType = type;
            Exception = exception;
        }

        /// <nodoc />
        public FileDownloadFailure(string toolName, string url, string targetLocation, FailureType type, Failure failure)
            : base(failure)
        {
            ToolName = toolName;
            Url = url;
            TargetLocation = targetLocation;
            FailureType = type;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            return I($"Failed to download tool '{ToolName}' from '{Url}' to '{TargetLocation}' due to {FailureType}. {Exception?.Message}");
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
    }

    /// <nodoc />
    public enum FailureType
    {
        /// <nodoc />
        HashExistingFile,

        /// <nodoc />
        InvalidUri,

        /// <nodoc />
        InitializeCache,

        /// <nodoc />
        GetFingerprintFromCache,

        /// <nodoc />
        LoadContentFromCache,

        /// <nodoc />
        PlaceContentFromCache,

        /// <nodoc />
        Download,

        /// <nodoc />
        DownloadResultMissing,

        /// <nodoc />
        HashingOfDownloadedFile,

        /// <nodoc />
        MismatchedHash,

        /// <nodoc />
        CannotCacheContent,

        /// <nodoc />
        CannotCacheFingerprint,

        /// <nodoc />
        CopyFile,
    }
}
