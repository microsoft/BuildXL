// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;

namespace BuildXL.Execution.Analyzer
{
    /// <summary>
    /// Writes to multiple text writers
    /// </summary>
    internal sealed class MultiWriter
    {
        private readonly TextWriter[] m_writers;

        public MultiWriter(params TextWriter[] writers)
        {
            m_writers = writers;
        }

        public void WriteLine()
        {
            foreach (var writer in m_writers)
            {
                writer.WriteLine();
            }
        }

        public void WriteLine(string value)
        {
            foreach (var writer in m_writers)
            {
                writer.WriteLine(value);
            }
        }

        public void WriteLine(string value, params object[] args)
        {
            foreach (var writer in m_writers)
            {
                writer.WriteLine(value, args);
            }
        }
    }
}
