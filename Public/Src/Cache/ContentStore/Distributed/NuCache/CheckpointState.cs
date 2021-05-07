// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Checkpoint state obtained from the central store.
    /// </summary>
    public readonly struct CheckpointState
    {
        /// <nodoc />
        public Role Role { get; }

        /// <nodoc />
        public EventSequencePoint StartSequencePoint { get; }

        /// <nodoc />
        public string CheckpointId { get; }

        /// <nodoc />
        public DateTime CheckpointTime { get; }

        /// <nodoc />
        public bool CheckpointAvailable => !string.IsNullOrEmpty(CheckpointId);

        /// <nodoc />
        public MachineLocation Producer { get; }

        /// <nodoc />
        public MachineLocation Master { get; }

        /// <nodoc />
        public CheckpointState(Role role, EventSequencePoint startSequencePoint, string checkpointId, DateTime checkpointTime, MachineLocation producer, MachineLocation master)
        {
            Contract.Requires(!string.IsNullOrEmpty(checkpointId));

            Role = role;
            StartSequencePoint = startSequencePoint;
            CheckpointId = checkpointId;
            CheckpointTime = checkpointTime;
            Producer = producer;
            Master = master;
        }

        /// <nodoc />
        private CheckpointState(Role role, DateTime epochStartCursorTime, MachineLocation master)
            : this()
        {
            Role = role;
            StartSequencePoint = new EventSequencePoint(epochStartCursorTime);
            Master = master;
        }

        /// <nodoc />
        public static CheckpointState CreateUnavailable(Role role, DateTime epochStartCursorTime, MachineLocation master)
        {
            return new CheckpointState(role, epochStartCursorTime, master);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return CheckpointAvailable ? $"Id={CheckpointId}, Time={CheckpointTime}, StartSeqPt={StartSequencePoint}" : "Unavailable";

        }
    }
}
