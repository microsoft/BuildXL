// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities;
using Microsoft.Azure.EventHubs;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming
{
    /// <summary>
    /// The way <see cref="ContentLocationEventDataSerializer"/> should validate serialized entries.
    /// </summary>
    public enum ValidationMode
    {
        /// <summary>
        /// The validation is off.
        /// </summary>
        Off,

        /// <summary>
        /// Trace if the deserialized entries are not the same as the original set.
        /// </summary>
        Trace,

        /// <summary>
        /// Fail if the deserialized entries are not the same as the original set.
        /// </summary>
        Fail,
    }

    /// <summary>
    /// Helper class used for serialization/deserialization of <see cref="ContentLocationEventData"/> instances.
    /// </summary>
    public sealed class ContentLocationEventDataSerializer
    {
        private readonly ValidationMode _validationMode;
        private const string Prefix = nameof(ContentLocationEventDataSerializer);

        /// <summary>
        /// Max size of the <see cref="EventData"/> instance.
        /// </summary>
        public const int MaxEventDataPayloadSize = 200 << 10;

        private const int MaxDesiredTraceMessageSize = 4096;

        private readonly StreamBinaryWriter _writer = new StreamBinaryWriter();
        private readonly StreamBinaryReader _reader = new StreamBinaryReader();

        /// <inheritdoc />
        public ContentLocationEventDataSerializer(ValidationMode validationMode)
        {
            _validationMode = validationMode;
        }

        /// <nodoc />
        public IReadOnlyList<EventData> Serialize(OperationContext context, IReadOnlyList<ContentLocationEventData> eventDatas)
        {
            var result = SerializeCore(context, eventDatas).ToList();

            if (_validationMode != ValidationMode.Off)
            {
                var deserializedEvents = result.SelectMany(e => DeserializeEvents(e, DateTime.Now)).ToList();
                AnalyzeEquality(context, eventDatas, deserializedEvents);
            }

            return result;
        }

        private static bool Equal(IReadOnlyList<ContentLocationEventData> originalEventDatas, IReadOnlyList<ContentLocationEventData> deserializedEvents)
        {
            // Flatten the event datas for comparison
            var left = originalEventDatas.SelectMany(data => GetIndices(data).Select(index => ((data, index)))).ToList();
            var right = deserializedEvents.SelectMany(data => GetIndices(data).Select(index => (data, index))).ToList();

            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!Equal(left[i].data, left[i].index, right[i].data, right[i].index))
                {
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<int> GetIndices(ContentLocationEventData data)
        {
            // Always have at least one index to ensure that events with no hashes like Reconcile are not skipped
            return Enumerable.Range(0, Math.Max(data.ContentHashes.Count, 1));
        }

        private static bool Equal(ContentLocationEventData left, int leftIndex, ContentLocationEventData right, int rightIndex)
        {
            if (left.Kind != right.Kind || left.Sender != right.Sender)
            {
                return false;
            }

            if (left.Kind == EventKind.Reconcile)
            {
                // Reconcile blobs do not have hashes so just check blob id equality
                var leftReconcile = (ReconcileContentLocationEventData)left;
                var rightReconcile = (ReconcileContentLocationEventData)right;
                return leftReconcile.BlobId == rightReconcile.BlobId;
            }

            if (left.ContentHashes.Count == 0 || right.ContentHashes.Count == 0)
            {
                // This is a strange case where an event other than reconcile has zero hashes. Not an error per se.
                // That said, we verify that an empty event data matches when deserialized.
                return left.ContentHashes.Count == right.ContentHashes.Count;
            }

            if (left.ContentHashes[leftIndex] != right.ContentHashes[rightIndex])
            {
                return false;
            }

            switch (left.Kind)
            {
                case EventKind.AddLocation:
                    var leftAdd = (AddContentLocationEventData)left;
                    var rightAdd = (AddContentLocationEventData)right;
                    if (leftAdd.ContentSizes[leftIndex] != rightAdd.ContentSizes[rightIndex])
                    {
                        return false;
                    }
                    break;
            }

            return true;
        }

        private string PrintHashInfo(ContentLocationEventData eventData, int index)
        {
            switch (eventData)
            {
                case AddContentLocationEventData add:
                    return $"{add.ContentHashes[index].ToString()}: {add.ContentSizes[index]}";
                default:
                    return eventData.ContentHashes[index].ToString();
            }
        }

        private string GetTraceInfo(ContentLocationEventData eventData)
        {
            switch (eventData)
            {
                case ReconcileContentLocationEventData reconcile:
                    return $"{reconcile.BlobId}";
                default:
                    return string.Join(", ", Enumerable.Range(0, eventData.ContentHashes.Count).Select(index => PrintHashInfo(eventData, index)));
            }
        }

        private void AnalyzeEquality(OperationContext context, IReadOnlyList<ContentLocationEventData> originalEventDatas, IReadOnlyList<ContentLocationEventData> deserializedEvents)
        {
            // EventData entries may be split due to the size restriction.
            if (!Equal(originalEventDatas, deserializedEvents))
            {
                StringBuilder builder = new StringBuilder();
                var header = $"{Prefix}: Serialization equality mismatch detected.";
                builder.AppendLine($"{header} Emitting original event information:");

                foreach (var eventData in originalEventDatas)
                {
                    builder.AppendLine($"{eventData.Kind}[{eventData.ContentHashes.Count}]({GetTraceInfo(eventData)})");
                }

                // Split up printout of original event information so that it doesn't get discarded by the telemetry pipeline
                var totalLength = builder.Length;
                var traceCount = (int)Math.Ceiling((double)totalLength / MaxDesiredTraceMessageSize);

                for (int i = 0; i < traceCount; i++)
                {
                    var start = i * MaxDesiredTraceMessageSize;
                    var length = Math.Min(totalLength - start, MaxDesiredTraceMessageSize);
                    context.TracingContext.Warning($"{i} of {traceCount}: '{builder.ToString(start, length)}'");
                }

                if (_validationMode == ValidationMode.Fail)
                {
                    throw new InvalidOperationException(header);
                }
            }
        }

        private IEnumerable<EventData> SerializeCore(OperationContext context, IReadOnlyList<ContentLocationEventData> eventDatas)
        {
            // TODO: Maybe a serialization format header. (bug 1365340)
            var splittedEventEntries = SplitLargeInstancesIfNeeded(eventDatas);

            if (splittedEventEntries.Count != eventDatas.Count)
            {
                context.TraceDebug($"{Prefix}: split {eventDatas.Count} to {splittedEventEntries.Count} because of size restrictions.");
            }

            using (_writer.PreservePosition())
            {
                int currentCount = 0;

                while (currentCount < splittedEventEntries.Count)
                {
                    long oldOffset = _writer.Buffer.Position;

                    splittedEventEntries[currentCount].Serialize(_writer.Writer);

                    long newOffset = _writer.Buffer.Position;
                    long eventSize = newOffset - oldOffset;
                    Contract.Assert(eventSize <= MaxEventDataPayloadSize, "No mitigation for single event that is too large");

                    bool isLast = currentCount == (splittedEventEntries.Count - 1);
                    bool isOverflow = newOffset > MaxEventDataPayloadSize;

                    if (isOverflow || isLast)
                    {
                        if (isOverflow)
                        {
                            // Need to change the offset for the overflow case, but not if the last element is serialized.
                            _writer.Buffer.SetLength(oldOffset);
                        }

                        var eventData = new EventData(_writer.Buffer.ToArray());
                        _writer.ResetPosition();

                        yield return eventData;

                        if (!isOverflow)
                        {
                            // Need to break because we don't increment current count
                            yield break;
                        }
                    }
                    else
                    {
                        currentCount++;
                    }
                }
            }
        }

        /// <nodoc />
        public void SerializeReconcileData(OperationContext context, BuildXLWriter writer, MachineId machine, IReadOnlyList<ShortHashWithSize> addedContent, IReadOnlyList<ShortHash> removedContent)
        {
            var entries = new ContentLocationEventData[]
            {
                new AddContentLocationEventData(machine, addedContent),
                new RemoveContentLocationEventData(machine, removedContent)
            };

            var finalEntries = SplitLargeInstancesIfNeeded(entries);

            context.TraceDebug($"{nameof(ContentLocationEventDataSerializer)}: EntriesCount={finalEntries.Count}");

            writer.WriteCompact(finalEntries.Count);
            foreach (var eventData in finalEntries)
            {
                eventData.Serialize(writer);
            }
        }

        /// <nodoc />
        public IEnumerable<ContentLocationEventData> DeserializeReconcileData(BuildXLReader reader)
        {
            var entriesCount = reader.ReadInt32Compact();

            for (int i = 0; i < entriesCount; i++)
            {
                // Using default as eventTimeUtc because reconciliation events should not have touches.
                yield return ContentLocationEventData.Deserialize(reader, eventTimeUtc: default);
            }
        }

        /// <nodoc />
        public IReadOnlyList<ContentLocationEventData> DeserializeEvents(EventData message, DateTime? eventTimeUtc = null)
        {
            if (eventTimeUtc == null)
            {
                Contract.Assert(message.SystemProperties != null, "Either eventTimeUtc argument must be provided or message.SystemProperties must not be null. Did you forget to provde eventTimeUtc arguments in tests?");
                eventTimeUtc = message.SystemProperties.EnqueuedTimeUtc;
            }

            var data = message.Body;
            return _reader.DeserializeSequence(data, reader => ContentLocationEventData.Deserialize(reader, eventTimeUtc.Value)).ToList();
        }

        /// <nodoc />
        public static IReadOnlyList<ContentLocationEventData> SplitLargeInstancesIfNeeded(IReadOnlyList<ContentLocationEventData> source)
        {
            var estimatedSizes = source.Select(e => e.EstimateSerializedInstanceSize()).ToList();
            if (!estimatedSizes.Any(es => es > MaxEventDataPayloadSize))
            {
                // We know that there is no entries with the size that is greater then the max payload size.
                // In this case, just return the original array.
                return source;
            }

            return splitLargeInstancesIfNeededCore().ToList();

            IEnumerable<ContentLocationEventData> splitLargeInstancesIfNeededCore()
            {
                for (int i = 0; i < source.Count; i++)
                {
                    // Splitting the incoming instances only if their estimated size is greater then the max payload size.
                    if (estimatedSizes[i] > MaxEventDataPayloadSize)
                    {
                        foreach (var entry in source[i].Split(MaxEventDataPayloadSize))
                        {
                            yield return entry;
                        }
                    }
                    else
                    {
                        yield return source[i];
                    }
                }
            }
        }
    }
}
