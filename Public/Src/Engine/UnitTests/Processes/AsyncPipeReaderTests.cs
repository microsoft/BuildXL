// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Native.Streams;
using BuildXL.Processes;
using BuildXL.Processes.Internal;
using BuildXL.Storage;
using Microsoft.Win32.SafeHandles;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Processes
{
    public sealed class AsyncPipeReaderTests : XunitBuildXLTest
    {
        private ITestOutputHelper TestOutput { get; }

        public AsyncPipeReaderTests(ITestOutputHelper output)
            : base(output) => TestOutput = output;

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task TestReadAsync()
        {
            Pipes.CreateInheritablePipe(
                Pipes.PipeInheritance.InheritWrite,
                Pipes.PipeFlags.ReadSideAsync,
                readHandle: out SafeFileHandle readHandle,
                writeHandle: out SafeFileHandle writeHandle);

            IAsyncFile readFile = AsyncFileFactory.CreateAsyncFile(
                readHandle,
                FileDesiredAccess.GenericRead,
                ownsHandle: true,
                kind: FileKind.Pipe);

            var debugMessages = new List<string>();
            var messages = new List<string>();

            using var reader = new AsyncPipeReader(
                readFile,
                msg => 
                {
                    messages.Add(msg);
                    return true;
                },
                Encoding.Unicode,
                SandboxedProcessInfo.BufferSize,
                debugPipeReporter: new AsyncPipeReader.DebugReporter(debugMsg => debugMessages.Add(debugMsg), AsyncPipeReader.DebugReporter.VerbosityLevel.Info));
            reader.BeginReadLine();

            const string Content = nameof(TestReadAsync);
            Task readTask = Task.Run(async () =>
            {
                await reader.WaitUntilEofAsync();
            });

            Task writeTask = Task.Run(() =>
            {
                XAssert.IsTrue(TryWrite(writeHandle, Content, out int _));
                writeHandle.Dispose();
            });

            await Task.WhenAll(readTask, writeTask);

            // Contents:
            // 0. Content string
            // 1. null (EOF)
            XAssert.AreEqual(2, messages.Count);
            XAssert.AreEqual(Content, messages[0]);
            XAssert.AreEqual(null, messages[1]);

            // Pipe can either ends with EOF or broken pipe - there can be race between sending EOF and having broken pipe.
            XAssert.AreEqual(1, debugMessages.Count);
            XAssert.IsTrue(
                debugMessages[0].Contains($"error code: {NativeIOConstants.ErrorBrokenPipe}") 
                || debugMessages[0].Contains($"error code: {NativeIOConstants.ErrorHandleEof}"));
        }


        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestDisconnectedRead()
        {
            Pipes.CreateInheritablePipe(
                Pipes.PipeInheritance.InheritWrite,
                Pipes.PipeFlags.ReadSideAsync,
                readHandle: out SafeFileHandle readHandle,
                writeHandle: out SafeFileHandle writeHandle);

            IAsyncFile readFile = AsyncFileFactory.CreateAsyncFile(
                readHandle,
                FileDesiredAccess.GenericRead,
                ownsHandle: true,
                kind: FileKind.Pipe);

            var debugMessages = new List<string>();
            var messages = new List<string>();

            using var reader = new AsyncPipeReader(
                readFile,
                msg =>
                {
                    messages.Add(msg);
                    return true;
                },
                Encoding.Unicode,
                SandboxedProcessInfo.BufferSize,
                debugPipeReporter: new AsyncPipeReader.DebugReporter(debugMsg => debugMessages.Add(debugMsg), AsyncPipeReader.DebugReporter.VerbosityLevel.Info));
            reader.BeginReadLine();
            readFile.Dispose();

            const string Content = nameof(TestDisconnectedRead);
            XAssert.IsFalse(TryWrite(writeHandle, Content, out int errorCode));
            XAssert.AreEqual(NativeIOConstants.ErrorNoData, errorCode);
            writeHandle.Dispose();
        }

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public async Task TestCancelAsync(bool retryOnCancel)
        {
            Pipes.CreateInheritablePipe(
                Pipes.PipeInheritance.InheritWrite,
                Pipes.PipeFlags.ReadSideAsync,
                readHandle: out SafeFileHandle readHandle,
                writeHandle: out SafeFileHandle writeHandle);

            IAsyncFile readFile = AsyncFileFactory.CreateAsyncFile(
                readHandle,
                FileDesiredAccess.GenericRead,
                ownsHandle: true,
                kind: FileKind.Pipe);

            var debugMessages = new List<string>();
            var messages = new List<string>();

            using var reader = new AsyncPipeReader(
                readFile,
                msg =>
                {
                    messages.Add(msg);
                    return true;
                },
                Encoding.Unicode,
                SandboxedProcessInfo.BufferSize,
                numOfRetriesOnCancel: retryOnCancel ? -1 : 0,
                debugPipeReporter: new AsyncPipeReader.DebugReporter(debugMsg => debugMessages.Add(debugMsg), AsyncPipeReader.DebugReporter.VerbosityLevel.Info));
            reader.AllowCancelOverlapped = true;
            reader.BeginReadLine();

            // Create a big content so that reading doesn't finish synchronously, which in that case no overlapped is returned.
            string content = string.Join(" ", Enumerable.Range(1, 10_000).Select(i => $"{nameof(TestCancelAsync)}"));

            Task readTask = Task.Run(async () =>
            {
                await reader.WaitUntilEofAsync();
            });

            int writeCount = 20;
            bool anyWriteFailure = false;
            int cancelInjectCount = 0;

            Task writeTask = Task.Run(async () =>
            {
                for (int i = 0; i < writeCount; ++i)
                {
                    if (i >= 3 && cancelInjectCount < 5 && i % 3 == 0)
                    {
                        ++cancelInjectCount;
                        reader.InjectCancellation();
                    }

                    if (!TryWrite(writeHandle, $"{content}{i}\r\n", out int errorCode))
                    {
                        anyWriteFailure = true;
                        XAssert.IsFalse(retryOnCancel);
                        XAssert.AreEqual(NativeIOConstants.ErrorNoData, errorCode);
                        break;
                    }

                    await Task.Delay(10);
                }

                writeHandle.Dispose();
            });

            await Task.WhenAll(readTask, writeTask);

            XAssert.AreEqual(retryOnCancel, !anyWriteFailure);

            if (!retryOnCancel)
            {
                XAssert.IsTrue(messages.Count < writeCount);

                // 2 messages, one for INFO and the other for error.
                XAssert.AreEqual(2, debugMessages.Count, string.Join(Environment.NewLine, debugMessages));
                XAssert.IsTrue(debugMessages[0].Contains($"error code: {NativeIOConstants.ErrorOperationAborted}"));
                XAssert.IsTrue(debugMessages[1].Contains("IOCompletionPort.GetQueuedCompletionStatus failed"));
            }
            else
            {
                XAssert.AreEqual(writeCount + 1, messages.Count);

                // All but last show operation is aborted.

                for (int i = 0; i < debugMessages.Count - 1; ++i)
                {
                    XAssert.IsTrue(debugMessages[i].Contains($"error code: {NativeIOConstants.ErrorOperationAborted}"));
                }

                XAssert.IsTrue(
                    debugMessages.Last().Contains($"error code: {NativeIOConstants.ErrorBrokenPipe}")
                    || debugMessages.Last().Contains($"error code: {NativeIOConstants.ErrorHandleEof}"));
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public async Task TestStressCancelAsync()
        {
            for (int i = 0; i < 5; i++)
            {
                await TestCancelByTimeAsync();
            }
        }

        private async Task TestCancelByTimeAsync()
        {
            Pipes.CreateInheritablePipe(
                Pipes.PipeInheritance.InheritWrite,
                Pipes.PipeFlags.ReadSideAsync,
                readHandle: out SafeFileHandle readHandle,
                writeHandle: out SafeFileHandle writeHandle);

            IAsyncFile readFile = AsyncFileFactory.CreateAsyncFile(
                readHandle,
                FileDesiredAccess.GenericRead,
                ownsHandle: true,
                kind: FileKind.Pipe);

            var debugMessages = new List<string>();
            var messages = new List<string>();

            using var reader = new AsyncPipeReader(
                readFile,
                msg =>
                {
                    messages.Add(msg);
                    return true;
                },
                Encoding.Unicode,
                SandboxedProcessInfo.BufferSize,
                numOfRetriesOnCancel: -1,
                debugPipeReporter: new AsyncPipeReader.DebugReporter(debugMsg => debugMessages.Add(debugMsg), AsyncPipeReader.DebugReporter.VerbosityLevel.Info));
            reader.AllowCancelOverlapped = true;
            reader.BeginReadLine();

            // Create a big content so that reading doesn't finish synchronously, which in that case no overlapped is returned.
            string content = string.Join(" ", Enumerable.Range(1, 10_000).Select(i => $"{nameof(TestCancelByTimeAsync)}"));

            Task readTask = Task.Run(async () =>
            {
                await reader.WaitUntilEofAsync();
            });

            int writeCount = 200;
            Task writeTask = Task.Run(async () =>
            {
                for (int i = 0; i < writeCount; ++i)
                {
                    if (!TryWrite(writeHandle, $"{content}{i}\r\n", out int errorCode))
                    {
                        XAssert.Fail(errorCode.ToString());
                    }

                    await Task.Delay(10);
                }

                writeHandle.Dispose();
            });

            Task cancelTask = Task.Run(async () =>
            {
                var rnd = new Random();

                for (int i = 0; i < 5; ++i)
                {
                    await Task.Delay(rnd.Next(50, 350));
                    reader.InjectCancellation();
                }
            });

            await Task.WhenAll(readTask, writeTask, cancelTask);

            // Ensure that all written message are received.
            XAssert.AreEqual(writeCount + 1, messages.Count);

            // Ensure that pipe reading is completed.
            XAssert.IsTrue(
                debugMessages.Last().Contains($"error code: {NativeIOConstants.ErrorBrokenPipe}")
                || debugMessages.Last().Contains($"error code: {NativeIOConstants.ErrorHandleEof}"));
        }

        private static bool TryWrite(SafeFileHandle handle, string content, out int error)
        {
            byte[] byteContent = Encoding.Unicode.GetBytes(content);
            return FileUtilities.TryWriteFileSync(handle, byteContent, out error);
        }
    }
}
