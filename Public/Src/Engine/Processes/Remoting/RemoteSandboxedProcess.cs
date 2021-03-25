// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Interop;
using BuildXL.Utilities;

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Class encapsulating a sandboxed process that should run remotely.
    /// </summary>
    /// <remarks>
    /// This sandboxed process adds process remoting capability for BuildXL via AnyBuild's shim server.
    /// When set with /remoteAllProcesses, for each process, BuildXL will create a TCP client that 
    /// sends a build request to the AnyBuild's shim server. The remoting mechanism leverages the SandboxedProcessExecutor
    /// used for executing processes in VM. That is, instead of executing the process directly, AnyBuild's shim server
    /// will be instructed to execute SandboxedProcessExecutor given the process' SandboxedProcessInfo.
    /// </remarks>
    public class RemoteSandboxedProcess : ExternalSandboxedProcess
    {
        /// <summary>
        /// CODESYNC: AnyBuild src/Client/Shim.Shared/ShimConstants.cs
        /// </summary>
        private const string DefaultPortEnv = "__ANYBUILD_PORT";

        private readonly ExternalToolSandboxedProcessExecutor m_tool;

        private readonly StringBuilder m_output = new StringBuilder();
        private readonly StringBuilder m_error = new StringBuilder();

        private RemotingClient m_remotingClient;
        private readonly CancellationTokenSource m_cancellationTokenSource = new CancellationTokenSource();
        private readonly CancellationToken m_cancellationToken;

        private bool? m_shouldRunLocally = default;
        private int? m_exitCode = default;

        private Task m_processingTask;

        private bool IsProcessingCompleted => m_processingTask != null && m_processingTask.IsCompleted;

        /// <summary>
        /// Checks if process should be run locally.
        /// </summary>
        public bool? ShouldRunLocally => IsProcessingCompleted ? m_shouldRunLocally : default;

        /// <inheritdoc />
        public override int ProcessId => 0;

        /// <inheritdoc />
        public override string StdOut => IsProcessingCompleted ? m_output.ToString() : string.Empty;

        /// <inheritdoc />
        public override string StdErr => IsProcessingCompleted ? m_error.ToString() : string.Empty;

        /// <inheritdoc />
        public override int? ExitCode => IsProcessingCompleted ? m_exitCode : default;

        /// <summary>
        /// Creates an instance of <see cref="RemoteSandboxedProcess"/>.
        /// </summary>
        public RemoteSandboxedProcess(
            SandboxedProcessInfo sandboxedProcessInfo,
            ExternalToolSandboxedProcessExecutor tool,
            string externalSandboxedProcessDirectory,
            CancellationToken cancellationToken)
            : base(sandboxedProcessInfo, Path.Combine(externalSandboxedProcessDirectory, nameof(ExternalToolSandboxedProcess)))
        {
            Contract.Requires(tool != null);

            m_tool = tool;
            m_cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_cancellationTokenSource.Token).Token;
        }

        /// <inheritdoc />
        public override void Start()
        {
            long startTicks = DateTime.UtcNow.Ticks;
            base.Start();

            SerializeSandboxedProcessInputFile(SandboxedProcessInfoFile, SandboxedProcessInfo.Serialize);

            m_remotingClient = new RemotingClient(GetEndPoint(), m_cancellationToken);

            long connectTicks = DateTime.UtcNow.Ticks;
            m_remotingClient.ConnectAsync().GetAwaiter().GetResult();
            connectTicks = DateTime.UtcNow.Ticks - connectTicks;

            string requestString = CreateRequestString(startTicks, connectTicks);
            m_remotingClient.WriteAsync(Protocol.RunProcessMessagePrefix, requestString).GetAwaiter().GetResult();
            m_processingTask = Task.Run(() => ProcessResponseAsync(), m_cancellationToken);
        }

        private async Task ProcessResponseAsync()
        {
            bool done = false;

            while (!done && !m_cancellationToken.IsCancellationRequested)
            {
                string response = await m_remotingClient.ReceiveStringAsync();

                if (string.IsNullOrEmpty(response))
                {
                    throw new BuildXLException("Receiving null or empty response");
                }

                switch (response[0])
                {
                    case Protocol.RunBuildLocallyMessage:
                        m_shouldRunLocally = true;
                        done = true;
                        break;
                    case Protocol.ProcessCompleteMessagePrefix:
                        m_exitCode = int.Parse(response.Substring(1));
                        done = true;
                        break;
                    case Protocol.StdoutMessagePrefix:
                        m_output.AppendLine(response.Substring(1).Trim('\0'));
                        break;
                    case Protocol.StderrMessagePrefix:
                        m_error.AppendLine(response.Substring(1).Trim('\0'));
                        break;
                    default:
                        throw new BuildXLException($"Unknown message protocol '{response[0]}'");
                }
            }
        }

        private string CreateRequestString(long startTicks, long connectTicks)
        {
            var builder = new StringBuilder();
            AppendWithCommand(builder)
                .Append(CreateString(SandboxedProcessInfo.WorkingDirectory))
                .Append(CreateTicks(startTicks))
                .Append(CreateEnvString())
                .Append(CreateTicks(connectTicks))
                .Append(CreateTicks(DateTime.UtcNow.Ticks));

            return builder.ToString();
        }

        private StringBuilder AppendWithCommand(StringBuilder builder)
        {
            var executable = m_tool.ExecutablePath;
            var args = m_tool.CreateArguments(SandboxedProcessInfoFile, SandboxedProcessResultsFile);

            if (OperatingSystemHelper.IsLinuxOS)
            {
                builder
                    .Append(CreateString(executable))
                    .Append(CreateString(args));
            }
            else
            {
                builder.Append(CreateString(executable + " " + args));
            }

            return builder;
        }

        private string CreateEnvString()
        {
            return CreateString(string.Join(
                string.Empty,
                SandboxedProcessInfo.EnvironmentVariables.ToDictionary().Select(kvp => CreateString($"{kvp.Key}={kvp.Value}"))));
        }

        private static string CreateString(string s) => s + "\0";

        private static string CreateTicks(long ticks) => CreateString(ticks.ToString());

        private static IPEndPoint GetEndPoint()
        {
            string portString = Environment.GetEnvironmentVariable(DefaultPortEnv);

            if (string.IsNullOrEmpty(portString))
            {
                throw new BuildXLException("Unable to find server port for remoting process");
            }

            return new IPEndPoint(IPAddress.Loopback, int.Parse(portString));
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            if (m_remotingClient != null)
            {
                m_remotingClient.Dispose();
            }

            m_cancellationTokenSource.Dispose();
        }

        /// <inheritdoc />
        public override ProcessMemoryCountersSnapshot? GetMemoryCountersSnapshot() => default;

        /// <inheritdoc />
        public override async Task<SandboxedProcessResult> GetResultAsync()
        {
            await m_processingTask;

            if (m_cancellationToken.IsCancellationRequested
                || !IsProcessingCompleted
                || !ExitCode.HasValue
                || ExitCode.Value != 0)
            {
                return CreateResultForFailure();
            }

            return DeserializeSandboxedProcessResultFromFile();
        }

        /// <inheritdoc />
        public override Task KillAsync()
        {
            m_cancellationTokenSource.Cancel();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override EmptyWorkingSetResult TryEmptyWorkingSet(bool isSuspend) => EmptyWorkingSetResult.None;

        /// <inheritdoc />
        public override bool TryResumeProcess() => false;

        private SandboxedProcessResult CreateResultForFailure()
        {
            string output = m_output.ToString();
            string error = m_error.ToString();
            string hint = Path.GetFileNameWithoutExtension(m_tool.ExecutablePath);

            return CreateResultForFailure(
                exitCode: m_cancellationToken.IsCancellationRequested
                    ? ExitCodes.Killed
                    : (IsProcessingCompleted ? ExitCode.Value : -1),
                killed: m_cancellationToken.IsCancellationRequested,
                timedOut: false,
                output: output,
                error: error,
                hint: hint);
        }
    }
}
