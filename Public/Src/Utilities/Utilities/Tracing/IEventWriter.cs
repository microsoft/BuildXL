// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.IO;

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// The event writer interface used for printing log events.
    /// </summary>
    public interface IEventWriter : IDisposable
    {
        /// <summary>
        /// Write a log entry with a given level.
        /// </summary>
        void WriteLine(EventLevel level, string text);

        /// <summary>
        /// Flushes the writer.
        /// </summary>
        void Flush();
    }

    /// <summary>
    /// Adapter class that glues together <see cref="IEventWriter"/> implementation with the <see cref="TextWriter"/> instance.
    /// </summary>
    internal sealed class TextEventWriter : IEventWriter
    {
        private readonly TextWriter m_textWriter;

        public TextEventWriter(TextWriter textWriter)
        {
            Contract.Requires(textWriter != null);
            m_textWriter = textWriter;
        }

        /// <inheritdoc />
        public void WriteLine(EventLevel level, string text)
        {
            m_textWriter.WriteLine(text);
        }

        /// <inheritdoc />
        public void Flush() => m_textWriter.Flush();

        /// <inheritdoc />
        public void Dispose()
        {
            m_textWriter.Dispose();
        }
    }
}
