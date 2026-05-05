// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Native.Streams;
using BuildXL.Processes;
using BuildXL.Processes.Internal;
using Microsoft.Win32.SafeHandles;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Processes
{
    /// <summary>
    /// Tests for <see cref="IAsyncPipeReader.TryDisconnect"/> across the three pipe-reader implementations.
    ///
    /// These exercise the unblock mechanism that <see cref="ProcessTreeContext.StopAsync"/> relies on when
    /// a non-self process holds a writer-end handle to the injector pipe past parent exit. The
    /// 'breakaway-grandchild' and 'inject-handle' production scenarios both reduce, at the kernel level,
    /// to the same situation: the server's read side cannot reach EOF because at least one external
    /// writer-end handle remains open. We simulate that by simply not closing our local writer handle and
    /// verifying that <see cref="NamedPipeServerStream.Disconnect"/> (via TryDisconnect) deterministically
    /// EOFs the reader.
    /// </summary>
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true)]
    public sealed class PipeReaderDisconnectTests : XunitBuildXLTest
    {
        private static readonly TimeSpan StallProbeDelay = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan AssertCompletionTimeout = TimeSpan.FromSeconds(15);

        public PipeReaderDisconnectTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public Task PipelineReaderTryDisconnectUnblocksHeldWriter()
        {
            return RunHeldWriterTestAsync(PipeReaderFactory.Kind.Pipeline);
        }

        [Fact]
        public Task StreamReaderTryDisconnectUnblocksHeldWriter()
        {
            return RunHeldWriterTestAsync(PipeReaderFactory.Kind.Stream);
        }

        /// <summary>
        /// Simulates the production stall scenario: a writer-end handle is held by an external party (here,
        /// just our local test) past the point where StopAsync would normally reach EOF. Without
        /// TryDisconnect, the reader's CompletionAsync stalls indefinitely. With TryDisconnect, it completes.
        /// </summary>
        private static async Task RunHeldWriterTestAsync(PipeReaderFactory.Kind kind)
        {
            var pipeStream = Pipes.CreateNamedPipeServerStream(
                PipeDirection.In,
                PipeOptions.Asynchronous,
                PipeOptions.None,
                out SafeFileHandle clientHandle);

            try
            {
                using var reader = PipeReaderFactory.CreateManagedPipeReader(
                    pipeStream,
                    callback: data => true,
                    encoding: Encoding.Unicode,
                    bufferSize: SandboxedProcessInfo.BufferSize,
                    overrideKind: kind);

                reader.BeginReadLine();

                Task completion = reader.CompletionAsync(true);

                // Hold the client (writer-end) handle open. EOF on the read side requires every writer-end
                // handle in the kernel pipe object to be released, so completion should NOT have completed.
                await Task.Delay(StallProbeDelay);
                XAssert.IsFalse(
                    completion.IsCompleted,
                    "Reader completion should still be pending while a writer-end handle is held open. " +
                    "Status: " + completion.Status);

                // Forcibly disconnect the server end. This must EOF the reader regardless of remaining
                // writer handles in the kernel.
                XAssert.IsTrue(reader.TryDisconnect(), "Managed pipe readers should report TryDisconnect=true on success.");

                Task winner = await Task.WhenAny(completion, Task.Delay(AssertCompletionTimeout));
                XAssert.AreSame(
                    completion,
                    winner,
                    "Reader completion should drain promptly after TryDisconnect.");

                // Observe any reader-side exception. Disconnect can throw IOException out of ReadAsync;
                // both 'completed normally' and 'completed with reader-side IO exception' are acceptable
                // outcomes as long as the task is no longer pending.
                try
                {
                    await completion;
                }
#pragma warning disable ERP022 // Disconnect-side faults on the reader path are expected and observed here.
                catch (Exception)
                {
                    // Intentional: see comment above.
                }
#pragma warning restore ERP022
            }
            finally
            {
                clientHandle.Dispose();
                pipeStream.Dispose();
            }
        }

        [Fact]
        public Task PipelineReaderNormalEofDoesNotRequireDisconnect()
        {
            return RunNormalEofTestAsync(PipeReaderFactory.Kind.Pipeline);
        }

        [Fact]
        public Task StreamReaderNormalEofDoesNotRequireDisconnect()
        {
            return RunNormalEofTestAsync(PipeReaderFactory.Kind.Stream);
        }

        /// <summary>
        /// Happy path: when the writer side closes naturally, the reader reaches EOF on its own. Asserts
        /// that the new TryDisconnect surface doesn't change normal-exit timing — the disconnect path
        /// never fires when there are no surviving writer handles.
        /// </summary>
        private static async Task RunNormalEofTestAsync(PipeReaderFactory.Kind kind)
        {
            var pipeStream = Pipes.CreateNamedPipeServerStream(
                PipeDirection.In,
                PipeOptions.Asynchronous,
                PipeOptions.None,
                out SafeFileHandle clientHandle);

            try
            {
                using var reader = PipeReaderFactory.CreateManagedPipeReader(
                    pipeStream,
                    callback: data => true,
                    encoding: Encoding.Unicode,
                    bufferSize: SandboxedProcessInfo.BufferSize,
                    overrideKind: kind);

                reader.BeginReadLine();

                // Close the client (writer-end) handle: this is the normal-exit equivalent.
                clientHandle.Dispose();

                Task completion = reader.CompletionAsync(true);
                Task winner = await Task.WhenAny(completion, Task.Delay(AssertCompletionTimeout));
                XAssert.AreSame(completion, winner, "Reader should EOF promptly when writer handle is closed.");
                await completion;
            }
            finally
            {
                if (!clientHandle.IsClosed)
                {
                    clientHandle.Dispose();
                }
                pipeStream.Dispose();
            }
        }

        [Fact]
        public Task PipelineReaderTryDisconnectAfterNaturalEofIsIdempotent()
        {
            return RunIdempotentDisconnectTestAsync(PipeReaderFactory.Kind.Pipeline);
        }

        [Fact]
        public Task StreamReaderTryDisconnectAfterNaturalEofIsIdempotent()
        {
            return RunIdempotentDisconnectTestAsync(PipeReaderFactory.Kind.Stream);
        }

        /// <summary>
        /// TryDisconnect after a natural EOF must not throw; the secondary-fix path
        /// (DetouredProcess.Kill → ProcessTreeContext.OnKilled) can race with normal teardown.
        /// </summary>
        private static async Task RunIdempotentDisconnectTestAsync(PipeReaderFactory.Kind kind)
        {
            var pipeStream = Pipes.CreateNamedPipeServerStream(
                PipeDirection.In,
                PipeOptions.Asynchronous,
                PipeOptions.None,
                out SafeFileHandle clientHandle);

            try
            {
                using var reader = PipeReaderFactory.CreateManagedPipeReader(
                    pipeStream,
                    callback: data => true,
                    encoding: Encoding.Unicode,
                    bufferSize: SandboxedProcessInfo.BufferSize,
                    overrideKind: kind);

                reader.BeginReadLine();

                clientHandle.Dispose();
                await reader.CompletionAsync(true);

                // Calling TryDisconnect after natural EOF must not throw — DetouredProcess.Kill's
                // secondary fix path can race with normal teardown. The exact bool return depends on
                // internal state: PipelineAsyncPipeReader's PipeReader.CompleteAsync disposes the
                // underlying NamedPipeServerStream (so Disconnect observes ObjectDisposedException and
                // returns false), while StreamAsyncPipeReader leaves it alive (so Disconnect succeeds
                // and returns true on the first call, then false on the second once already
                // disconnected). What matters here is the no-throw guarantee, not the bool value.
                reader.TryDisconnect();
                reader.TryDisconnect();
            }
            finally
            {
                if (!clientHandle.IsClosed)
                {
                    clientHandle.Dispose();
                }
                pipeStream.Dispose();
            }
        }

        [Fact]
        public void LegacyAsyncPipeReaderTryDisconnectReturnsFalse()
        {
            // The legacy reader is backed by an anonymous pipe (IAsyncFile), which has no equivalent of
            // NamedPipeServerStream.Disconnect. TryDisconnect must always report false so callers can
            // fall back to disposal/cancelation.
            Pipes.CreateInheritablePipe(
                Pipes.PipeInheritance.InheritWrite,
                Pipes.PipeFlags.ReadSideAsync,
                readHandle: out SafeFileHandle readHandle,
                writeHandle: out SafeFileHandle writeHandle);

            try
            {
                IAsyncFile readFile = AsyncFileFactory.CreateAsyncFile(
                    readHandle,
                    FileDesiredAccess.GenericRead,
                    ownsHandle: true,
                    kind: FileKind.Pipe);

                using var reader = new AsyncPipeReader(
                    readFile,
                    callback: msg => true,
                    encoding: Encoding.Unicode,
                    bufferSize: SandboxedProcessInfo.BufferSize);

                XAssert.IsFalse(
                    reader.TryDisconnect(),
                    "Legacy AsyncPipeReader has no Disconnect equivalent; TryDisconnect must return false.");
            }
            finally
            {
                writeHandle.Dispose();
            }
        }
    }
}
