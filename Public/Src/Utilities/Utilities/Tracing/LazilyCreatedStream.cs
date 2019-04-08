// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Stream that is lazily created on its first write
    /// </summary>
    public class LazilyCreatedStream : Stream
    {
        private readonly string m_path;
        private readonly object m_lock = new object();
        private FileStream m_stream;

        /// <summary>
        /// Stream that is lazily created. Upon construction, it will attempt to open and delete the file as a test as
        /// to whether the operation will work. It also ensures that a previous version of the file won't exist if this
        /// stream is never written to
        /// </summary>
        public LazilyCreatedStream(string path)
        {
            m_path = path;
            using (FileStream fs = new FileStream(m_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Delete, 4096, FileOptions.DeleteOnClose))
            {
                // Just making sure we can actually create the file. This also deletes the old one
            }
        }

        private void EnsureCreated()
        {
            if (m_stream == null)
            {
                lock (m_lock)
                {
                    if (m_stream == null)
                    {
                        m_stream = File.Open(m_path, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete);
                    }
                }
            }
        }

        /// <inheritdoc/>
        public override bool CanRead => true;

        /// <inheritdoc/>
        public override bool CanSeek => true;

        /// <inheritdoc/>
        public override bool CanWrite => true;

        /// <inheritdoc/>
        public override void Flush()
        {
            m_stream?.Flush();
        }

        /// <inheritdoc/>
        public override long Length
        {
            get
            {
                if (m_stream == null)
                {
                    return 0;
                }

                EnsureCreated();
                return m_stream.Length;
            }
        }

        /// <inheritdoc/>
        public override long Position
        {
            get
            {
                return m_stream?.Length ?? 0;
            }

            set
            {
                EnsureCreated();
                m_stream.Position = value;
            }
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (m_stream == null)
            {
                return 0;
            }

            EnsureCreated();
            return m_stream.Read(buffer, offset, count);
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            EnsureCreated();
            return m_stream.Seek(offset, origin);
        }

        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            EnsureCreated();
            m_stream.SetLength(value);
        }

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCreated();
            m_stream.Write(buffer, offset, count);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            m_stream?.Dispose();
        }
    }
}
