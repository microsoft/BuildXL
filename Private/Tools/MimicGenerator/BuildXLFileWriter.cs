// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Tool.MimicGenerator
{
    /// <summary>
    /// Base class for writing a file
    /// </summary>
    public abstract class BuildXLFileWriter : IDisposable
    {
        /// <summary>
        /// Absolute path of the underlying file
        /// </summary>
        public readonly string AbsolutePath;

        /// <summary>
        /// StreamWriter
        /// </summary>
        public StreamWriter Writer
        {
            get
            {
                if (m_writer == null)
                {
                    if (!m_wasInitialized)
                    {
                        m_writer = new StreamWriter(AbsolutePath, append: false);
                        WriteStart();
                        m_wasInitialized = true;
                    }
                    else
                    {
                        m_writer = new StreamWriter(AbsolutePath, append: true);
                    }
                }

                return m_writer;
            }
        }

        private StreamWriter m_writer;

        private bool m_wasInitialized = false;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="absolutePath">Absolute path to the output file</param>
        protected BuildXLFileWriter(string absolutePath)
        {
            AbsolutePath = absolutePath;
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
        }

        /// <summary>
        /// Closes the StreamWriter so there aren't a bazillion handles open at once. Future writes are still allowed
        /// and will cause a new underlying StreamWriter to be created.
        /// </summary>
        public void SoftClose()
        {
            m_writer.Dispose();
            m_writer = null;
        }

        /// <summary>
        /// Writes the start of the file
        /// </summary>
        protected abstract void WriteStart();

        /// <summary>
        /// Writes the end of the file
        /// </summary>
        protected abstract void WriteEnd();

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Usage", "CA1816")]
        [SuppressMessage("Microsoft.Design", "CA1063")]
        public void Dispose()
        {
            WriteEnd();
            m_writer.Dispose();
            m_writer = null;
        }
    }
}
