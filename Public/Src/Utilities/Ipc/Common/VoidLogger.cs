// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Ipc.Interfaces;

namespace BuildXL.Ipc.Common
{
    /// <summary>
    /// No-op implementation of <see cref="ILogger"/>.
    /// </summary>
    public sealed class VoidLogger : ILogger
    {
        /// <summary>Singleton instance.</summary>
        public static readonly VoidLogger Instance = new VoidLogger();

        /// <summary>Ignores all log request.</summary>
        public void Log(LogLevel level, string format, params object[] args) { }

        /// <nodoc />
        public void Dispose() { }
    }
}
