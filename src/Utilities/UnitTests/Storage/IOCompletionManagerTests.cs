// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BuildXL.Native.IO;
using BuildXL.Native.Streams;
using BuildXL.Native.Streams.Windows;
using Microsoft.Win32.SafeHandles;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Storage
{
    /// <summary>
    /// Low level tests for receiving completion callbacks on an <see cref="IOCompletionManager" />.
    /// </summary>
    // Depends on IOCompletionManager, which explicitly depends on BuildXL.Native.IO.Windows.FileSystem for IO completion (not on Unix)
    [Trait("Category", "WindowsOSOnly")]
    public class IOCompletionManagerTests : TemporaryStorageTestBase
    {

        [Fact]
        public void TrivialCreationAndCleanup()
        {
            using (new IOCompletionManager(numberOfCompletionPortThreads: 4))
            {
            }
        }

        [Fact]
        public void CallbackForCompletedReadAtEndOfEmptyFile()
        {
            FileAsyncIOResult result;
            using (var io = new IOCompletionManager())
            using (var target = new BlockingCompletionTarget(bufferSize: 10))
            using (
                SafeFileHandle handle = CreateOrOpenFile(
                    GetFullPath("emptyFile"),
                    FileDesiredAccess.GenericWrite | FileDesiredAccess.GenericRead,
                    FileShare.Read | FileShare.Delete,
                    FileMode.Create))
            {
                io.BindFileHandle(handle);

                unsafe
                {
                    io.ReadFileOverlapped(target, handle, target.PinBuffer(), target.Buffer.Length, fileOffset: 0);
                }

                result = target.Wait();
            }

            XAssert.IsTrue(result.ErrorIndicatesEndOfFile, "File is empty; EOF read expected");
            XAssert.AreEqual(FileAsyncIOStatus.Failed, result.Status);
        }

        [Fact]
        public void ReadFileToEnd()
        {
            const int NumberOfWords = 1024 * 1024;
            const int TotalSize = NumberOfWords * 4;

            string path = GetFullPath("file");

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Delete))
            using (var writer = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true))
            {
                for (int i = 0; i < NumberOfWords; i++)
                {
                    writer.Write((int)i);
                }
            }

            using (var memoryStream = new MemoryStream(capacity: NumberOfWords * 4))
            {
                using (var io = new IOCompletionManager())
                using (var target = new BlockingCompletionTarget(bufferSize: 64 * 1024))
                using (
                    SafeFileHandle handle = CreateOrOpenFile(
                        path,
                        FileDesiredAccess.GenericRead,
                        FileShare.Read | FileShare.Delete,
                        FileMode.Open))
                {
                    io.BindFileHandle(handle);

                    while (true)
                    {
                        unsafe
                        {
                            io.ReadFileOverlapped(target, handle, target.PinBuffer(), target.Buffer.Length, fileOffset: memoryStream.Length);
                        }

                        FileAsyncIOResult result = target.Wait();

                        if (result.Status == FileAsyncIOStatus.Failed)
                        {
                            XAssert.IsTrue(result.ErrorIndicatesEndOfFile, "Unexpected non-EOF read failure.");
                            break;
                        }
                        else
                        {
                            XAssert.AreEqual(FileAsyncIOStatus.Succeeded, result.Status);
                        }

                        XAssert.AreNotEqual(0, result.BytesTransferred);
                        XAssert.IsTrue(
                            memoryStream.Length + result.BytesTransferred <= TotalSize,
                            "Too many bytes read; Read {0} so far and just got another {1} bytes",
                            memoryStream.Length,
                            result.BytesTransferred);
                        memoryStream.Write(target.Buffer, 0, result.BytesTransferred);
                    }
                }

                memoryStream.Position = 0;

                using (var reader = new BinaryReader(memoryStream, Encoding.UTF8, leaveOpen: true))
                {
                    for (int i = 0; i < NumberOfWords; i++)
                    {
                        XAssert.AreEqual(i, reader.ReadInt32(), "File contents read incorrectly; offset {0}", i * 4);
                    }
                }
            }
        }

        /// <summary>
        /// Tests that we can manage a large number of outstanding reads. We force this by reading a named pipe file, since we can delay read completion.
        /// </summary>
        [Fact]
        public void ConcurrentReadsSingleBatch()
        {
            const int NumberOfConcurrentReads = 128; // This should be larger than the batch size at which we allocate OVERLAPPED structures.

            WithPipePair(
                (server, client) =>
                {
                    using (var io = new IOCompletionManager())
                    {
                        io.BindFileHandle(client);

                        var targets = new BlockingCompletionTarget[NumberOfConcurrentReads];
                        var completions = new bool[NumberOfConcurrentReads];
                        var bufferToWrite = new byte[NumberOfConcurrentReads * 4];

                        using (var writeBufferStream = new MemoryStream(bufferToWrite, writable: true))
                        {
                            using (var writer = new BinaryWriter(writeBufferStream))
                            {
                                for (int i = 0; i < NumberOfConcurrentReads; i++)
                                {
                                    writer.Write((int)i);
                                }
                            }
                        }

                        try
                        {
                            for (int i = 0; i < targets.Length; i++)
                            {
                                targets[i] = new BlockingCompletionTarget(bufferSize: 8);
                                unsafe
                                {
                                    // Note that non-seekable streams mandate an offset of 0 for all requests.
                                    // https://msdn.microsoft.com/en-us/library/windows/desktop/ms684342%28v=vs.85%29.aspx
                                    io.ReadFileOverlapped(targets[i], client, targets[i].PinBuffer(), 4, fileOffset: 0);
                                }
                            }

                            server.Write(bufferToWrite, 0, bufferToWrite.Length);
                            server.Flush();

                            for (int i = 0; i < targets.Length; i++)
                            {
                                FileAsyncIOResult result = targets[i].Wait();
                                XAssert.AreEqual(FileAsyncIOStatus.Succeeded, result.Status);
                                XAssert.AreEqual(result.BytesTransferred, 4);
                                int completedValue = BitConverter.ToInt32(targets[i].Buffer, 0);

                                XAssert.IsTrue(completedValue >= 0 && completedValue < completions.Length);
                                XAssert.IsFalse(completions[completedValue], "Value {0} completed multiple times", completedValue);
                                completions[completedValue] = true;
                            }
                        }
                        finally
                        {
                            foreach (BlockingCompletionTarget target in targets)
                            {
                                if (target != null)
                                {
                                    target.Dispose();
                                }
                            }
                        }
                    }
                });
        }

        /// <summary>
        /// Creates a writable pipe server stream and a file handle for the readable client stream.
        /// This allows forcing an arbitrary number of outstanding I/O requests since pipe reads block until the other side writes or closes.
        /// </summary>
        private static void WithPipePair(Action<NamedPipeServerStream, SafeFileHandle> action)
        {
            string pipeId = Guid.NewGuid().ToString("N");
            string pipePath = @"\\.\pipe\" + pipeId;

            using (var server = new NamedPipeServerStream(pipeId, PipeDirection.Out, maxNumberOfServerInstances: 1))
            {
                using (
                    SafeFileHandle client = CreateOrOpenFile(
                        pipePath,
                        FileDesiredAccess.GenericRead,
                        FileShare.ReadWrite | FileShare.Delete,
                        FileMode.Open))
                {
                    server.WaitForConnection();
                    action(server, client);
                }
            }
        }

        private static SafeFileHandle CreateOrOpenFile(
            string path,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileMode creationDisposition)
        {
            SafeFileHandle handle;
            var result = FileUtilities.TryCreateOrOpenFile(
                path,
                desiredAccess,
                shareMode,
                creationDisposition,
                FileFlagsAndAttributes.FileFlagOverlapped,
                out handle);

            if (!result.Succeeded)
            {
                throw result.CreateExceptionForError();
            }

            return handle;
        }

        private class BlockingCompletionTarget : IIOCompletionTarget, IDisposable
        {
            private readonly ManualResetEvent m_event = new ManualResetEvent(initialState: true);
            private FileAsyncIOResult m_result = default(FileAsyncIOResult);
            private readonly byte[] m_buffer;
            private GCHandle m_pinningHandle;

            public BlockingCompletionTarget(int bufferSize)
            {
                m_buffer = new byte[bufferSize];
            }

            public void OnCompletion(FileAsyncIOResult asyncIOResult)
            {
                m_result = asyncIOResult;

                Contract.Assume(m_pinningHandle.IsAllocated);
                m_pinningHandle.Free();

                m_event.Set();
            }

            public byte[] Buffer
            {
                get { return m_buffer; }
            }

            /// <summary>
            /// Pins the buffer for this target. It will not be unpinned until <see cref="OnCompletion"/> is called.
            /// </summary>
            public unsafe byte* PinBuffer()
            {
                m_event.WaitOne();
                m_event.Reset();

                Contract.Assume(!m_pinningHandle.IsAllocated);
                m_pinningHandle = GCHandle.Alloc(m_buffer, GCHandleType.Pinned);
                return (byte*)m_pinningHandle.AddrOfPinnedObject();
            }

            public FileAsyncIOResult Wait()
            {
                m_event.WaitOne();
                return m_result;
            }

            public void Dispose()
            {
                // Note that we don't release the pinning handle if OnCompletion was never called.
                // We'd rather leak the buffers than corrupt memory if a native operation completes somehow following a test failure (broken manager?)
                m_event.Dispose();
            }
        }
    }
}
