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

        /// <nodoc />
        public ReportServicePipIsReadyCommand(int processId, string processName)
        {
            ProcessId = processId;
            ProcessName = processName;
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
        }

        internal static Command InternalDeserialize(BinaryReader reader)
        {
            var processId = reader.ReadInt32();
            var processName = reader.ReadString();

            return new ReportServicePipIsReadyCommand(processId, processName);
        }
    }
}
