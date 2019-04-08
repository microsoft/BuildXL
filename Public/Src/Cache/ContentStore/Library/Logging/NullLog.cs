// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        public static readonly ILog Instance = new NullLog();

        /// <inheritdoc />
        public Severity CurrentSeverity => Severity.Diagnostic;

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
