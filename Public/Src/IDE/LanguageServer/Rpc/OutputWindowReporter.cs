// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.Tracing;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Ide.JsonRpc
{
    /// <nodoc />
    internal sealed class OutputWindowReporter : IOutputWindowReporter
    {
        private const string TraceTargetName = "dscript/outputTrace";
        private readonly StreamJsonRpc.JsonRpc m_pushRpc;

        public OutputWindowReporter(StreamJsonRpc.JsonRpc pushRpc)
        {
            m_pushRpc = pushRpc;
        }

        /// <inheritdoc />
        public void WriteLine(EventLevel level, string message)
        {
            m_pushRpc
                .NotifyWithParameterObjectAsync(TraceTargetName, LogMessageParams.Create(level, message))
                .IgnoreErrors();
        }
    }
}
