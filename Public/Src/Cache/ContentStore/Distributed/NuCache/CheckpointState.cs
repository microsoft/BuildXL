// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        public CheckpointState(Role role, EventSequencePoint startSequencePoint, string checkpointId, DateTime checkpointTime)
        {
            Contract.Requires(!string.IsNullOrEmpty(checkpointId));

            Role = role;
            StartSequencePoint = startSequencePoint;
            CheckpointId = checkpointId;
            CheckpointTime = checkpointTime;
        }

        /// <nodoc />
        private CheckpointState(Role role, DateTime epochStartCursorTime)
            : this()
        {
            Role = role;
            StartSequencePoint = new EventSequencePoint(epochStartCursorTime);
        }

        /// <nodoc />
        public static CheckpointState CreateUnavailable(Role role, DateTime epochStartCursorTime)
        {
            return new CheckpointState(role, epochStartCursorTime);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return CheckpointAvailable ? CheckpointId : "Unavailable";

        }
    }
}
