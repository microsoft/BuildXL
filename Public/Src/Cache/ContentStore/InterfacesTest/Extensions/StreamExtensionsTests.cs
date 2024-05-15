// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Extensions
{
    [Trait("DisableFailFast", "true")]
    public class StreamExtensionsTests
    {
        [Fact]
        public async Task ReadBytesAsync_ReadsFewerBytesThanBufferLength()
        {
            var data = Encoding.UTF8.GetBytes("Hello, world!");
            using var stream = new MemoryStream(data);
            var buffer = new byte[1024];
            int bytesRead = await stream.ReadBytesAsync(buffer, 5);
            Assert.Equal(5, bytesRead);
            Assert.Equal("Hello", Encoding.UTF8.GetString(buffer, 0, bytesRead));
        }

        [Fact]
        public async Task ReadBytesAsync_ReadsExactlyBufferLength()
        {
            var data = Encoding.UTF8.GetBytes("Hello, world!");
            using var stream = new MemoryStream(data);
            var buffer = new byte[13];
            int bytesRead = await stream.ReadBytesAsync(buffer, buffer.Length);
            Assert.Equal(buffer.Length, bytesRead);
            Assert.Equal("Hello, world!", Encoding.UTF8.GetString(buffer, 0, bytesRead));
        }

        [Fact]
        public async Task ReadBytesAsync_ReadsMoreBytesThanAvailable()
        {
            var data = Encoding.UTF8.GetBytes("Hello");
            using var stream = new MemoryStream(data);
            var buffer = new byte[1024];
            int bytesRead = await stream.ReadBytesAsync(buffer, 10);
            Assert.Equal(data.Length, bytesRead);
            Assert.Equal("Hello", Encoding.UTF8.GetString(buffer, 0, bytesRead));
        }

        [Fact]
        public Task ReadBytesAsync_ThrowsExceptionOnNullStream()
        {
            Stream stream = null;
            var buffer = new byte[1024];
            return Assert.ThrowsAsync<ContractException>(() => stream.ReadBytesAsync(buffer, 5));
        }

        [Fact]
        public Task ReadBytesAsync_ThrowsExceptionOnNullBuffer()
        {
            var stream = new MemoryStream();
            byte[] buffer = null;
            return Assert.ThrowsAsync<ContractException>(() => stream.ReadBytesAsync(buffer, 5));
        }

        [Fact]
        public Task ReadBytesAsync_ThrowsExceptionOnCountGreaterThanBufferLength()
        {
            var stream = new MemoryStream();
            var buffer = new byte[5];
            return Assert.ThrowsAsync<ContractException>(() => stream.ReadBytesAsync(buffer, 10));
        }

        [Fact]
        public async Task CopyToWithFullBufferAsync_CopiesWithDefaultBuffer()
        {
            var sourceData = Encoding.UTF8.GetBytes("Hello, world!");
            using var sourceStream = new MemoryStream(sourceData);
            using var destinationStream = new MemoryStream();
            await sourceStream.CopyToWithFullBufferAsync(destinationStream);
            destinationStream.Seek(0, SeekOrigin.Begin);
            var copiedData = new byte[sourceData.Length];
            await destinationStream.ReadAsync(copiedData, 0, copiedData.Length);
            Assert.Equal(sourceData, copiedData);
        }

        [Fact]
        public async Task CopyToWithFullBufferAsync_CopiesWithProvidedBuffer()
        {
            var sourceData = Encoding.UTF8.GetBytes("Hello, world!");
            using var sourceStream = new MemoryStream(sourceData);
            using var destinationStream = new MemoryStream();
            var buffer = new byte[5];
            await sourceStream.CopyToWithFullBufferAsync(destinationStream, buffer);
            destinationStream.Seek(0, SeekOrigin.Begin);
            var copiedData = new byte[sourceData.Length];
            await destinationStream.ReadAsync(copiedData, 0, copiedData.Length);
            Assert.Equal(sourceData, copiedData);
        }

        [Fact]
        public async Task CopyToWithFullBufferAsync_CopiesFromEmptyStream()
        {
            using var sourceStream = new MemoryStream();
            using var destinationStream = new MemoryStream();
            await sourceStream.CopyToWithFullBufferAsync(destinationStream);
            Assert.Equal(0, destinationStream.Length);
        }

        [Fact]
        public async Task CopyToWithFullBufferAsync_ThrowsExceptionOnNullSourceStream()
        {
            Stream sourceStream = null;
            using var destinationStream = new MemoryStream();
            await Assert.ThrowsAsync<ContractException>(() => sourceStream.CopyToWithFullBufferAsync(destinationStream));
        }

        [Fact]
        public async Task CopyToWithFullBufferAsync_ThrowsExceptionOnNullDestinationStream()
        {
            using var sourceStream = new MemoryStream();
            Stream destinationStream = null;
            await Assert.ThrowsAsync<ContractException>(() => sourceStream.CopyToWithFullBufferAsync(destinationStream));
        }

        private class TestStream : MemoryStream
        {
            public bool FlushCalled { get; private set; }

            public TestStream() : base()
            {
            }

            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                FlushCalled = true;
                return base.FlushAsync(cancellationToken);
            }
        }

        [Fact]
        public async Task CopyToWithFullBufferAsync_FlushesDestinationStream()
        {
            var sourceData = Encoding.UTF8.GetBytes("Hello, world!");
            using var sourceStream = new MemoryStream(sourceData);
            using var destinationStream = new TestStream();

            await sourceStream.CopyToWithFullBufferAsync(destinationStream);

            Assert.True(destinationStream.FlushCalled, "FlushAsync was not called on the destination stream.");
        }

        private class NotImplementedFlushStream : MemoryStream
        {
            public NotImplementedFlushStream() : base()
            {
            }

            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }
        }

        [Fact]
        public async Task CopyToWithFullBufferAsync_HandlesNotImplementedExceptionInFlushAsync()
        {
            var sourceData = Encoding.UTF8.GetBytes("Hello, world!");
            using var sourceStream = new MemoryStream(sourceData);
            using var destinationStream = new NotImplementedFlushStream();

            // Execute the copy method and ensure it does not throw
            await sourceStream.CopyToWithFullBufferAsync(destinationStream);

            // Check that the copy was successful despite the NotImplementedException in FlushAsync
            destinationStream.Seek(0, SeekOrigin.Begin);
            var copiedData = new byte[sourceData.Length];
            await destinationStream.ReadAsync(copiedData, 0, copiedData.Length);
            Assert.Equal(sourceData, copiedData);
        }

        public async Task CopyToWithFullBufferAsync_UsesBufferFromPool()
        {
            var sourceData = Encoding.UTF8.GetBytes("Hello, world!");
            using var sourceStream = new MemoryStream(sourceData);
            using var destinationStream = new MemoryStream();

            var count = GlobalObjectPools.FileIOBuffersArrayPool.UseCount;
            await sourceStream.CopyToWithFullBufferAsync(destinationStream);
            Assert.True(GlobalObjectPools.FileIOBuffersArrayPool.UseCount > count, "Buffer was not requested from the pool.");

            destinationStream.Seek(0, SeekOrigin.Begin);
            var copiedData = new byte[sourceData.Length];
            await destinationStream.ReadAsync(copiedData, 0, copiedData.Length);
            Assert.Equal(sourceData, copiedData);
        }

        [Fact]
        public async Task ReadBytes()
        {
            var content = new byte[100];
            var r = new Random();
            r.NextBytes(content);

            var buffer = new byte[100];
            using (var testReadStream = new PartialReadStream(content, buffer.Length / 2))
            {
                await testReadStream.ReadBytesAsync(buffer, buffer.Length);
                Assert.True(content.SequenceEqual(buffer));
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
    }
}
