// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

        #region Constructors and Factory Methods

        /// <nodoc/>
        public Client(IClient client)
        {
            m_client = client;
        }

        /// <summary>
        /// Convenient factory method.
        /// </summary>
        public static Client Create(string moniker, IClientConfig config = null)
        {
            var provider = IpcFactory.GetProvider();
            return new Client(provider.GetClient(moniker, config ?? new ClientConfig()));
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
        /// Lists the content of a sealed directory
        /// </summary>
        public Task<Possible<List<SealedDirectoryFile>>> GetSealedDirectoryContent(DirectoryArtifact directory, string fullDirectoryPath)
        {
            return ExecuteCommand(new GetSealedDirectoryContentCommand(directory, fullDirectoryPath));
        }
        #endregion

        private async Task<Possible<T>> ExecuteCommand<T>(Command<T> command)
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
                return new Failure<string>("Cannot parse IPC result: " + ipcResult.ToString());
            }

            return cmdResult;
        }
    }
}
