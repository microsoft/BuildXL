// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PipExecutionSimulator
{
    class ProgressStream : Stream
    {
        private Stream m_innerStream;
        private long m_length;
        private long m_position;
        private long m_nextReportPosition;
        private string m_name;

        public ProgressStream(Stream innerStream, string name)
        {
            m_innerStream = innerStream;
            m_name = name;
            m_length = innerStream.Length;
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void Flush()
        {
            m_innerStream.Flush();
        }

        public override long Length
        {
            get { return m_length; }
        }

        public override long Position
        {
            get
            {
                return m_innerStream.Position;
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            m_position += count;
            if (m_position > m_nextReportPosition)
            {
                m_nextReportPosition += (1024 * 1024);
                Console.WriteLine("{3} read bytes: {0} / {1} ({2} %)", m_position, m_length, ((double)m_position * 100) / m_length, m_name);
            }

            return m_innerStream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override void Close()
        {
            m_innerStream.Close();
        }
    }
}
