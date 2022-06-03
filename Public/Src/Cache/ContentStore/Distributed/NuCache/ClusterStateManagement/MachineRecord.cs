// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text;
using BuildXL.Cache.ContentStore.Utils;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Single immutable record of a given machine's state. For the entire cluster, <see cref="ClusterStateMachine"/>
    /// </summary>
    /// <remarks>
    /// This class must be serializable due to <see cref="BlobClusterStateStorage"/>
    /// </remarks>
    public record MachineRecord
    {
        /// <summary>
        /// Block ids can only be ~80 characters max (max 64 bytes in base 64)
        /// We choose a reasonable number below that value.
        /// !!CHANGING THIS NUMBER IS A BREAKING CHANGE because all blocks in a blob
        /// must id of the same length and normally there will already be blocks for a blob.
        /// </summary>
        internal const int MaxBlockLength = 56;

        public MachineId Id { get; init; } = MachineId.Invalid;

        public MachineLocation Location { get; init; } = new MachineLocation(string.Empty);

        public MachineState State { get; init; } = MachineState.Unknown;

        public DateTime LastHeartbeatTimeUtc { get; init; } = DateTime.MinValue;

        private DateTime? _creationTimeUtc;
        public DateTime CreationTimeUtc
        {
            get => _creationTimeUtc ?? LastHeartbeatTimeUtc;
            init => _creationTimeUtc = value;
        }

        private string? _machineBlockId;
        public string MachineBlockId
        {
            get => _machineBlockId ?? (_machineBlockId = ComputeBlockId(Location, Id, CreationTimeUtc));
            init => _machineBlockId = value;
        }

        internal MachineRecord Heartbeat(DateTime nowUtc, MachineState nextState)
        {
            if (nextState == MachineState.Unknown)
            {
                return this with { LastHeartbeatTimeUtc = nowUtc, };
            }

            return this with { State = nextState, LastHeartbeatTimeUtc = nowUtc };
        }

        public bool IsOpen()
        {
            return State == MachineState.Open;
        }

        public bool IsClosed()
        {
            return State == MachineState.Closed;
        }

        public bool IsInactive()
        {
            return State == MachineState.DeadExpired || State == MachineState.DeadUnavailable;
        }

        /// <summary>
        /// Compute block id for machine used by <see cref="BlobContentLocationRegistry"/>
        /// </summary>
        private static string ComputeBlockId(MachineLocation location, MachineId machineId, DateTime creationTimeUtc)
        {
            (var host, var port) = location.ExtractHostInfo();
            host = ProcessHostName(host);
            var blockId = $"//{host}+P{port ?? 0}/M{machineId.Index}/{creationTimeUtc.ToReadableString().Replace('.', '+')}/";

            blockId = blockId.PadRight(MaxBlockLength, '0');
            return blockId;
        }

        private static string ProcessHostName(string host)
        {
            const int MAX_HOST_LENGTH = 20;

            StringBuilder sb = new StringBuilder();
            foreach (var ch in host)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(ch);
                    if (sb.Length == MAX_HOST_LENGTH)
                    {
                        break;
                    }
                }
                else if (sb.Length > 5)
                {
                    break;
                }
            }

            return sb.ToString();
        }
    }
}
