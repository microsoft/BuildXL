// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Extensions
{
    public class StreamExtensionsTests
    {
        [Fact]
        public async Task ReadBytes()
        {
            var content = new byte[100];
            var r = new Random();
            r.NextBytes(content);

            var buffer = new byte[100];
            using (var testReadStream = new PartialReadStream(content, buffer.Length / 2))
            {
                await testReadStream.ReadBytes(buffer, buffer.Length);
                Assert.True(content.SequenceEqual(buffer));
            }
        }

        [Fact]
        public async Task CopyToWithFullBufferAsync()
        {
            var content = new byte[1234];
            var r = new Random();
            r.NextBytes(content);

            using (var testReadStream = new PartialReadStream(content, 100))
            {
                int bytesWritten = 0;
                using (var testWriteStream = new WriteCallbackStream((bufferToWrite, offset, count) =>
                {
                    Assert.Equal(250, bufferToWrite.Length);
                    bytesWritten += count;
                    if (bytesWritten < content.Length)
                    {
                        Assert.Equal(250, count);
                    }
                    else
                    {
                        Assert.Equal(content.Length % 250, count);
                    }
                }))
                {
                    await testReadStream.CopyToWithFullBufferAsync(testWriteStream, 250);
                }
            }
        }

        private class PartialReadStream : Stream
        {
            private readonly byte[] _content;
            private readonly int _maxReadLength;

            public PartialReadStream(byte[] content, int maxReadLength)
            {
                _content = content;
                _maxReadLength = maxReadLength;
            }

            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                count = Math.Min(count, _maxReadLength);
                count = Math.Min(count, (int)(Length - Position));

                for (int i = 0; i < count; i++)
                {
                    buffer[offset + i] = _content[Position];
                    Position++;
                }

                return Task.FromResult(count);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => _content.Length;

            public override long Position { get; set; }
        }

        private class WriteCallbackStream : Stream
        {
            private readonly Action<byte[], int, int> _writeCallback;

            public WriteCallbackStream(Action<byte[], int, int> writeCallback)
            {
                _writeCallback = writeCallback;
            }

            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                _writeCallback(buffer, offset, count);
                return Task.FromResult(0);
            }

            public override bool CanRead
            {
                get { throw new NotImplementedException(); }
            }

            public override bool CanSeek
            {
                get { throw new NotImplementedException(); }
            }

            public override bool CanWrite => true;

            public override long Length
            {
                get { throw new NotImplementedException(); }
            }

            public override long Position
            {
                get { throw new NotImplementedException(); }
                set { throw new NotImplementedException(); }
            }
        }
    }
}
