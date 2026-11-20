// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Writes the execution log as consecutive parts in a &lt;log name&gt;.parts folder next to the logs directory and uploads each
    /// completed part to the <see cref="LogsArtifact"/> while the build keeps running. Concatenating the parts in order gives the
    /// original log. If the log never grows past the first part, it is moved to its usual path instead, so smaller builds keep a
    /// single execution log.
    /// </summary>
    internal sealed class ChunkedExecutionLogStream : Stream
    {
        private readonly string m_path;
        private readonly string m_partsFolder;
        private readonly string m_fileName;
        private readonly long m_chunkSize;
        private FileStream m_current;
        private int m_index;

        public ChunkedExecutionLogStream(string path, long chunkSize)
        {
            m_path = path;
            m_fileName = Path.GetFileName(path);
            m_partsFolder = LogsArtifact.GetEarlyUploadFolder(Path.GetDirectoryName(path), m_fileName + ".parts");
            m_chunkSize = chunkSize;

            // Remove the log and parts of an earlier build in the same logs directory.
            File.Delete(m_path);
            if (Directory.Exists(m_partsFolder))
            {
                Directory.Delete(m_partsFolder, recursive: true);
            }

            Directory.CreateDirectory(m_partsFolder);
            m_current = OpenChunk();
        }

        private string ChunkPath => Path.Combine(m_partsFolder, $"{m_fileName}.{m_index:D3}");

        private FileStream OpenChunk() => File.Open(ChunkPath, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete);

        public override void Write(byte[] buffer, int offset, int count)
        {
            m_current.Write(buffer, offset, count);
            if (m_current.Length >= m_chunkSize)
            {
                m_current.Dispose();
                LogsArtifact.UploadEarly(m_partsFolder, new[] { ChunkPath }, Path.GetFileName(ChunkPath));
                m_index++;
                m_current = OpenChunk();
            }
        }

        public override void Flush() => m_current.Flush();

        protected override void Dispose(bool disposing)
        {
            if (disposing && m_current != null)
            {
                bool empty = m_current.Length == 0 && m_index > 0;
                m_current.Dispose();
                m_current = null;
                if (m_index == 0)
                {
                    // A single part is the whole log: keep it at its usual path, where it is uploaded with the rest of the logs.
                    File.Delete(m_path);
                    File.Move(ChunkPath, m_path);
                    Directory.Delete(m_partsFolder);
                }
                else if (empty)
                {
                    File.Delete(ChunkPath);
                }
                else
                {
                    LogsArtifact.UploadEarly(m_partsFolder, new[] { ChunkPath }, Path.GetFileName(ChunkPath));
                }
            }

            base.Dispose(disposing);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
