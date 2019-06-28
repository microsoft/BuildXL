// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using static BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming.ContentLocationEventStoreCounters;

namespace BuildXL.Cache.ContentStore.Distributed.Tracing
{
    /// <summary>
    /// Contains a set of extension methods for <see cref="Tracer"/> type with high-level logging operations for the current project.
    /// </summary>
    internal static class TracingStructuredExtensions
    {
        public const int ShortHashTracingDefaultBatchSize = 100;

        private static readonly string _uniqueContentCountMetricName = "UniqueContentCount";
        private static readonly string _uniqueContentSizeMetricName = "UniqueContentSize";
        private static readonly string _totalContentCountMetricName = "TotalContentCount";
        private static readonly string _totalContentSizeMetricName = "TotalContentSize";

        /// <nodoc />
        public static void LogMachineMapping(
            this OperationContext context,
            Tracer tracer,
            MachineId machineId,
            MachineLocation location)
        {
            tracer.Debug(context, $"{tracer.Name}: Machine mapping: Id:{machineId}, Location:{location}");
        }

        /// <nodoc />
        public static void GarbageCollectionFinished(
            this Tracer tracer,
            OperationContext context,
            TimeSpan duration,
            long totalEntries,
            long removedEntries,
            long cumulativeRemovedEntries,
            long uniqueContentCount,
            long uniqueContentSize,
            long totalContentCount,
            long totalContentSize)
        {
            tracer.Info(context.TracingContext, $"{tracer.Name}: Garbage collection finished by {duration.ToMilliseconds()}ms. Total: {totalEntries}, Removed: {removedEntries}, Cumulative Removed: {cumulativeRemovedEntries}");

            tracer.TrackMetric(context, _uniqueContentCountMetricName, uniqueContentCount);
            tracer.TrackMetric(context, _uniqueContentSizeMetricName, uniqueContentSize);
            tracer.TrackMetric(context, _totalContentCountMetricName, totalContentCount);
            tracer.TrackMetric(context, _totalContentSizeMetricName, totalContentSize);

            tracer.OperationFinished(context, BoolResult.Success, duration);
        }

        /// <nodoc />
        public static string GetShortHashesTraceString(this IEnumerable<ShortHash> hashes)
        {
            return string.Join(", ", hashes);
        }

        /// <nodoc />
        public static string GetShortHashesTraceString(this IEnumerable<ContentHash> hashes)
        {
            return string.Join(", ", hashes.Select(h => new ShortHash(h)));
        }

        /// <nodoc />
        public static string GetShortHashesTraceString(this GetBulkLocationsResult result)
        {
            if (!result)
            {
                return result.ToString();
            }

            return string.Join(", ", result.ContentHashesInfo.Select(info => $"{new ShortHash(info.ContentHash)}={info.Locations?.Count ?? 0}"));
        }

        /// <nodoc />
        public static void LogContentLocationOperations(
            OperationContext context,
            string tracerName,
            IEnumerable<(ShortHash hash, EntryOperation op, OperationReason reason, int modificationCount)> operations)
        {
            foreach (var group in operations.GroupBy(t => (t.op, t.reason)))
            {
                foreach (var page in group.GetPages(ShortHashTracingDefaultBatchSize))
                {
                    var results = string.Join(", ", page.GroupBy(t => t.hash).Select(g => $"{g.Key.ToString()}={g.Sum(o => o.modificationCount)}"));
                    context.TraceDebug($"{tracerName}: Handling operation {PrintOperation(group.Key)}({page.Count()}): [{results}]");
                }
            }
        }

        /// <nodoc />
        public static void LogProcessEventsOverview(this OperationContext context, CounterCollection<ContentLocationEventStoreCounters> eventStoreCounters, int duration)
        {
            var sb = new StringBuilder();
            sb.Append($"TotalMessagesSize={eventStoreCounters[ReceivedMessagesTotalSize].Value}, ")
                .Append($"DeserializationDuration={(long)eventStoreCounters[Deserialization].Duration.TotalMilliseconds}ms, ")
                .Append($"#Events={eventStoreCounters[DispatchEvents].Value}, ")
                .Append($"DispatchDuration={(long)eventStoreCounters[DispatchEvents].Duration.TotalMilliseconds}ms, ")
                .Append($"FilteredEvents={eventStoreCounters[FilteredEvents].Value}, ")
                .Append("[Operation, #Events, #Hashes, DispatchDuration(ms)] => ")
                .Append($"[Add, #{eventStoreCounters[DispatchAddLocations].Value}, #{eventStoreCounters[DispatchAddLocationsHashes].Value}, {(long)eventStoreCounters[DispatchAddLocations].Duration.TotalMilliseconds}ms], ")
                .Append($"[Remove, #{eventStoreCounters[DispatchRemoveLocations].Value}, #{eventStoreCounters[DispatchRemoveLocationsHashes].Value}, {(long)eventStoreCounters[DispatchRemoveLocations].Duration.TotalMilliseconds}ms], ")
                .Append($"[Touch, #{eventStoreCounters[DispatchTouch].Value}, #{eventStoreCounters[DispatchTouchHashes].Value}, {(long)eventStoreCounters[DispatchTouch].Duration.TotalMilliseconds}ms], ")
                .Append($"[Reconcile, #{eventStoreCounters[DispatchReconcile].Value}, N/A, {(long)eventStoreCounters[DispatchReconcile].Duration.TotalMilliseconds}ms].");
            context.TraceInfo(
                $"{nameof(EventHubContentLocationEventStore)}: processed {eventStoreCounters[ReceivedEventBatchCount].Value} message(s) by {duration}ms. {sb}");
        }

        /// <nodoc />
        public static void LogSendEventsOverview(this OperationContext context, CounterCollection<ContentLocationEventStoreCounters> eventStoreCounters, int duration)
        {
            var sb = new StringBuilder();
            sb.Append($"TotalMessagesSize={eventStoreCounters[SentMessagesTotalSize].Value}, ")
                .Append($"SerializationDuration={(long)eventStoreCounters[Serialization].Duration.TotalMilliseconds}ms, ")
                .Append($"#Events={eventStoreCounters[SentEventsCount].Value}, ")
                .Append("[Operation, #Events, #Hashes] => ")
                .Append($"[Add, #{eventStoreCounters[SentAddLocationsEvents].Value}, #{eventStoreCounters[SentAddLocationsHashes].Value}], ")
                .Append($"[Remove, #{eventStoreCounters[SentRemoveLocationsEvents].Value}, #{eventStoreCounters[SentRemoveLocationsHashes].Value}], ")
                .Append($"[Touch, #{eventStoreCounters[SentTouchLocationsEvents].Value}, #{eventStoreCounters[SentTouchLocationsHashes].Value}], ")
                .Append($"[Reconcile, #{eventStoreCounters[SentReconcileEvents].Value}, N/A].");
            context.TraceInfo($"{nameof(EventHubContentLocationEventStore)}: sent {eventStoreCounters[SentEventBatchCount].Value} message(s) by {duration}ms. {sb}");
        }

        public static void TraceStartupConfiguration(
            this Tracer tracer,
            Context context,
            RedisContentLocationStoreConfiguration configuration)
        {
            string blobStoreConfigurationAsText = configuration.CentralStore is BlobCentralStoreConfiguration blobStoreConfiguration
                ? $"BlobCentralStore=True, #Shards={blobStoreConfiguration.Credentials.Count}, "
                : "BlobCentralStore=False, ";

            var message = $"{tracer.Name}: Starting content location store. "
                          + "Features: "
                          + $"ReadMode={configuration.ReadMode}, WriteMode={configuration.WriteMode}, "
                          + $"ReconciliationEnabled={configuration.EnableReconciliation}, "
                          + $"MachineReputationEnabled={configuration.ReputationTrackerConfiguration?.Enabled ?? false}, "
                          + $"Checkpoint={configuration.Checkpoint != null}, "
                          + $"IncrementalCheckpointing={configuration.Checkpoint?.UseIncrementalCheckpointing == true}, "
                          + $"RaidedRedis={configuration.RedisGlobalStoreSecondaryConnectionString != null}, "
                          + $"SmallFilesInRedis={configuration.AreBlobsSupported}, "
                          + $"DistributedCentralStore={configuration.DistributedCentralStore != null}, "
                          + $"EventHub={configuration.EventStore is EventHubContentLocationEventStoreConfiguration}, "
                          + blobStoreConfigurationAsText
                          + $"RocksDb={configuration.Database is RocksDbContentLocationDatabaseConfiguration}, "
                          + $"BuildXLVersion={BuildXL.Utilities.Branding.Version}, "
                          + $"BuildXLSourceVersion={BuildXL.Utilities.Branding.SourceVersion}, "
                          + $"OSVersion={OperatingSystemHelper.GetOSVersion()}, "
                          + $".NET Framework={OperatingSystemHelper.GetInstalledDotNetFrameworkVersion()}, "
                          ;
            tracer.Info(context, message);
        }

        private static string PrintOperation((EntryOperation op, OperationReason reason) t)
        {
            if (t.reason == OperationReason.Unknown)
            {
                return t.op.ToString();
            }

            return $"{t.reason}_{t.op}";
        }

        internal enum OperationReason
        {
            Unknown,
            Reconcile,
            GarbageCollect
        }

        internal enum EntryOperation
        {
            Invalid,
            AddMachine,
            RemoveMachine,
            Touch,
            Create,
            Delete
        }
    }
}
