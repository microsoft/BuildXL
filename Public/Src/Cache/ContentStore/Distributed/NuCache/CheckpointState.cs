// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Checkpoint state obtained from the central store.
    /// </summary>
    /// <remarks>
    /// This is not a record because .NET Core 3.1 does not support specifying constructors.
    /// 
    /// See: https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-immutability?pivots=dotnet-5-0
    /// </remarks>
    public class CheckpointState : IEquatable<CheckpointState>
    {
        /// <nodoc />
        public EventSequencePoint StartSequencePoint { get; set; }

        /// <nodoc />
        public string CheckpointId { get; set; }

        /// <nodoc />
        [JsonIgnore]
        internal BlobName? FileName { get; set; }

        /// <nodoc />
        [JsonIgnore]
        public bool CheckpointAvailable => !string.IsNullOrEmpty(CheckpointId);

        /// <nodoc />
        public DateTime CheckpointTime { get; set; }

        /// <nodoc />
        public MachineLocation Producer { get; set; }

        /// <summary>
        /// Machines currently registered as consumers of the checkpoint
        /// </summary>
        public SetList<MachineLocation> Consumers { get; set; } = new SetList<MachineLocation>();

        // Only exposed for back compat in json serialization
        public DateTime CreationTimeUtc { get => CheckpointTime; set => CheckpointTime = value; }

        public CheckpointState()
            : this(EventSequencePoint.Invalid)
        {
        }

        /// <nodoc />
        public CheckpointState(
            EventSequencePoint startSequencePoint,
            string? checkpointId = null,
            DateTime? checkpointTime = null,
            MachineLocation producer = default)
        {
            StartSequencePoint = startSequencePoint;
            CheckpointId = checkpointId ?? string.Empty;
            CheckpointTime = checkpointTime ?? DateTime.MinValue;
            Producer = producer;
        }

        public static CheckpointState CreateUnavailable(DateTime epochStartCursorTime)
        {
            return new CheckpointState(new EventSequencePoint(epochStartCursorTime));
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"Id={CheckpointId}, CheckpointTime={CheckpointTime}, StartSequencePoint={StartSequencePoint}, Producer={Producer}, Consumers={Consumers.Count}";
        }

        public bool Equals([AllowNull] CheckpointState other)
        {
            if (other == null)
            {
                return false;
            }

            return Equals(this, other, s => (s.CheckpointId, s.CheckpointTime, s.StartSequencePoint, s.Producer, s.Consumers.Count));
        }

        public override int GetHashCode()
        {
            return GetHashCode(this, s => (s.CheckpointId, s.CheckpointTime, s.StartSequencePoint, s.Producer, s.Consumers.Count));
        }

        private bool Equals<T, TEquatable>(T t0, T t1, Func<T, TEquatable> getEquatable)
            where TEquatable : IEquatable<TEquatable>
        {
            return getEquatable(t0).Equals(getEquatable(t1));
        }
        private int GetHashCode<T, TEquatable>(T t, Func<T, TEquatable> getEquatable)
            where TEquatable : struct, IEquatable<TEquatable>
        {
            return getEquatable(t).GetHashCode();
        }

        public struct ContentEntry : IKeyedItem<ShortHash>
        {
            public ShortHash Hash { get; set; }
            public long Size { get; set; }

            public ShortHash GetKey()
            {
                return Hash;
            }
        }
    }
}
