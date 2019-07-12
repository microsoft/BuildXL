// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Diagnostics.Tracing;
using BuildXL.Utilities.Tracing;
using BuildXL.Ide.JsonRpc;

namespace BuildXL.Ide.LanguageServer.Tracing
{
    /// <summary>
    /// Event writer that writes events to a given <see cref="TextWriter"/> and sends them to the language server through the <see cref="StreamJsonRpc.JsonRpc"/> channel.
    /// </summary>
    internal sealed class OutputWindowEventWriter : IEventWriter
    {
        private readonly IOutputWindowReporter m_outputWindowReporter;
        private readonly TextWriter m_logFileWriter;
        private readonly EventLevel m_outputPaneVerbosity;

        public OutputWindowEventWriter(IOutputWindowReporter outputWindowReporter, TextWriter logFileWriter, EventLevel outputPaneVerbosity)
        {
            m_outputWindowReporter = outputWindowReporter;
            m_logFileWriter = logFileWriter;
            m_outputPaneVerbosity = outputPaneVerbosity;
        }

        /// <inheritdoc />
        public void Flush() => m_logFileWriter.Flush();

        /// <inheritdoc />
        public void Dispose() => m_logFileWriter.Dispose();

        /// <inheritdoc />
        public void WriteLine(EventLevel level, string text)
        {
            m_logFileWriter.WriteLine(text);

            if (level <= m_outputPaneVerbosity)
            {
                m_outputWindowReporter.WriteLine(level, text);
            }
        }
    }
}
