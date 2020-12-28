// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using static BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming.ContentLocationEventStoreCounters;

namespace BuildXL.Cache.ContentStore.Distributed.Tracing
{
    /// <summary>
    /// Contains a set of extension methods for <see cref="Tracer"/> type with high-level logging operations for the current project.
    /// </summary>
    public static class TracingStructuredExtensions
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
            Contract.Requires(result.Succeeded);

            return string.Join(", ", result.ContentHashesInfo.Select(info => $"{info.ContentHash.ToShortString()}={info.Locations?.Count ?? 0}"));
        }

        /// <nodoc />
        public static void LogContentLocationOperations<TElement>(
            Context context,
            string tracerName,
            IEnumerable<(TElement hash, EntryOperation op, OperationReason reason)> operations) where TElement : IToStringConvertible
        {
            foreach (var group in operations.GroupBy(t => (t.op, t.reason)))
            {
                foreach (var page in group.GetPages(ShortHashTracingDefaultBatchSize))
                {
                    using var stringBuilderPoolInstance = Pools.StringBuilderPool.GetInstance();
                    var sb = stringBuilderPoolInstance.Instance;
                    sb.Append(tracerName)
                        .Append(": Handling operation ")
                        .Append(PrintOperation(group.Key))
                        .Append("(")
                        .Append(page.Count())
                        .Append(") [");

                    sb.AppendSequence(page.Select(p => p.hash), (builder, hash) => hash.ToString(builder));

                    sb.Append("]");
                    context.Debug(sb.ToString(), component: tracerName);
                }
            }
        }

        /// <nodoc />
        public static void LogProcessEventsOverview(
            this OperationContext context,
            CounterCollection<ContentLocationEventStoreCounters> counters,
            int durationMs,
            UpdatedHashesVisitor updatedHashesVisitor)
        {
            using var stringBuilderPoolInstance = Pools.StringBuilderPool.GetInstance();
            var sb = stringBuilderPoolInstance.Instance;

            var hashesAdded = counters[DispatchAddLocationsHashes].Value;
            var hashesRemoved = counters[DispatchRemoveLocationsHashes].Value;
            var hashesTouched = counters[DispatchTouchHashes].Value;
            sb.Append($"TotalMessagesSize={counters[ReceivedMessagesTotalSize].Value}, ")
                .Append($"DeserializationDuration={counters[Deserialization].TotalMilliseconds}ms, ")
                .Append($"#Events={counters[DispatchEvents].Value}, ")
                .Append($"DispatchDuration={counters[DispatchEvents].TotalMilliseconds}ms, ")
                .Append($"FilteredEvents={counters[FilteredEvents].Value}, ")
                .Append("[Operation, #Events, #Hashes, #DbChanges, DispatchDuration(ms)] => ")
                .Append($"[Add, #{counters[DispatchAddLocations].Value}, #{hashesAdded}, #{counters[DatabaseAddedLocations].Value}, {counters[DispatchAddLocations].TotalMilliseconds}ms], ")
                .Append($"[Remove, #{counters[DispatchRemoveLocations].Value}, #{hashesRemoved}, #{counters[DatabaseRemovedLocations].Value}, {counters[DispatchRemoveLocations].TotalMilliseconds}ms], ")
                .Append($"[Touch, #{counters[DispatchTouch].Value}, #{hashesTouched}, #{counters[DatabaseTouchedLocations].Value}, {counters[DispatchTouch].TotalMilliseconds}ms], ")
                .Append($"[UpdateMetadata, #{counters[DispatchUpdateMetadata].Value}, N/A, #{counters[DatabaseUpdatedMetadata].Value}, {counters[DispatchUpdateMetadata].TotalMilliseconds}ms], ")
                .Append($"[Stored, #{counters[DispatchBlob].Value}, N/A, {counters[DispatchBlob].TotalMilliseconds}ms].")
                .Append($" AddLocationsMinHash={updatedHashesVisitor.AddLocationsMinHash}, AddLocationsMaxHash={updatedHashesVisitor.AddLocationsMaxHash},")
                .Append($" RemoveLocationsMinHash={updatedHashesVisitor.RemoveLocationsMinHash?.ToString() ?? "None"}, RemoveLocationsMaxHash={updatedHashesVisitor.RemoveLocationsMaxHash?.ToString() ?? "None"}");

            context.TraceInfo(
                $"Processed {counters[ReceivedEventBatchCount].Value} message(s) by {durationMs}ms. {sb}",
                component: nameof(EventHubContentLocationEventStore));

            trackMetric(name: "Master_HashesAdded", hashesAdded);
            trackMetric(name: "Master_DatabaseAdded", counters[DatabaseAddedLocations].Value);

            trackMetric(name: "Master_HashesRemoved", hashesRemoved);
            trackMetric(name: "Master_DatabaseRemoved", counters[DatabaseRemovedLocations].Value);

            trackMetric(name: "Master_HashesTouched", hashesTouched);
            trackMetric(name: "Master_DatabaseTouched", counters[DatabaseTouchedLocations].Value);

            trackMetric(name: "Master_HashesProcessed", hashesAdded + hashesRemoved + hashesTouched);

            var totalEvents = counters[DispatchAddLocations].Value + counters[DispatchRemoveLocations].Value + counters[DispatchTouch].Value + counters[DispatchUpdateMetadata].Value;
            trackMetric(name: "Master_EventsProcessed", totalEvents);
            trackMetric(name: "Master_AddEventsProcessed", counters[DispatchAddLocations].Value);
            trackMetric(name: "Master_RemoveEventsProcessed", counters[DispatchRemoveLocations].Value);
            trackMetric(name: "Master_TouchEventsProcessed", counters[DispatchTouch].Value);
            trackMetric(name: "Master_UpdateMetadataEventsProcessed", counters[DispatchUpdateMetadata].Value);
            trackMetric(name: "Master_StoredEventsProcessed", counters[DispatchUpdateMetadata].Value);

            void trackMetric(string name, long value) => context.TrackMetric(name, value, tracerName: nameof(EventHubContentLocationEventStore));
        }

        /// <nodoc />
        public static void LogSendEventsOverview(this OperationContext context, CounterCollection<ContentLocationEventStoreCounters> eventStoreCounters, int duration)
        {
            using var stringBuilderPoolInstance = Pools.StringBuilderPool.GetInstance();
            var sb = stringBuilderPoolInstance.Instance;
            var addedHashes = eventStoreCounters[SentAddLocationsHashes].Value;
            var removedHashes = eventStoreCounters[SentRemoveLocationsHashes].Value;
            var touchedHashes = eventStoreCounters[SentTouchLocationsHashes].Value;

            sb.Append($"TotalMessagesSize={eventStoreCounters[SentMessagesTotalSize].Value}, ")
                .Append($"SerializationDuration={eventStoreCounters[Serialization].TotalMilliseconds}ms, ")
                .Append($"#Events={eventStoreCounters[SentEventsCount].Value}, ")
                .Append("[Operation, #Events, #Hashes] => ")
                .Append($"[Add, #{eventStoreCounters[SentAddLocationsEvents].Value}, #{addedHashes}], ")
                .Append($"[Remove, #{eventStoreCounters[SentRemoveLocationsEvents].Value}, #{removedHashes}], ")
                .Append($"[Touch, #{eventStoreCounters[SentTouchLocationsEvents].Value}, #{touchedHashes}], ")
                .Append($"[UpdateMetadata, #{eventStoreCounters[SentUpdateMetadataEntryEvents].Value}, N/A, {eventStoreCounters[SentUpdateMetadataEntryEvents].TotalMilliseconds}ms], ")
                .Append($"[Stored, #{eventStoreCounters[SentStoredEvents].Value}, N/A].");
            context.TraceInfo($"Sent {eventStoreCounters[SentEventBatchCount].Value} message(s) by {duration}ms. {sb}", component: nameof(EventHubContentLocationEventStore));

            var totalEvents = eventStoreCounters[SentAddLocationsEvents].Value + eventStoreCounters[SentRemoveLocationsEvents].Value +
                eventStoreCounters[SentTouchLocationsEvents].Value + eventStoreCounters[SentUpdateMetadataEntryEvents].Value;

            trackMetric(name: "LLS_TotalHashesSentInEvents", addedHashes + removedHashes + touchedHashes);
            trackMetric(name: "LLS_TotalAddHashesSentInEvents", addedHashes);
            trackMetric(name: "LLS_TotalRemoveHashesSentInEvents", removedHashes);
            trackMetric(name: "LLS_TotalTouchHashesSentInEvents", touchedHashes);

            trackMetric(name: "LLS_EventsSent", totalEvents);
            trackMetric(name: "LLS_AddEventsSent", eventStoreCounters[SentAddLocationsEvents].Value);
            trackMetric(name: "LLS_RemoveEventsSent", eventStoreCounters[SentRemoveLocationsEvents].Value);
            trackMetric(name: "LLS_TouchEventsSent", eventStoreCounters[SentTouchLocationsEvents].Value);
            trackMetric(name: "LLS_UpdateMetadataEventsSent", eventStoreCounters[SentUpdateMetadataEntryEvents].Value);

            void trackMetric(string name, long value) => context.TrackMetric(name, value, tracerName: nameof(EventHubContentLocationEventStore));
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
                          + $"ReconciliationEnabled={configuration.ReconcileMode > ReconciliationMode.None}, "
                          + $"MachineReputationEnabled={configuration.ReputationTrackerConfiguration?.Enabled ?? false}, "
                          + $"Checkpoint={configuration.Checkpoint != null}, "
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

        public enum OperationReason
        {
            Unknown,
            Reconcile,
            GarbageCollect
        }

        public enum EntryOperation
        {
            Invalid,
            AddMachine,
            RemoveMachine,
            Touch,
            Create,
            Delete,
            UpdateMetadataEntry,
            RemoveMetadataEntry,
        }
    }
}
