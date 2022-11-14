// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Interop;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Class encapsulating a sandboxed process that should run remotely.
    /// </summary>
    /// <remarks>
    /// This sandboxed process adds process remoting capability for BuildXL via AnyBuild's shim server.
    /// The remoting mechanism leverages the SandboxedProcessExecutor used for executing processes in VM. That is, instead of executing
    /// the process directly, AnyBuild will be instructed to execute SandboxedProcessExecutor given the process' SandboxedProcessInfo.
    /// </remarks>
    public class RemoteSandboxedProcess : ExternalSandboxedProcess
    {
        private const string RemoteSandboxedProcessDataFileName = "RemoteData";

        // 1: initial
        // 2: fix engine path
        private const int Version = 2;

        private IRemoteProcessPip m_remoteProcess;
        private readonly RemoteData m_remoteData;
        private readonly IRemoteProcessManager m_remoteProcessManager;
        private readonly CancellationTokenSource m_killProcessCts = new ();
        private readonly CancellationTokenSource m_combinedCts;
        private readonly CancellationToken m_cancellationToken;
        private readonly ExternalToolSandboxedProcessExecutor m_tool;

        private bool IsCompletedSuccessfully => m_remoteProcess != null && m_remoteProcess.Completion.IsCompleted && m_remoteProcess.Completion.Status == TaskStatus.RanToCompletion;

        /// <summary>
        /// Checks if process should be run locally.
        /// </summary>
        public bool? ShouldRunLocally => IsCompletedSuccessfully && m_remoteProcess.Completion.Result.ShouldRunLocally;

        /// <inheritdoc />
        public override int ProcessId => 0;

        /// <inheritdoc />
        public override string StdOut => IsCompletedSuccessfully ? (m_remoteProcess.Completion.Result.StdOut ?? string.Empty) : string.Empty;

        /// <inheritdoc />
        public override string StdErr => IsCompletedSuccessfully ? (m_remoteProcess.Completion.Result.StdErr ?? string.Empty) : string.Empty;

        /// <inheritdoc />
        public override int? ExitCode => IsCompletedSuccessfully ? m_remoteProcess.Completion.Result.ExitCode : default;

        private string RemoteSandboxedProcessDataFile => Path.Combine(WorkingDirectory, RemoteSandboxedProcessDataFileName);

        /// <summary>
        /// Creates an instance of <see cref="RemoteSandboxedProcess"/>.
        /// </summary>
        public RemoteSandboxedProcess(
            SandboxedProcessInfo sandboxedProcessInfo,
            RemoteData remoteData,
            IRemoteProcessManager remoteProcessManager,
            ExternalToolSandboxedProcessExecutor tool,
            string externalSandboxedProcessDirectory,
            CancellationToken cancellationToken)
            : base(sandboxedProcessInfo, Path.Combine(externalSandboxedProcessDirectory, nameof(ExternalToolSandboxedProcess)))
        {
            Contract.Requires(tool != null);
            Contract.Requires(remoteProcessManager.IsInitialized);

            m_remoteData = remoteData;
            m_tool = tool;
            m_remoteProcessManager = remoteProcessManager;
            m_cancellationToken = cancellationToken;
            m_combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_killProcessCts.Token);
        }

        /// <inheritdoc />
        public override void Start()
        {
            base.Start();

            SerializeSandboxedProcessInputFile(SandboxedProcessInfoFile, SandboxedProcessInfo.Serialize);
            SerializeSandboxedProcessInputFile(RemoteSandboxedProcessDataFile, m_remoteData.Serialize);

            var remoteProcessInfo = new RemoteProcessInfo(
                m_tool.ExecutablePath,
                m_tool.CreateArguments(
                    SandboxedProcessInfoFile,
                    SandboxedProcessResultsFile,
                    remoteSandboxedProcessDataFile: RemoteSandboxedProcessDataFile,
                    remoteArgSalt: GetSalt()),
                SandboxedProcessInfo.WorkingDirectory,
                SandboxedProcessInfo.EnvironmentVariables.ToDictionary());

            m_remoteProcess = m_remoteProcessManager.CreateAndStartAsync(remoteProcessInfo, m_combinedCts.Token).GetAwaiter().GetResult();
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            m_remoteProcess?.Dispose();
            m_combinedCts.Dispose();
            m_killProcessCts.Dispose();

            CleanUpWorkingDirectory();
        }

        /// <inheritdoc />
        public override ProcessMemoryCountersSnapshot? GetMemoryCountersSnapshot() => default;

        /// <inheritdoc />
        public override async Task<SandboxedProcessResult> GetResultAsync()
        {
            Contract.Assert(m_remoteProcess != null);

            await m_remoteProcess.Completion;

            if (IsCancellationRequested
                || !IsCompletedSuccessfully
                || !ExitCode.HasValue
                || ExitCode.Value != 0)
            {
                return CreateResultForFailure();
            }

            return DeserializeSandboxedProcessResultFromFile();
        }

        /// <inheritdoc />
        public override async Task KillAsync()
        {
            m_killProcessCts.Cancel();

            if (m_remoteProcess != null)
            {
                await m_remoteProcess.Completion;
            }
        }

        private bool IsCancellationRequested => m_cancellationToken.IsCancellationRequested || m_killProcessCts.IsCancellationRequested;

        /// <inheritdoc />
        public override EmptyWorkingSetResult TryEmptyWorkingSet(bool isSuspend) => EmptyWorkingSetResult.None;

        /// <inheritdoc />
        public override bool TryResumeProcess() => false;

        private SandboxedProcessResult CreateResultForFailure()
        {
            string hint = Path.GetFileNameWithoutExtension(m_tool.ExecutablePath);

            return CreateResultForFailure(
                exitCode: IsCancellationRequested
                    ? ExitCodes.Killed
                    : (IsCompletedSuccessfully ? (ExitCode ?? -1) : -1),
                killed: IsCancellationRequested,
                timedOut: false,
                output: StdOut,
                error: StdErr,
                hint: hint);
        }

        private static string GetSalt() =>
            Version.ToString() +
            (string.IsNullOrEmpty(EngineEnvironmentSettings.ProcessRemotingSalt.Value) 
                ? string.Empty 
                : "__" +
                  (string.Equals(EngineEnvironmentSettings.ProcessRemotingSalt.Value, "*", StringComparison.Ordinal) 
                    ? Guid.NewGuid().ToString()
                    : EngineEnvironmentSettings.ProcessRemotingSalt.Value));
    }
}
