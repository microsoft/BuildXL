// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.AdoBuildRunner;

namespace Test.Tool.AdoBuildRunner
{
    public class MockLauncher : IBuildXLLauncher
    {
        /// <nodoc />
        public MockLauncher(int returnCode = 0)
        {
            ReturnCode = returnCode;
        }

        /// <summary>
        /// Set this task from a test if you want the completion to be delayed or controlled by a task completion source, etc.
        /// </summary>
        public Task CompletionTask = Task.CompletedTask;

        /// <summary>
        /// Value to return for the mocked BuildXL invocation
        /// </summary>
        public int ReturnCode { get; set; }

        /// <nodoc />
        public bool Launched { get; private set; }

        /// <nodoc />
        public bool Exited { get; private set; }

        /// <summary>
        /// True if the launch ended because the orchestrator-termination pipe was signaled
        /// (and <see cref="ReactToOrchestratorTerminationPipe"/> was enabled).
        /// </summary>
        public bool PipeSignaled { get; private set; }

        /// <nodoc />
        public string Arguments { get; private set; } = "";

        /// <summary>
        /// When true, the mock reads the orchestrator-termination pipe handle from the
        /// <c>BUILDXL_ORCH_TERMINATION_PIPE_HANDLE</c> entry of the extra environment and races
        /// BuildXL's completion against reading from that pipe. This simulates BuildXL's in-process
        /// watcher reacting to the runner's signal.
        /// </summary>
        public bool ReactToOrchestratorTerminationPipe { get; set; }

        /// <inheritdoc />
        async Task<int> IBuildXLLauncher.LaunchAsync(string args, Action<string> outputDataReceived, Action<string> errorDataRecieved, IReadOnlyDictionary<string, string>? extraEnvironment)
        {
            Launched = true;
            Arguments = args;

            AnonymousPipeClientStream? pipe = null;
            var pipeReaderCts = new CancellationTokenSource();
            var pipeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (ReactToOrchestratorTerminationPipe
                && extraEnvironment != null
                && extraEnvironment.TryGetValue(Constants.OrchestratorTerminationPipeEnvVar, out var pipeHandle)
                && !string.IsNullOrEmpty(pipeHandle))
            {
                try
                {
                    // Open the inherited pipe handle exactly the way production BuildXL does
                    // (WorkerService.StartOrchestratorTerminationWatcher): via the handle-STRING constructor.
                    // This is the only client-open shape that works on Linux -- wrapping the raw handle in a
                    // SafePipeHandle(ownsHandle:false) leaves the FD unregistered for reads, so ReadAsync never
                    // observes the byte and the test hangs until the 10-min pip timeout.
                    // CODESYNC: Public/Src/Engine/Dll/Distribution/WorkerService.cs (StartOrchestratorTerminationWatcher).
                    pipe = new AnonymousPipeClientStream(PipeDirection.In, pipeHandle);
                    var pipeLocal = pipe;
                    var token = pipeReaderCts.Token;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var buffer = new byte[1];
                            // Mirror production (WorkerService): only an explicit signal byte (read > 0) counts
                            // as "runner signaled the orchestrator is gone". EOF (read == 0) means the runner
                            // closed the pipe without signaling -- the orchestrator outcome is unknown, so it is
                            // NOT treated as a termination signal.
                            var read = await pipeLocal.ReadAsync(buffer.AsMemory(0, 1), token);
                            pipeTcs.TrySetResult(read > 0);
                        }
#pragma warning disable ERP022 // Defensive: a failed read (including cancellation) resolves the TCS, not propagate.
                        catch
                        {
                            pipeTcs.TrySetResult(false);
                        }
#pragma warning restore ERP022
                    });
                }
#pragma warning disable ERP022 // Defensive: a failed Open is logged by the test surface elsewhere.
                catch
                {
                }
#pragma warning restore ERP022
            }

            var winner = await Task.WhenAny(CompletionTask, pipeTcs.Task);

            Exited = true;

            // PipeSignaled is true only when the reader observed an explicit signal byte (pipeTcs == true).
            // An EOF resolves pipeTcs to false and must NOT count as a signal.
            if (winner == pipeTcs.Task)
            {
                PipeSignaled = await pipeTcs.Task;
            }

            // We intentionally do NOT await the background reader. When the runner never signals (e.g. the
            // worker's BuildXL exits on its own), the reader stays parked in ReadAsync until the server closes
            // the write end, which WorkerBuildExecutor does in its `using` block AFTER this LaunchAsync returns --
            // so awaiting here would deadlock. Letting the reader resolve in the background is safe: it owns no
            // resources we need to reclaim, and pipeTcs has already been observed above.
            await pipeReaderCts.CancelAsync();
            pipe?.Dispose();
            return ReturnCode;
        }
    }
}
