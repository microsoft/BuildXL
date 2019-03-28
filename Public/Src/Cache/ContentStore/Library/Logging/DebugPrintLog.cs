// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.ContentStore.Logging
{
    /// <summary>
    ///     An ILog that writes messages to the debug channel.
    /// </summary>
    public sealed class DebugPrintLog : ILog
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="DebugPrintLog" /> class.
        /// </summary>
        public DebugPrintLog(Severity severity)
        {
            CurrentSeverity = severity;
        }

        /// <inheritdoc />
        public void Dispose()
        {
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
            if (severity >= CurrentSeverity)
            {
                Debug.Print(message);
            }
        }
    }
}
