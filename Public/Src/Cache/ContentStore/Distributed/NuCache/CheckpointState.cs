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
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Interfaces.Results;

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
        public bool CheckpointAvailable => !string.IsNullOrEmpty(CheckpointId);

        /// <nodoc />
        public DateTime CheckpointTime { get; set; }

        /// <nodoc />
        public MachineLocation Producer { get; set; }

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

        public bool Equals(CheckpointState? other)
        {
            if (other is null)
            {
                return false;
            }

            return StartSequencePoint.Equals(other.StartSequencePoint) &&
                CheckpointId.Equals(other.CheckpointId) &&
                CheckpointTime.Equals(other.CheckpointTime) &&
                Producer.Equals(other.Producer);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (StartSequencePoint, CheckpointId, CheckpointTime, Producer).GetHashCode();
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            if (obj is null || obj is not CheckpointState)
            {
                return false;
            }

            return EqualityComparer<CheckpointState>.Default.Equals(this, (obj as CheckpointState)!);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"Id={CheckpointId}, CheckpointTime={CheckpointTime}, StartSequencePoint={StartSequencePoint}, Producer={Producer}";
        }

        public Result<string> ToJson(JsonSerializerOptions? options = null)
        {
            try
            {
                return Result.Success(JsonSerializer.Serialize(this, options));
            }
            catch (Exception e)
            {
                return Result.FromException<string>(e);
            }
        }

        public static async Task<Result<CheckpointState>> FromJsonStreamAsync(Stream stream, CancellationToken token = default)
        {
            try
            {
                var checkpointState = await JsonSerializer.DeserializeAsync<CheckpointState>(
                    stream,
                    cancellationToken: token);

                return Result.Success(checkpointState!);
            }
            catch (Exception e)
            {
                return Result.FromException<CheckpointState>(e);
            }
        }
    }
}
