// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.IO;

namespace BuildXL.Ipc.ExternalApi.Commands
{
    /// <summary>
    /// Command corresponding to the <see cref="Client.ReportDaemonTelemetry"/> API operation.
    /// </summary>
    public sealed class ReportDaemonTelemetryCommand : Command<bool>
    {
        /// <summary>
        /// The name of a daemon.
        /// </summary>
        public string DaemonName { get; }
        
        /// <summary>
        /// Payload that contains telemetry data
        /// </summary>
        public string TelemetryPayload { get; }

        /// <summary>
        /// Payload that contains various info about a daemon
        /// </summary>
        public string InfoPayload { get; }

        /// <nodoc />
        public ReportDaemonTelemetryCommand(string daemonName, string daemonTelemetryPayload, string daemonInfoPayload)
        {
            Contract.Requires(daemonName != null);
            Contract.Requires(daemonTelemetryPayload != null || daemonInfoPayload != null, "At least one payload must be present");

            DaemonName = daemonName;
            TelemetryPayload = daemonTelemetryPayload;
            InfoPayload = daemonInfoPayload;
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

        /// <inheritdoc />
        internal override void InternalSerialize(BinaryWriter writer)
        {
            writer.Write(DaemonName);
            writer.Write(TelemetryPayload != null);
            if (TelemetryPayload != null)
            {
                writer.Write(TelemetryPayload);
            }

            writer.Write(InfoPayload != null);
            if (InfoPayload != null)
            {
                writer.Write(InfoPayload);
            }
        }

        internal static Command InternalDeserialize(BinaryReader reader)
        {
            var daemonName = reader.ReadString();
            string telemetryPayload = null;
            string infoPayload = null;
            if (reader.ReadBoolean())
            {
                telemetryPayload = reader.ReadString();
            }

            if (reader.ReadBoolean())
            {
                infoPayload = reader.ReadString();
            }

            return new ReportDaemonTelemetryCommand(daemonName, telemetryPayload, infoPayload);
        }
    }
}
