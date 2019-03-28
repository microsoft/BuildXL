// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.ContentStore.Logging
{
    /// <summary>
    ///     Interface for real log implementations invoked by Logger.
    /// </summary>
    public interface ILog : IDisposable
    {
        /// <summary>
        ///     Gets get the current severity.
        /// </summary>
        /// <remarks>
        ///     Messages at the current severity and above are written to the destination while
        ///     lower severity messages are ignored.
        /// </remarks>
        Severity CurrentSeverity { get; }

        /// <summary>
        ///     Flush buffered messages to storage.
        /// </summary>
        void Flush();

        /// <summary>
        ///     Log a message with the given severity if it is at least as high as the current severity.
        /// </summary>
        /// <param name="dateTime">Time the event occurred.</param>
        /// <param name="threadId">Managed thread Id associated with the event.</param>
        /// <param name="severity">Severity to attach to this log message.</param>
        /// <param name="message">Message string</param>
        void Write(DateTime dateTime, int threadId, Severity severity, string message);
    }
}
