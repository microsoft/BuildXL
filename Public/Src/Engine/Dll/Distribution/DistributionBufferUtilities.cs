// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Utilities.Core;

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// Identifies the role of a reusable distribution buffer for replacement telemetry.
    /// </summary>
    internal enum DistributionBufferKind
    {
        ExecutionLogEvents,
        ManifestEvents,
        FlushedManifestEvents,
        FlushedExecutionLog,
    }

    /// <summary>
    /// Provides a consistent reset policy for reusable distribution buffers.
    /// </summary>
    /// <remarks>
    /// Clearing a <see cref="MemoryStream"/> resets its length but retains its backing array. Reusing normally sized
    /// buffers avoids repeated allocations, while replacing buffers that grew unusually large prevents transient
    /// distribution payloads from remaining in the worker's retained memory.
    /// </remarks>
    internal static class DistributionBufferUtilities
    {
        /// <summary>
        /// Maximum buffer capacity retained for reuse after a distribution payload is processed.
        /// </summary>
        /// <remarks>
        /// This is a conservative retention cap rather than an empirically tuned optimum. It preserves allocation
        /// reuse for routine payloads while preventing atypically large backing arrays from remaining live for the
        /// rest of the build. Reset-capacity and replacement counters provide production data for tuning this value.
        /// </remarks>
        internal const int MaximumRetainedCapacity = 1 << 20;

        /// <summary>
        /// Resets <paramref name="buffer"/> for reuse, replacing it when its capacity exceeds
        /// <see cref="MaximumRetainedCapacity"/>.
        /// </summary>
        /// <param name="buffer">
        /// The buffer to reset. The reference may be replaced with a new empty stream.
        /// </param>
        /// <param name="counters">Distribution counters that receive capacity and replacement telemetry.</param>
        /// <param name="bufferKind">The role of the buffer being reset.</param>
        /// <param name="maximumRetainedCapacity">Maximum backing-buffer capacity retained for reuse.</param>
        internal static void Reset(
            ref MemoryStream buffer,
            CounterCollection<DistributionCounter> counters,
            DistributionBufferKind bufferKind,
            int maximumRetainedCapacity = MaximumRetainedCapacity)
        {
            int capacity = buffer.Capacity;
            counters.IncrementCounter(GetResetCapacityCounter(capacity));

            if (capacity > maximumRetainedCapacity)
            {
                counters.IncrementCounter(DistributionCounter.DistributionBufferReplacementCount);
                counters.AddToCounter(DistributionCounter.DistributionBufferDiscardedCapacityBytes, capacity);
                counters.IncrementCounter(GetReplacementCountCounter(bufferKind));
                counters.AddToCounter(GetDiscardedCapacityCounter(bufferKind), capacity);

                buffer.Dispose();
                buffer = new MemoryStream();
            }
            else
            {
                buffer.SetLength(0);
            }
        }

        /// <summary>
        /// Resets a buffer without recording distribution telemetry.
        /// </summary>
        internal static void Reset(ref MemoryStream buffer, int maximumRetainedCapacity)
        {
            if (buffer.Capacity > maximumRetainedCapacity)
            {
                buffer.Dispose();
                buffer = new MemoryStream();
            }
            else
            {
                buffer.SetLength(0);
            }
        }

        private static DistributionCounter GetResetCapacityCounter(int capacity)
        {
            if (capacity <= 1 << 16)
            {
                return DistributionCounter.DistributionBufferResetCapacity64KBOrLess;
            }

            if (capacity <= 1 << 18)
            {
                return DistributionCounter.DistributionBufferResetCapacity256KBOrLess;
            }

            if (capacity <= MaximumRetainedCapacity)
            {
                return DistributionCounter.DistributionBufferResetCapacity1MBOrLess;
            }

            if (capacity <= 1 << 22)
            {
                return DistributionCounter.DistributionBufferResetCapacity4MBOrLess;
            }

            return DistributionCounter.DistributionBufferResetCapacityGreaterThan4MB;
        }

        private static DistributionCounter GetReplacementCountCounter(DistributionBufferKind bufferKind)
        {
            return bufferKind switch
            {
                DistributionBufferKind.ExecutionLogEvents => DistributionCounter.ExecutionLogEventBufferReplacementCount,
                DistributionBufferKind.ManifestEvents => DistributionCounter.ManifestEventBufferReplacementCount,
                DistributionBufferKind.FlushedManifestEvents => DistributionCounter.FlushedManifestEventBufferReplacementCount,
                DistributionBufferKind.FlushedExecutionLog => DistributionCounter.FlushedExecutionLogBufferReplacementCount,
                _ => throw new System.ArgumentOutOfRangeException(nameof(bufferKind)),
            };
        }

        private static DistributionCounter GetDiscardedCapacityCounter(DistributionBufferKind bufferKind)
        {
            return bufferKind switch
            {
                DistributionBufferKind.ExecutionLogEvents => DistributionCounter.ExecutionLogEventBufferDiscardedCapacityBytes,
                DistributionBufferKind.ManifestEvents => DistributionCounter.ManifestEventBufferDiscardedCapacityBytes,
                DistributionBufferKind.FlushedManifestEvents => DistributionCounter.FlushedManifestEventBufferDiscardedCapacityBytes,
                DistributionBufferKind.FlushedExecutionLog => DistributionCounter.FlushedExecutionLogBufferDiscardedCapacityBytes,
                _ => throw new System.ArgumentOutOfRangeException(nameof(bufferKind)),
            };
        }
    }
}
