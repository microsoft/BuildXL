// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.ContentStore.Logging
{
    /// <summary>
    ///     An ILog that drops all messages into the bit bucket.
    /// </summary>
    public sealed class NullLog : ILog
    {
        /// <summary>
        ///     Shared default instance.
        /// </summary>
        public static readonly ILog Instance = new NullLog(Severity.Diagnostic);

        public NullLog(Severity severity)
        {
            CurrentSeverity = severity;
        }

        /// <inheritdoc />
        public Severity CurrentSeverity { get; }

        /// <inheritdoc />
        public void Flush()
        {
        }

        /// <inheritdoc />
        public void Write(DateTime dateTime, int threadId, Severity severity, string message)
        {
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
