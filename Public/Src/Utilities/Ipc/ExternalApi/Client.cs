// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi.Commands;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;

namespace BuildXL.Ipc.ExternalApi
{
    /// <summary>
    /// Provides a single entry for all operations that constitute BuildXL External API.
    /// </summary>
    public sealed class Client : IDisposable
    {
        private readonly IClient m_client;

        internal const string ErrorCannotParseIpcResultMessage = "Cannot parse IPC result";

        #region Constructors and Factory Methods

        /// <nodoc/>
        public Client(IClient client)
        {
            m_client = client;
        }

        /// <summary>
        /// Convenient factory method.
        /// </summary>
        public static Client Create(IIpcProvider provider, string connectionString, IClientConfig config = null)
        {
            return new Client(provider.GetClient(connectionString, config ?? new ClientConfig()));
        }
        #endregion

        /// <inheritdoc/>
        public void Dispose()
        {
            m_client.RequestStop();
            m_client.Completion.GetAwaiter().GetResult();
            m_client.Dispose();
        }

        #region API

        /// <summary>
        /// Ensures file is materialized on disk.
        /// </summary>
        public Task<Possible<bool>> MaterializeFile(FileArtifact file, string fullFilePath)
        {
            return ExecuteCommand(new MaterializeFileCommand(file, fullFilePath));
        }

        /// <summary>
        /// Reads the SHA-256 hashes from cache or materializes the files on disk and computes their hashes.
        /// Returns empty if BuildManifestEntry[] all the hashes could be generated/read from cache.
        /// Else returns a BuildManifestEntry[] of entries that couldn't be hashed.
        /// </summary>
        public Task<Possible<BuildManifestEntry[]>> RegisterFilesForBuildManifest(
            string dropName,
            BuildManifestEntry[] buildManifestEntries)
        {
            return ExecuteCommand(new RegisterFilesForBuildManifestCommand(dropName, buildManifestEntries));
        }

        /// <summary>
        /// Generates a list of file hashes for BuildManifest stored by <see cref="RegisterFilesForBuildManifest"/>.
        /// </summary>
        public Task<Possible<List<BuildManifestFileInfo>>> GenerateBuildManifestFileList(string dropName)
        {
            return ExecuteCommand(new GenerateBuildManifestFileListCommand(dropName));
        }

        /// <summary>
        /// Compute the content hash with different hashing algorithm for a file
        /// </summary>
        public Task<Possible<RecomputeContentHashEntry>> RecomputeContentHashFiles(FileArtifact file, string requestedhashType, RecomputeContentHashEntry entry)
        {
            return ExecuteCommand(new RecomputeContentHashCommand(file, requestedhashType, entry));
        }

        /// <summary>
        /// Log a verbose or warning message on BuildXL side
        /// </summary>
        public Task<Possible<bool>> LogMessage(string message, bool isWarning = false)
        {
            return ExecuteCommand(new LogMessageCommand(message, isWarning));
        }

        /// <summary>
        /// Arbitrary statistics that BuildXL should report (in its .stats file).
        /// </summary>
        public Task<Possible<bool>> ReportStatistics(IDictionary<string, long> stats)
        {
            return ExecuteCommand(new ReportStatisticsCommand(stats));
        }

        /// <summary>
        /// Arbitrary info and stats that BuildXL should publish into telemetry.
        /// </summary>
        public Task<Possible<bool>> ReportDaemonTelemetry(string daemonName, string daemonTelemetryPayload, string daemonInfoPayload)
        {
            return ExecuteCommand(new ReportDaemonTelemetryCommand(daemonName, daemonTelemetryPayload, daemonInfoPayload));
        }

        /// <summary>
        /// Lists the content of a sealed directory
        /// </summary>
        public Task<Possible<List<SealedDirectoryFile>>> GetSealedDirectoryContent(DirectoryArtifact directory, string fullDirectoryPath)
        {
            return ExecuteCommand(new GetSealedDirectoryContentCommand(directory, fullDirectoryPath));
        }

        /// <summary>
        /// Notifies the engine that the service pip has started successfully and is listening to the assigned port.
        /// </summary>
        public Task<Possible<bool>> ReportServicePipIsReady(int processId, string processName)
        {
            return ExecuteCommand(new ReportServicePipIsReadyCommand(processId, processName));
        }

        #endregion

        private async Task<Possible<T>> ExecuteCommand<T>(Command<T> command)
        {
            try
            {
                IIpcOperation ipcOperation = new IpcOperation(Command.Serialize(command), waitForServerAck: true);
                IIpcResult ipcResult = await m_client.Send(ipcOperation);
                if (!ipcResult.Succeeded)
                {
                    return new Failure<string>(ipcResult.ToString());
                }

                T cmdResult;
                if (!command.TryParseResult(ipcResult.Payload, out cmdResult))
                {
                    return new Failure<string>($"{ErrorCannotParseIpcResultMessage}: {ipcResult}");
                }

                return cmdResult;
            }
            catch (Exception ex)
            {
                return new Failure<Exception>(ex).Annotate($"Executing {command.GetType().Name} command threw exception.");
            }
        }
    }
}
