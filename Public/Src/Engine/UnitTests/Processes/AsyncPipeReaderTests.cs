// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Native.Streams;
using BuildXL.Processes;
using BuildXL.Processes.Internal;
using BuildXL.Storage;
using Microsoft.Win32.SafeHandles;
using Test.BuildXL.TestUtilities.Xunit;
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
                new AsyncPipeReader.DebugReporter(debugMsg => debugMessages.Add(debugMsg), AsyncPipeReader.DebugReporter.VerbosityLevel.Info));
            reader.BeginReadLine();

            const string Content = nameof(TestReadAsync);
            Task readTask = Task.Run(async () =>
            {
                await reader.WaitUntilEofAsync();
            });

            Task writeTask = Task.Run(() =>
            {
                XAssert.IsTrue(Write(writeHandle, Content, out int _));
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
                debugMessages[0].Contains($"Error = {NativeIOConstants.ErrorBrokenPipe},") 
                || debugMessages[0].Contains($"Error = {NativeIOConstants.ErrorHandleEof},"));
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
                new AsyncPipeReader.DebugReporter(debugMsg => debugMessages.Add(debugMsg), AsyncPipeReader.DebugReporter.VerbosityLevel.Info));
            reader.BeginReadLine();
            readFile.Dispose();

            const string Content = nameof(TestDisconnectedRead);
            XAssert.IsFalse(Write(writeHandle, Content, out int errorCode));
            XAssert.AreEqual(NativeIOConstants.ErrorNoData, errorCode);
            writeHandle.Dispose();
        }

        private static bool Write(SafeFileHandle handle, string content, out int error)
        {
            byte[] byteContent = Encoding.Unicode.GetBytes(content);
            return FileUtilities.TryWriteFileSync(handle, byteContent, out error);
        }
    }
}
