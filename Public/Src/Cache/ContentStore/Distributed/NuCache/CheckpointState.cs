// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.Json.Serialization;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Checkpoint state obtained from the central store.
    /// </summary>
    public record CheckpointState(
        EventSequencePoint StartSequencePoint,
        string CheckpointId = "",
        DateTime CheckpointTime = default,
        MachineLocation Producer = default)
    {
        /// <nodoc />
        [JsonIgnore]
        public bool IsValid => !string.IsNullOrEmpty(CheckpointId);

        /// <summary>
        /// This constructor is required for <see cref="AzureBlobStorageCheckpointRegistry"/>
        /// </summary>
        public CheckpointState()
            : this(EventSequencePoint.Invalid)
        {
        }

        public static CheckpointState CreateUnavailable(DateTime epochStartCursorTime)
        {
            return new CheckpointState(new EventSequencePoint(epochStartCursorTime));
        }
    }
}
