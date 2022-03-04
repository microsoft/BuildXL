// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.ContentStore.Logging
{
    internal record struct Request(
        DateTime DateTime,
        int ThreadId,
        RequestType Type,
        Severity Severity,
        string? Message)
    {
        public static Request FlushRequest { get; } = new Request(DateTime.MinValue, int.MinValue, RequestType.Flush,
            Severity.Debug, Message: null);

        public static Request LogStringRequest(DateTime dateTime, int threadId, Severity severity, string message)
            => new Request(dateTime, threadId, RequestType.LogString, severity, message);
    }

    /// <summary>
    ///     Message to the background thread.
    /// </summary>
    internal enum RequestType : ushort
    {
        /// <summary>
        ///     Flush the log.
        /// </summary>
        Flush,

        /// <summary>
        ///     Log on the background thread.
        /// </summary>
        LogString
    }
}
