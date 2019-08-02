// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Native.Streams;
using BuildXL.Native.Streams.Windows;
using BuildXL.Storage;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Storage
{
    public class AsyncFileStreamTests : TemporaryStorageTestBase
    {
        [Fact]
        public void ReadSmallFile()
        {
            VerifyReadingValidatableFile(GetFullPath("file"), numberOfBytes: 8, readSize: AsyncFileStream.DefaultBufferSize);
        }

        [Fact]
        public void ReadAsyncFileStreamWithContentsAtBufferSize()
        {
            VerifyReadingValidatableFile(GetFullPath("file"), numberOfBytes: AsyncFileStream.DefaultBufferSize, readSize: AsyncFileStream.DefaultBufferSize);
        }

        [Fact]
        public void ReadAsyncFileStreamWithReadsSmallerThanBuffer()
        {
            VerifyReadingValidatableFile(GetFullPath("file"), numberOfBytes: AsyncFileStream.DefaultBufferSize * 3, readSize: AsyncFileStream.DefaultBufferSize / 3);
        }

        [Fact]
        public void ReadAsyncFileStreamWithReadsLargerThanBuffer()
        {
            VerifyReadingValidatableFile(GetFullPath("file"), numberOfBytes: AsyncFileStream.DefaultBufferSize * 3, readSize: AsyncFileStream.DefaultBufferSize * 2);
        }

        [Fact]
        public void ReadWithSeeking()
        {
            int NumberOfBytes = AsyncFileStream.DefaultBufferSize * 2;

            string path = GetFullPath("file");
            FileStreamValidation.WriteValidatableFile(path, NumberOfBytes);

            byte[] buffer = new byte[NumberOfBytes];
            WithReadableStream(
                path,
                async stream =>
                      {
                          // Here we ping-pong around in the stream doing reads less than the buffer size.
                          // The intent is to verify that seeking properly flushes the partially-full read buffer.

                          // Read 1/4 of the stream, starting halfway
                          stream.Seek(AsyncFileStream.DefaultBufferSize, SeekOrigin.Begin);
                          await Read(stream, buffer, offset: AsyncFileStream.DefaultBufferSize, count: AsyncFileStream.DefaultBufferSize / 2);

                          // Read 1/4 of the stream, starting at the beginning
                          stream.Seek(0, SeekOrigin.Begin);
                          await Read(stream, buffer, offset: 0, count: AsyncFileStream.DefaultBufferSize / 2);

                          // Read 1/4 of the stream, again from the second half
                          stream.Seek(AsyncFileStream.DefaultBufferSize + (AsyncFileStream.DefaultBufferSize / 2), SeekOrigin.Begin);
                          await Read(stream, buffer, offset: AsyncFileStream.DefaultBufferSize + (AsyncFileStream.DefaultBufferSize / 2), count: AsyncFileStream.DefaultBufferSize / 2);

                          // Read 1/4 of the stream, again from the first half
                          stream.Seek(AsyncFileStream.DefaultBufferSize / 2, SeekOrigin.Begin);
                          await Read(stream, buffer, offset: AsyncFileStream.DefaultBufferSize / 2, count: AsyncFileStream.DefaultBufferSize / 2);
                      });

            FileStreamValidation.ValidateBuffer(buffer, NumberOfBytes);
        }

        private static void VerifyReadingValidatableFile(string path, int numberOfBytes, int readSize)
        {
            FileStreamValidation.WriteValidatableFile(path, numberOfBytes);

            using (var targetStream = new MemoryStream())
            {
                CopyReadableStream(path, readSize, targetStream);

                FileStreamValidation.ValidateStream(targetStream, numberOfBytes);
            }
        }

        private static void CopyReadableStream(string path, int readSize, MemoryStream target)
        {
            WithReadableStream(
                path,
                async stream =>
                {
                    var buffer = new byte[readSize];
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, readSize)) != 0)
                    {
                        target.Write(buffer, 0, read);
                    }
                });

            target.Position = 0;
        }

        private static async Task Read(AsyncFileStream stream, byte[] buffer, int offset, int count)
        {
            int remaining = count;
            while (remaining > 0)
            {
                int read = await stream.ReadAsync(buffer, offset, remaining);
                XAssert.AreNotEqual(0, read, "Unexpected EOF");
                remaining -= read;
                offset += read;
            }
        }

        private static void WithReadableStream(string path, Func<AsyncFileStream, Task> action)
        {
#if NET_FRAMEWORK
            using (var io = new IOCompletionManager())
#else
            IIOCompletionManager io = null;
#endif
            {
                using (
                    IAsyncFile file = AsyncFileFactory.CreateOrOpen(
                        path,
                        FileDesiredAccess.GenericRead,
                        FileShare.Read | FileShare.Delete,
                        FileMode.Open,
                        FileFlagsAndAttributes.None,
                        io))
                {
                    using (AsyncFileStream stream = file.CreateReadableStream())
                    {
                        XAssert.IsTrue(stream.CanRead);
                        XAssert.IsTrue(stream.CanSeek);
                        XAssert.IsFalse(stream.CanWrite);

                        action(stream).GetAwaiter().GetResult();
                    }
                }
            }
        }
    }
}
