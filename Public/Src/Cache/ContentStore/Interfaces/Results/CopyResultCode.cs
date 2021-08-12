// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// A code that helps caller to make decisions.
    /// </summary>
    public enum CopyResultCode
    {
        /// <summary>
        /// The call succeeded.
        /// </summary>
        Success,

        /// <summary>
        /// The cause of the error was connection timeout.
        /// </summary>
        ConnectionTimeoutError,

        /// <summary>
        /// The cause of the error was inability to get first reply from the other side in time.
        /// </summary>
        TimeToFirstByteTimeoutError,

        /// <summary>
        /// The cause of the exception was the destination path.
        /// </summary>
        DestinationPathError,

        /// <summary>
        /// The cause of the exception was the source file.
        /// </summary>
        FileNotFoundError,

        /// <summary>
        /// The cause of the exception was copy timeout.
        /// </summary>
        CopyTimeoutError,

        /// <summary>
        /// The cause of the exception was the received file does not have the expected hash.
        /// </summary>
        InvalidHash,

        /// <summary>
        /// The cause of the exception is unknown.
        /// </summary>
        Unknown,

        /// <summary>
        /// The cause of the exception was copy timeout detected by bandwidth checker.
        /// </summary>
        CopyBandwidthTimeoutError,

        /// <summary>
        /// The server is unavailable.
        /// </summary>
        ServerUnavailable,

        /// <summary>
        /// gRPC error occurred.
        /// </summary>
        RpcError,

        /// <summary>
        /// The server already has the content.
        /// </summary>
        Rejected_ContentAvailableLocally,

        /// <summary>
        /// The server is already handling a copy of the content.
        /// </summary>
        Rejected_OngoingCopy,

        /// <summary>
        /// The server is at the limit of concurrent copies.
        /// </summary>
        Rejected_CopyLimitReached,

        /// <summary>
        /// The server does not have any handlers which support the operation.
        /// </summary>
        Rejected_NotSupported,

        /// <summary>
        /// The server is already evicting older content than this
        /// </summary>
        Rejected_OlderThanLastEvictedContent,

        /// <summary>
        /// Rejected for unknown reasons.
        /// </summary>
        Rejected_Unknown,

        /// <summary>
        /// Copy is disabled by configuration.
        /// </summary>
        Disabled,

        /// <summary>
        /// Server returned an unknown error.
        /// </summary>
        UnknownServerError,
    }
}
