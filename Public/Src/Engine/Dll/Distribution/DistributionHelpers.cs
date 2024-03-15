// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Linq;
using BuildXL.Engine.Tracing;
using BuildXL.Utilities.Collections;
using Google.Protobuf;

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// This class is encapsulates helper methods and constants for distribution logic.
    /// </summary>
    public static class DistributionHelpers
    {
        /// <summary>
        /// Machine name
        /// </summary>
        public static string MachineName = Environment.MachineName;

        /// <summary>
        /// Set of event ids for distribution error messages
        /// </summary>
        public static readonly ReadOnlyArray<int> DistributionErrors = ReadOnlyArray<int>.FromWithoutCopy(
                (int)LogEventId.DistributionWorkerExitFailure);

        /// <summary>
        /// Set of event ids for distribution warning messages
        /// </summary>
        public static readonly ReadOnlyArray<int> DistributionWarnings = ReadOnlyArray<int>.FromWithoutCopy(
                (int)LogEventId.DistributionFailedToCallOrchestrator,
                (int)LogEventId.DistributionCallWorkerCodeException,
                (int)LogEventId.DistributionCallOrchestratorCodeException,
                (int)LogEventId.DistributionSuccessfulRetryCallToWorker,
                (int)LogEventId.DistributionSuccessfulRetryCallToOrchestrator);

        /// <summary>
        /// Set of event ids for distribution informational messages
        /// </summary>
        public static readonly ReadOnlyArray<int> DistributionInfoMessages = ReadOnlyArray<int>.FromWithoutCopy(
                (int)LogEventId.DistributionDisableServiceProxyInactive,
                (int)LogEventId.DistributionWaitingForOrchestratorAttached,
                (int)LogEventId.DistributionSayingHelloToOrchestrator,
                (int)LogEventId.DistributionHostLog,
                (int)LogEventId.DistributionDebugMessage,
                (int)LogEventId.DistributionServiceInitializationError,
                (int)LogEventId.GrpcTrace,
                (int)LogEventId.GrpcTraceWarning,
                (int)LogEventId.GrpcServerTrace,
                (int)LogEventId.GrpcServerTraceWarning);

        /// <summary>
        /// Set of event ids for distribution messages of all levels
        /// </summary>
        public static readonly ReadOnlyArray<int> DistributionAllMessages = DistributionErrors
            .Concat(DistributionWarnings)
            .Concat(DistributionInfoMessages)
            .ToReadOnlyArray();

        /// <summary>
        /// Convert Bond byte arraysegment to Grpc ByteString
        /// </summary>
        public static ByteString ToByteString(this ArraySegment<byte> bytes)
        {
            return ByteString.CopyFrom(bytes.Array, bytes.Offset, bytes.Count);
        }

        /// <summary>
        /// Convert Grpc ByteString to Bond byte arraysegment
        /// </summary>
        public static ArraySegment<byte> ToArraySegmentByte(this ByteString byteString)
        {
            return new ArraySegment<byte>(byteString.ToByteArray());
        }

        internal static string GetServiceName(string ipAddress, int port)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}::{1}", ipAddress, port);
        }

        /// <nodoc />
        public static BuildXL.Distribution.Grpc.PipSpecificPropertyAndValue ToGrpc(this BuildXL.Utilities.PipSpecificPropertyAndValue pipSpecificPropertyAndValue)
        {
            var serialized = new BuildXL.Distribution.Grpc.PipSpecificPropertyAndValue()
            {
                PipSpecificProperty = (int)pipSpecificPropertyAndValue.PropertyName,
                PipSemiStableHash = pipSpecificPropertyAndValue.PipSemiStableHash
            };

            if (pipSpecificPropertyAndValue.PropertyValue != null)
            {
                serialized.PropertyValue = pipSpecificPropertyAndValue.PropertyValue;
            }

            return serialized;
		}
    }
}
