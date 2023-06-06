// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;

namespace BuildXL.Ipc.ExternalApi.Commands
{
    /// <summary>
    /// Command corresponding to the <see cref="Client.ReportServicePipIsReady"/> API operation.
    /// </summary>
    public sealed class ReportServicePipIsReadyCommand : Command<bool>
    {
        /// <summary>
        /// Process ID of a started service pip.
        /// </summary>
        /// <remarks>
        /// This value will be used to match an API call to a running service.
        /// </remarks>
        public int ProcessId { get; }

        /// <summary>
        /// Process name of a started service pip.
        /// </summary>
        /// <remarks>
        /// This is used just so we have more descriptive logging.
        /// </remarks>
        public string ProcessName { get; }
        
        /// <summary>
        /// If set, contains a connection string used the server inside of a service pip
        /// and indicated that the original connection string could not be used.
        /// </summary>
        public string NewConnectionString { get; }

        /// <nodoc />
        public ReportServicePipIsReadyCommand(int processId, string processName, string newConnectionString)
        {
            ProcessId = processId;
            ProcessName = processName;
            NewConnectionString = newConnectionString;
        }

        /// <inheritdoc />
        public override bool TryParseResult(string result, out bool commandResult)
        {
            return bool.TryParse(result, out commandResult);
        }

        /// <inheritdoc />
        public override string RenderResult(bool commandResult)
        {
            return commandResult.ToString();
        }

        internal override void InternalSerialize(BinaryWriter writer)
        {
            writer.Write(ProcessId);
            writer.Write(ProcessName);
            if (NewConnectionString != null)
            {
                writer.Write(true);
                writer.Write(NewConnectionString);
            }
            else
            {
                writer.Write(false);
            }
        }

        internal static Command InternalDeserialize(BinaryReader reader)
        {
            var processId = reader.ReadInt32();
            var processName = reader.ReadString();
            var newConnectionString = reader.ReadBoolean() ? reader.ReadString() : null;

            return new ReportServicePipIsReadyCommand(processId, processName, newConnectionString);
        }
    }
}
