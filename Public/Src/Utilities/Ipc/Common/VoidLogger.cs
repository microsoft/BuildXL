// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Ipc.Interfaces;

namespace BuildXL.Ipc.Common
{
    /// <summary>
    /// No-op implementation of <see cref="IIpcLogger "/>.
    /// </summary>
    public sealed class VoidLogger : IIpcLogger
    {
        /// <summary>Singleton instance.</summary>
        public static readonly VoidLogger Instance = new VoidLogger();

        /// <summary>Ignores all log request.</summary>
        public void Log(LogLevel level, string format, params object[] args) { }

        /// <nodoc />
        public void Dispose() { }
    }
}
