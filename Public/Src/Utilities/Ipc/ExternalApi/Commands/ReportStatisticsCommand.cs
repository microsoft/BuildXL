// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;

namespace BuildXL.Ipc.ExternalApi.Commands
{
    /// <summary>
    /// Command corresponding to the <see cref="Client.ReportStatistics"/> API operation.
    /// </summary>
    /// <remarks>
    /// For potential performance reasons, does nothing to ensure that <see cref="Stats"/>
    /// remains immutable.  In any case, no one should really mutate that property, ever.
    /// </remarks>
    public sealed class ReportStatisticsCommand : Command<bool>
    {
        /// <summary>
        /// Statistics to report.
        /// </summary>
        public IDictionary<string, long> Stats { get; }

        /// <nodoc />
        public ReportStatisticsCommand(IDictionary<string, long> stats)
        {
            Contract.Requires(stats != null);

            Stats = stats;
        }

        /// <inheritdoc />
        public override bool TryParseResult(string result, out bool commandResult)
        {
            commandResult = false;
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
            writer.Write(Stats.Count);
            foreach (var entry in Stats)
            {
                writer.Write(entry.Key);
                writer.Write(entry.Value);
            }
        }

        internal static Command InternalDeserialize(BinaryReader reader)
        {
            var capacity = reader.ReadInt32();
            var stats = new Dictionary<string, long>(capacity);
            for (int i = 0; i < capacity; i++)
            {
                stats[reader.ReadString()] = reader.ReadInt64();
            }

            return new ReportStatisticsCommand(stats);
        }
    }
}
