// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;

#nullable enable

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Virtual factory interface for creating an instance of <see cref="IRemoteProcess"/>.
    /// </summary>
    public interface IRemoteProcessFactory
    {
        /// <summary>
        /// Creates an instance of <see cref="IRemoteProcess"/>, starting the remote execution without waiting for process completion.
        /// </summary>
        /// <param name="remoteProcessInfo">Process info.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An instance of <see cref="IRemoteProcess"/> that can be used to await process completion or cancel.</returns>
        Task<IRemoteProcess> CreateAndStartAsync(RemoteCommandExecutionInfo remoteProcessInfo, CancellationToken cancellationToken);
    }

    /// <summary>
    /// The results of a completed <see cref="IRemoteProcess"/>.
    /// </summary>
    public interface IRemoteProcessResult
    {
        /// <summary>
        /// Whether the process should be run locally because of some failure remoting.
        /// </summary>
        public bool ShouldRunLocally { get; }

        /// <summary>
        /// Gets the process exit code, or null if the process was not remotable.
        /// </summary>
        int? ExitCode { get; }

        /// <summary>
        /// Gets the stdout contents of the process, or null if the process was not remotable.
        /// </summary>
        string? StdOut { get; }

        /// <summary>
        /// Gets the stderr contents of the process, or null if the process was not remotable.
        /// </summary>
        string? StdErr { get; }

        /// <summary>
        /// Gets whether the process was a cache hit or was remoted.
        /// </summary>
        CommandExecutionDisposition Disposition { get; }
    }

    /// <summary>
    /// Concrete implementation of <see cref="IRemoteProcessResult"/>.
    /// </summary>
    internal sealed class RemoteProcessResult : IRemoteProcessResult
    {
        /// <inheritdoc/>
        public bool ShouldRunLocally { get; private set; }

        /// <inheritdoc/>
        public int? ExitCode { get; private set; }

        /// <inheritdoc/>
        public string? StdOut { get; private set; }

        /// <inheritdoc/>
        public string? StdErr { get; private set; }

        /// <inheritdoc/>
        public CommandExecutionDisposition Disposition { get; private set; }

        internal static RemoteProcessResult CreateForLocalRun() => new() { ShouldRunLocally = true };

        internal static RemoteProcessResult CreateFromCompletedProcess(int exitCode, string stdOut, string stdErr, CommandExecutionDisposition disposition) =>
            new() { ExitCode = exitCode, StdOut = stdOut, StdErr = stdErr, Disposition = disposition };
    }

    /// <summary>
    /// Runs a remote process via AnyBuild. Process file outputs are placed onto the local disk.
    /// </summary>
    public interface IRemoteProcess : IDisposable
    {
        /// <summary>
        /// Allows awaiting remote processing completion.
        /// </summary>
        /// <exception cref="TaskCanceledException">
        /// The caller-provided cancellation token was signaled or the object was disposed.
        /// </exception>
        Task<IRemoteProcessResult> Completion { get; }
    }

    /// <summary>
    /// Concrete virtual factory implementation for <see cref="IRemoteProcessFactory"/>.
    /// </summary>
    public sealed class RemoteProcessFactory : IRemoteProcessFactory
    {
        /// <summary>
        /// Singleton instance for the factory.
        /// </summary>
        public static RemoteProcessFactory Instance { get; } = new();

        /// <summary>
        /// Creates and starts an instance of <see cref="IRemoteProcess"/>.
        /// </summary>
        public Task<IRemoteProcess> CreateAndStartAsync(RemoteCommandExecutionInfo remoteProcessInfo, CancellationToken cancellationToken)
        {
            return CreateAndStartAsync(21337, remoteProcessInfo, cancellationToken);
        }

        /// <summary>
        /// Creates and starts an instance of <see cref="IRemoteProcess"/>.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Virtual factory pattern")]
        public async Task<IRemoteProcess> CreateAndStartAsync(int shimPort, RemoteCommandExecutionInfo remoteProcessInfo, CancellationToken cancellationToken, IShimClient? shimClient = null)
        {
            var cmd = new RemoteCommand(shimPort, remoteProcessInfo, cancellationToken, shimClient);
            await cmd.StartAsync();
            return cmd;
        }
    }

    /// <summary>
    /// Runs a remote command over the Shim execution protocol, storing the resulting stdout, stderr, and exit code.
    /// Command file outputs are placed into the local disk by the AnyBuild service process.
    /// </summary>
    /// <remarks>
    /// CODESYNC:
    /// - (AnyBuild) src/Client/AnyBuild/ShimServer.cs
    /// - (BuildXL) src/Engine/Processes/Remoting/RemotingSandboxedProcess.cs
    /// </remarks>
    public sealed class RemoteCommand : IRemoteProcess
    {
        private readonly IShimClient m_client;
        private readonly RemoteCommandExecutionInfo m_commandInfo;
        private Task<IRemoteProcessResult>? m_processingTask;
        private readonly StringBuilder m_stdout = new (128);
        private readonly StringBuilder m_stderr = new ();
        private readonly CancellationTokenSource m_cancelProcessCts = new ();
        private readonly CancellationTokenSource m_combinedCts;

        /// <summary>
        /// Creates an instance of <see cref="RemoteCommand"/>.
        /// </summary>
        /// <param name="port">The TCP port of the localhost AnyBuild service process.</param>
        /// <param name="command">Parameters for process execution.</param>
        /// <param name="cancellationToken">A cancellation token for the command execution.</param>
        /// <param name="shimClient">A Shim client, for unit testing. By default a new <see cref="ShimClient"/> is used. This client will be disposed on dispose of this object.</param>
        internal RemoteCommand(
            int port,
            RemoteCommandExecutionInfo command,
            CancellationToken cancellationToken,
            IShimClient? shimClient = null)
        {
            m_combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_cancelProcessCts.Token);
            m_client = shimClient ?? new ShimClient(port, m_combinedCts.Token);
            m_commandInfo = command;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            CancelAsync().GetAwaiter().GetResult();
            m_client.Dispose();
            m_combinedCts.Dispose();
            m_cancelProcessCts.Dispose();
        }

        /// <summary>
        /// Allows for awaiting remote process completion.
        /// </summary>
        public Task<IRemoteProcessResult> Completion => m_processingTask!;

        /// <summary>
        /// Starts the remote process without waiting for process completion.
        /// Should only be called from <see cref="RemoteProcessFactory"/>.
        /// </summary>
        internal async Task StartAsync()
        {
            long startTicks = DateTime.UtcNow.Ticks;

            long connectTicks = DateTime.UtcNow.Ticks;
            if (!m_client.IsConnected)
            {
                await m_client.ConnectAsync();
            }

            connectTicks = DateTime.UtcNow.Ticks - connectTicks;
            string requestString = CreateRequestString(startTicks, connectTicks);
            await m_client.WriteAsync(Protocol.RunProcessMessagePrefix, requestString);
            m_processingTask = Task.Run(() => ProcessResponseAsync(), m_combinedCts.Token);
        }

        /// <summary>
        /// Cancels remote process execution. This also causes <see cref="Completion"/> to complete.
        /// </summary>
        public async Task CancelAsync()
        {
            if (m_processingTask != null)
            {
                if (!m_processingTask.IsCompleted)
                {
                    m_cancelProcessCts.Cancel();
                }

                try
                {
                    await m_processingTask;
                }
#pragma warning disable ERP022
                catch
                {
                    // Eat shutdown exceptions.
                }
#pragma warning restore ERP022

                m_processingTask = null;
            }
        }

        private async Task<IRemoteProcessResult> ProcessResponseAsync()
        {
            bool done = false;

            while (!done && !m_combinedCts.IsCancellationRequested)
            {
                string response = await m_client.ReceiveStringAsync();

                if (string.IsNullOrEmpty(response))
                {
                    throw new InvalidDataException("Received null or empty response");
                }

                switch (response[0])
                {
                    case Protocol.RunBuildLocallyMessage:
                        return RemoteProcessResult.CreateForLocalRun();

                    case Protocol.ProcessCompleteMessagePrefix:
                        int semicolonIndex = response.IndexOf(';');
                        string exitCodeStr = response.Substring(1, semicolonIndex - 1);
                        int exitCode = int.Parse(exitCodeStr, CultureInfo.InvariantCulture);
                        CommandExecutionDisposition disposition = Protocol.Disposition.ToCommandExecutionDisposition(response[semicolonIndex + 1]);
                        return RemoteProcessResult.CreateFromCompletedProcess(exitCode, m_stdout.ToString(), m_stderr.ToString(), disposition);

                    case Protocol.StdoutMessagePrefix:
                    {
                        // Trim \0 at end 
                        string s = StripPrefixAndNullChar(response);
                        m_stdout.AppendLine(s);
                        break;
                    }

                    case Protocol.StderrMessagePrefix:
                    {
                        string s = StripPrefixAndNullChar(response);
                        m_stderr.AppendLine(s);
                        break;
                    }

                    default:
                        throw new InvalidDataException($"Unknown message protocol '{response[0]}'");
                }
            }

            // Should not be reachable but need safe return.
            return RemoteProcessResult.CreateForLocalRun();
        }

        private static string StripPrefixAndNullChar(string protocolString)
        {
            return protocolString.Substring(1, protocolString.Length - 2);
        }

        private string CreateRequestString(long startTicks, long connectTicks)
        {
            var builder = new StringBuilder(128);

            // We're not replacing an existing process with a shim so use a default value.
            const string ShimmedProcessId = "0";
            builder.AppendShimString(ShimmedProcessId);

            // Parent process ID (this process).
            builder.AppendShimString(Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));

            AppendCommandAndArgs(builder)
                .AppendShimString(m_commandInfo.WorkingDirectory)
                .AppendShimString(startTicks.ToString(CultureInfo.InvariantCulture));

            AppendEnv(builder)
                .AppendShimString(connectTicks.ToString(CultureInfo.InvariantCulture))
                .AppendShimString(DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));

            return builder.ToString();
        }

        private StringBuilder AppendCommandAndArgs(StringBuilder builder)
        {
            if (OperatingSystemHelper.IsLinuxOS)
            {
                builder
                    .AppendShimString(m_commandInfo.Command)
                    .AppendShimString(m_commandInfo.Args);
            }
            else
            {
                builder.Append(m_commandInfo.Command).Append(' ').AppendShimString(m_commandInfo.Args);
            }

            return builder;
        }

        private StringBuilder AppendEnv(StringBuilder builder)
        {
            foreach (KeyValuePair<string, string> kvp in m_commandInfo.Env)
            {
                builder.Append(kvp.Key).Append('=').AppendShimString(kvp.Value);
            }

            // Double null at end of list.
            builder.Append('\0');

            return builder;
        }
    }

    internal static class ShimStringBuilderExtensions
    {
        public static StringBuilder AppendShimString(this StringBuilder sb, string s)
        {
            sb.Append(s).Append('\0');
            return sb;
        }
    }
}
