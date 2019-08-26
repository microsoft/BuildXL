// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tracing;
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
                (int)LogEventId.DistributionFailedToCallMaster,
                (int)LogEventId.DistributionFailedToCallWorker,
                (int)LogEventId.DistributionCallWorkerCodeException,
                (int)LogEventId.DistributionCallMasterCodeException,
                (int)LogEventId.DistributionSuccessfulRetryCallToWorker,
                (int)LogEventId.DistributionSuccessfulRetryCallToMaster);

        /// <summary>
        /// Set of event ids for distribution informational messages
        /// </summary>
        public static readonly ReadOnlyArray<int> DistributionInfoMessages = ReadOnlyArray<int>.FromWithoutCopy(
                (int)LogEventId.DistributionBondCall,
                (int)LogEventId.DistributionDisableServiceProxyInactive,
                (int)LogEventId.DistributionWaitingForMasterAttached,
                (int)LogEventId.DistributionHostLog,
                (int)LogEventId.DistributionDebugMessage,
                (int)LogEventId.DistributionServiceInitializationError,
                (int)LogEventId.GrpcTrace);

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

        /// <summary>
        /// Convert Grpc ByteString to BondContentHash
        /// </summary>
        public static BondContentHash ToBondContentHash(this ByteString byteString)
        {
            return new BondContentHash() { Data = byteString.ToArraySegmentByte() };
        }

        internal static string GetServiceName(int port)
        {
            return GetServiceName(System.Net.Dns.GetHostName(), port);
        }

        internal static string GetServiceName(string ipAddress, int port)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}::{1}", ipAddress, port);
        }

        internal static string GetExecuteDescription(IList<long> semiStableHashes)
        {
            using (var sbPool = Pools.GetStringBuilder())
            {
                var sb = sbPool.Instance;

                sb.Append("ExecutePips: ");
                AppendSemiStableHashes(sb, semiStableHashes);

                return sb.ToString();
            }
        }

        internal static string GetNotifyDescription(OpenBond.WorkerNotificationArgs notificationArgs, IList<long> semiStableHashes)
        {
            using (var sbPool = Pools.GetStringBuilder())
            {
                var sb = sbPool.Instance;

                if (semiStableHashes?.Count > 0)
                {
                    sb.Append("NotifyPipResults: ");
                    AppendSemiStableHashes(sb, semiStableHashes);
                }

                if (notificationArgs.ExecutionLogData != null && notificationArgs.ExecutionLogData.Count > 0)
                {
                    sb.AppendFormat("ExecutionLogData: Size={0}, SequenceNumber={1}", notificationArgs.ExecutionLogData.Count, notificationArgs.ExecutionLogBlobSequenceNumber);
                }

                if (notificationArgs.ForwardedEvents?.Count > 0)
                {
                    sb.AppendFormat("ForwardedEvents: Count={0}", notificationArgs.ForwardedEvents.Count);
                }

                return sb.ToString();
            }
        }

        internal static void AppendSemiStableHashes(StringBuilder builder, IList<long> semiStableHashes)
        {
            if (semiStableHashes.Count > 0)
            {
                builder.AppendFormat(CultureInfo.InvariantCulture, "{0:X16}", semiStableHashes[0]);
                for (int i = 1; i < semiStableHashes.Count; i++)
                {
                    builder.Append(',').AppendFormat(CultureInfo.InvariantCulture, " {0:X16}", semiStableHashes[i]);
                }
            }
        }
    }
}
