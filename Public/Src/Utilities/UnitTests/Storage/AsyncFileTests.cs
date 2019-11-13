// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Native.Streams;
using BuildXL.Native.Streams.Windows;
using BuildXL.Storage;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Storage
{
    public class AsyncFileTests : TemporaryStorageTestBase
    {
        
        [Fact]
        public async Task ReadEmptyFile()
        {
            string path = GetFullPath("file");
            using (File.Create(path))
            {
            }

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
                    XAssert.IsTrue(file.CanRead);
                    XAssert.IsFalse(file.CanWrite);

                    var buffer = new byte[10];
                    FileAsyncIOResult result = await file.ReadAsync(buffer, buffer.Length, 0);
                    XAssert.AreEqual(FileAsyncIOStatus.Failed, result.Status);
                    XAssert.IsTrue(result.ErrorIndicatesEndOfFile);

                    result = await file.ReadAsync(buffer, buffer.Length, 16);
                    XAssert.AreEqual(FileAsyncIOStatus.Failed, result.Status);
                    XAssert.IsTrue(result.ErrorIndicatesEndOfFile);
                }
            }
        }
        
        [Fact]
        public async Task ReadFileRandomAccess()
        {
            const int NumberOfReads = 16;
            const int NumberOfWordsPerRead = 64 * 1024;
            const int NumberOfBytesPerRead = NumberOfWordsPerRead * 4;
            const int NumberOfWords = NumberOfWordsPerRead * NumberOfReads;
            const int TotalSize = NumberOfWords * 4;

            string path = GetFullPath("file");

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Delete))
            {
                using (var writer = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true))
                {
                    for (int i = 0; i < NumberOfWords; i++)
                    {
                        writer.Write((int)i);
                    }
                }
            }

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
                    XAssert.IsTrue(file.CanRead);
                    XAssert.IsFalse(file.CanWrite);

                    var readBuffer = new byte[TotalSize];
                    var readTasks = new Task[NumberOfReads];
                    for (int i = 0; i < readTasks.Length; i++)
                    {
                        int offset = NumberOfBytesPerRead * i;
                        readTasks[i] = Task.Run(
                            async () =>
                                  {
                                      byte[] localBuffer = new byte[NumberOfBytesPerRead];
                                      int readSoFar = 0;
                                      while (readSoFar < NumberOfBytesPerRead)
                                      {
                                          FileAsyncIOResult result =
                                              await file.ReadAsync(localBuffer, bytesToRead: NumberOfBytesPerRead - readSoFar, fileOffset: offset + readSoFar);
                                          XAssert.AreEqual(FileAsyncIOStatus.Succeeded, result.Status);
                                          XAssert.IsTrue(result.BytesTransferred > 0);
                                          XAssert.IsTrue(readSoFar + result.BytesTransferred <= NumberOfBytesPerRead);

                                          Buffer.BlockCopy(localBuffer, 0, readBuffer, offset + readSoFar, result.BytesTransferred);
                                          readSoFar += result.BytesTransferred;
                                      }

                                      Contract.Assert(readSoFar == NumberOfBytesPerRead);
                                  });
                    }

                    for (int i = 0; i < readTasks.Length; i++)
                    {
                        await readTasks[i];
                    }

                    using (var reader = new BinaryReader(new MemoryStream(readBuffer, writable: false), Encoding.UTF8, leaveOpen: false))
                    {
                        for (int i = 0; i < NumberOfWords; i++)
                        {
                            XAssert.AreEqual(i, reader.ReadInt32());
                        }
                    }
                }
            }
        }
    }
}
