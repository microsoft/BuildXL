// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.FormattableStringEx;

#if !DISABLE_FEATURE_BOND_RPC
using BondTransport;
using Microsoft.Bond;
using BuildXL.Engine.Distribution.InternalBond;
#endif

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// Verifies bond messages
    /// </summary>
    public sealed class DistributionServices
    {
        private const string ChecksumMismatchExceptionMessage = "Checksum did not match for received message";
        private const string BuildIdMismatchExceptionMessage = "Build ids did not match for received message";

        /// <summary>
        /// Counters for message verification
        /// </summary>
        public readonly CounterCollection<DistributionCounter> Counters = new CounterCollection<DistributionCounter>();

        /// <summary>
        /// Build id to represent a distributed build session.
        /// </summary>
        public string BuildId { get; }

        /// <nodoc/>
        public DistributionServices(string buildId)
        {
            BuildId = buildId;
        }

        /// <summary>
        /// Log statistics of distribution counters.
        /// </summary>
        public void LogStatistics(LoggingContext loggingContext)
        {
            Counters.LogAsStatistics("Distribution", loggingContext);
        }

#if !DISABLE_FEATURE_BOND_RPC

        /// <summary>
        /// Buffer manager
        /// </summary>
        internal readonly BufferManager BufferManager = new BufferManager();

        private static uint ComputeChecksum<T>(Message<T> message, out long size) where T : IBondSerializable, new()
        {
            using (var streamWrapper = Pools.MemoryStreamPool.GetInstance())
            {
                var stream = streamWrapper.Instance;
                using (var writer = new CompactBinaryProtocolWriter(stream, leaveOpen: true))
                {
                    message.Payload.Write(writer);

                    var bytes = stream.GetBuffer();
                    var result = HashCodeHelper.Combine(new ArrayView<byte>(bytes, start: 0, length: (int)stream.Position));

                    size = stream.Position;
                    return unchecked((uint)result);
                }
            }
        }

        /// <summary>
        /// Checks whether an exception is a checksum mismatch exception
        /// </summary>
        public bool IsChecksumMismatchException(Exception ex)
        {
            if (ex is RpcException && ex.Message.Contains(ChecksumMismatchExceptionMessage))
            {
                Counters.IncrementCounter(DistributionCounter.ClientChecksumMismatchCount);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks whether an exception is a build id mismatch exception
        /// </summary>
        public static bool IsBuildIdMismatchException(Exception ex)
        {
            if (ex is RpcException && ex.Message.Contains(BuildIdMismatchExceptionMessage))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Verify the message for the checksum and build ids. Dispatch a mismatch exception if checksum or build id does not match
        /// </summary>
        internal bool Verify<TFrom, TTo>(Request<TFrom, TTo> request, string senderBuildId)
            where TFrom : IBondSerializable, new()
            where TTo : IBondSerializable, new()
        {
            if (!string.Equals(BuildId, senderBuildId, StringComparison.OrdinalIgnoreCase))
            {
                request.DispatchException(new BuildXLException(I($"{BuildIdMismatchExceptionMessage}: Self={BuildId}, Sender={senderBuildId}")));
                return false;
            }

            var message = request.Message;
            uint expectedChecksum = message.GetChecksum();
            uint computedChecksum = ComputeChecksum(message, out var size);
            Counters.AddToCounter(DistributionCounter.ReceivedMessageSizeBytes, size);

            if (expectedChecksum != computedChecksum)
            {
                Counters.IncrementCounter(DistributionCounter.ServerChecksumMismatchCount);

                request.DispatchException(new BuildXLException(ChecksumMismatchExceptionMessage));
                return false;
            }

            return true;
        }

        /// <summary>
        /// Computes and assigns checksum
        /// </summary>
        internal void AssignChecksum<TInput>(Message<TInput> message) where TInput : IBondSerializable, new()
        {
            uint checksum = ComputeChecksum(message, out var size);
            Counters.AddToCounter(DistributionCounter.SentMessageSizeBytes, size);
            message.SetChecksum(checksum);
        }
#endif
    }
}
