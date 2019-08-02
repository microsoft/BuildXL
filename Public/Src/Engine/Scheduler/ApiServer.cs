// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi.Commands;
using BuildXL.Ipc.Interfaces;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// IPC server providing an implementation for the BuildXL External API <see cref="BuildXL.Ipc.ExternalApi.Client"/>.
    /// </summary>
    public sealed class ApiServer : IIpcOperationExecutor, IDisposable
    {
        private readonly FileContentManager m_fileContentManager;
        private readonly IServer m_server;
        private readonly PipExecutionContext m_context;

        private long m_numMaterializeFile = 0;
        private long m_numReportStatistics = 0;
        private long m_numGetSealedDirectoryContent = 0;

        private LoggingContext m_loggingContext;

        /// <nodoc />
        public ApiServer(
            IIpcProvider ipcProvider,
            string ipcMonikerId,
            FileContentManager fileContentManager,
            PipExecutionContext context,
            IServerConfig config)
        {
            Contract.Requires(ipcMonikerId != null);
            Contract.Requires(fileContentManager != null);
            Contract.Requires(context != null);
            Contract.Requires(config != null);

            m_fileContentManager = fileContentManager;
            m_server = ipcProvider.GetServer(ipcProvider.LoadAndRenderMoniker(ipcMonikerId), config);
            m_context = context;
        }

        /// <summary>
        /// Starts the server. <seealso cref="IServer.Start"/>
        /// </summary>
        public void Start(LoggingContext loggingContext)
        {
            Contract.Requires(loggingContext != null);

            m_loggingContext = loggingContext;
            m_server.Start(this);
        }

        /// <summary>
        /// Stops the server and waits until it is stopped.
        /// <seealso cref="IStoppable.RequestStop"/>, <seealso cref="IStoppable.Completion"/>.
        /// </summary>
        public Task Stop()
        {
            m_server.RequestStop();
            return m_server.Completion;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_server.Dispose();
        }

        /// <summary>
        /// Logs statistics.
        /// </summary>
        public void LogStats(LoggingContext loggingContext)
        {
            Logger.Log.BulkStatistic(loggingContext, new Dictionary<string, long>
            {
                [Statistics.ApiTotalMaterializeFileCalls] = Volatile.Read(ref m_numMaterializeFile),
                [Statistics.ApiTotalReportStatisticsCalls] = Volatile.Read(ref m_numReportStatistics),
                [Statistics.ApiTotalGetSealedDirectoryContentCalls] = Volatile.Read(ref m_numGetSealedDirectoryContent),
            });
        }

        async Task<IIpcResult> IIpcOperationExecutor.ExecuteAsync(IIpcOperation op)
        {
            Contract.Requires(op != null);

            Tracing.Logger.Log.ApiServerOperationReceived(m_loggingContext, op.Payload);
            var maybeIpcResult = await TryDeserialize(op.Payload)
                .ThenAsync(cmd => TryExecuteCommand(cmd));

            return maybeIpcResult.Succeeded
                ? maybeIpcResult.Result
                : new IpcResult(IpcResultStatus.ExecutionError, maybeIpcResult.Failure.Describe());
        }

        /// <summary>
        /// Generic ExecuteCommand.  Pattern matches <paramref name="cmd"/> and delegates
        /// to a specific Execute* method based on the commands type.
        /// </summary>
        private async Task<Possible<IIpcResult>> TryExecuteCommand(Command cmd)
        {
            Contract.Requires(cmd != null);

            var materializeFileCmd = cmd as MaterializeFileCommand;
            if (materializeFileCmd != null)
            {
                var result = await ExecuteCommandWithStats(ExecuteMaterializeFileAsync, materializeFileCmd, ref m_numMaterializeFile);
                return new Possible<IIpcResult>(result);
            }

            var reportStatisticsCmd = cmd as ReportStatisticsCommand;
            if (reportStatisticsCmd != null)
            {
                var result = await ExecuteCommandWithStats(ExecuteReportStatistics, reportStatisticsCmd, ref m_numReportStatistics);
                return new Possible<IIpcResult>(result);
            }

            var getSealedDirectoryFilesCmd = cmd as GetSealedDirectoryContentCommand;
            if(getSealedDirectoryFilesCmd != null)
            {
                var result = await ExecuteCommandWithStats(ExecuteGetSealedDirectoryContent, getSealedDirectoryFilesCmd, ref m_numGetSealedDirectoryContent);
                return new Possible<IIpcResult>(result);
            }

            var errorMessage = "Unimplemented command: " + cmd.GetType().FullName;
            Contract.Assert(false, errorMessage);
            return new Failure<string>(errorMessage);
        }

        /// <summary>
        /// Executes <see cref="MaterializeFileCommand"/>.  First check that <see cref="MaterializeFileCommand.File"/>
        /// and <see cref="MaterializeFileCommand.FullFilePath"/> match, then delegates to <see cref="FileContentManager.TryMaterializeFileAsync"/>.
        /// </summary>
        private async Task<IIpcResult> ExecuteMaterializeFileAsync(MaterializeFileCommand cmd)
        {
            Contract.Requires(cmd != null);

            // for extra safety, check that provided file path and file id match
            AbsolutePath filePath;
            bool isValidPath = AbsolutePath.TryCreate(m_context.PathTable, cmd.FullFilePath, out filePath);
            if (!isValidPath || !cmd.File.Path.Equals(filePath))
            {
                return new IpcResult(
                    IpcResultStatus.ExecutionError,
                    "file path ids differ; file = " + cmd.File.Path.ToString(m_context.PathTable) + ", file path = " + cmd.FullFilePath);
            }

            var result = await m_fileContentManager.TryMaterializeFileAsync(cmd.File);
            bool succeeded = result == ArtifactMaterializationResult.Succeeded;
            string absoluteFilePath = cmd.File.Path.ToString(m_context.PathTable);

            // if file materialization failed, log an error here immediately, so that this errors gets picked up as the root cause 
            // (i.e., the "ErrorBucket") instead of whatever fallout ends up happening (e.g., IPC pip fails)
            if (!succeeded)
            {
                Tracing.Logger.Log.ErrorApiServerMaterializeFileFailed(m_loggingContext, absoluteFilePath, result.ToString());
            }
            else
            {
                Tracing.Logger.Log.ApiServerMaterializeFileSucceeded(m_loggingContext, absoluteFilePath);
            }

            return IpcResult.Success(cmd.RenderResult(succeeded));
        }

        /// <summary>
        /// Executes <see cref="ReportStatisticsCommand"/>.
        /// </summary>
        private Task<IIpcResult> ExecuteReportStatistics(ReportStatisticsCommand cmd)
        {
            Contract.Requires(cmd != null);

            Tracing.Logger.Log.ApiServerReportStatisticsExecuted(m_loggingContext, cmd.Stats.Count);
            Logger.Log.BulkStatistic(m_loggingContext, cmd.Stats);
            return Task.FromResult(IpcResult.Success(cmd.RenderResult(true)));
        }

        private async Task<IIpcResult> ExecuteGetSealedDirectoryContent(GetSealedDirectoryContentCommand cmd)
        {
            Contract.Requires(cmd != null);

            // for extra safety, check that provided directory path and directory id match
            AbsolutePath dirPath;
            bool isValidPath = AbsolutePath.TryCreate(m_context.PathTable, cmd.FullDirectoryPath, out dirPath);
            if (!isValidPath || !cmd.Directory.Path.Equals(dirPath))
            {
                return new IpcResult(
                    IpcResultStatus.ExecutionError,
                    "directory path ids differ, or could not create AbsolutePath; directory = " + cmd.Directory.Path.ToString(m_context.PathTable) + ", directory path = " + cmd.FullDirectoryPath);
            }

            var files = m_fileContentManager.ListSealedDirectoryContents(cmd.Directory);

            Tracing.Logger.Log.ApiServerGetSealedDirectoryContentExecuted(m_loggingContext, cmd.Directory.Path.ToString(m_context.PathTable));

            var inputContentsTasks = files
                .Select(f => m_fileContentManager.TryQuerySealedOrUndeclaredInputContentAsync(f.Path, nameof(ApiServer), false))
                .ToArray();

            var inputContents = await TaskUtilities.SafeWhenAll(inputContentsTasks);

            var results = new List<BuildXL.Ipc.ExternalApi.SealedDirectoryFile>();
            var failedResults = new List<string>();

            for (int i = 0; i < files.Length; ++i)
            {
                if (!inputContents[i].HasValue || !inputContents[i].Value.HasKnownLength)
                {
                    failedResults.Add(files[i].Path.ToString(m_context.PathTable));
                }
                else
                {
                    results.Add(new BuildXL.Ipc.ExternalApi.SealedDirectoryFile(
                        files[i].Path.ToString(m_context.PathTable),
                        files[i],
                        inputContents[i].Value));
                }
            }

            if (failedResults.Count > 0)
            {
                new IpcResult(
                    IpcResultStatus.ExecutionError,
                    "could not find content information for the files: " + string.Join("; ", failedResults));
            }

            return IpcResult.Success(cmd.RenderResult(results));
        }

        private Possible<Command> TryDeserialize(string operation)
        {
            try
            {
                return Command.Deserialize(operation);
            }
            catch (Exception e)
            {
                Tracing.Logger.Log.ApiServerInvalidOperation(m_loggingContext, operation, e.ToStringDemystified());
                return new Failure<string>("Invalid operation: " + operation);
            }
        }

        private static Task<IIpcResult> ExecuteCommandWithStats<TCommand>(Func<TCommand, Task<IIpcResult>> executor, TCommand cmd, ref long totalCounter)
            where TCommand : Command
        {
            Interlocked.Increment(ref totalCounter);
            return executor(cmd);
        }
    }
}
