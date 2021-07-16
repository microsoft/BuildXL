// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
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
        private static readonly Tracer Tracer = new Tracer(Prefix);

        /// <summary>
        /// Max size of the <see cref="EventData"/> instance.
        /// </summary>
        public const int MaxEventDataPayloadSize = 200 << 10;

        private const int MaxDesiredTraceMessageSize = 4096;

        private readonly StreamBinaryWriter _writer = new StreamBinaryWriter();
        private readonly StreamBinaryReader _reader = new StreamBinaryReader();

        private readonly bool _synchronize;

        /// <nodoc />
        public ContentLocationEventDataSerializer(ValidationMode validationMode, bool synchronize = false)
        {
            _validationMode = validationMode;
            _synchronize = synchronize;
        }

        /// <nodoc />
        public IReadOnlyList<EventData> Serialize(OperationContext context, IReadOnlyList<ContentLocationEventData> eventDatas)
        {
            return SynchronizeIfNeeded(
                _ =>
                {
                    var result = SerializeCore(context, eventDatas).ToList();

                    if (_validationMode != ValidationMode.Off)
                    {
                        var deserializedEvents = result.SelectMany(e => DeserializeEvents(e, DateTime.Now)).ToList();
                        AnalyzeEquality(context, eventDatas, deserializedEvents);
                    }

                    return result;
                });
        }

        private static bool Equal(IReadOnlyList<ContentLocationEventData> originalEventDatas, IReadOnlyList<ContentLocationEventData> deserializedEvents)
        {
            // Flatten the event data for comparison
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

            if (left.Kind == EventKind.Blob)
            {
                // Reconcile blobs do not have hashes so just check blob id equality
                var leftReconcile = (BlobContentLocationEventData)left;
                var rightReconcile = (BlobContentLocationEventData)right;
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
                case BlobContentLocationEventData blobEvent:
                    return $"{blobEvent.BlobId}";
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

                builder.AppendLine($"Deserialized event information:");
                foreach (var eventData in deserializedEvents)
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
                    Tracer.Warning(context, $"{i} of {traceCount}: '{builder.ToString(start, length)}'");
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
            var splitLargeInstancesIfNeeded = SplitLargeInstancesIfNeeded(context, eventDatas);

            if (splitLargeInstancesIfNeeded.Count != eventDatas.Count)
            {
                context.TracingContext.Debug($"Split {eventDatas.Count} to {splitLargeInstancesIfNeeded.Count} because of size restrictions.", component: nameof(ContentLocationEventDataSerializer));
            }

            using (_writer.PreservePosition())
            {
                int currentCount = 0;

                while (currentCount < splitLargeInstancesIfNeeded.Count)
                {
                    long oldOffset = _writer.Buffer.Position;

                    splitLargeInstancesIfNeeded[currentCount].Serialize(_writer.Writer);

                    long newOffset = _writer.Buffer.Position;
                    long eventSize = newOffset - oldOffset;

                    Contract.Check(eventSize <= MaxEventDataPayloadSize)?.Assert($"No mitigation for single {splitLargeInstancesIfNeeded[currentCount].Kind} event that is too large");
                    bool isLast = currentCount == (splitLargeInstancesIfNeeded.Count - 1);
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
        public void SerializeEvents(BuildXLWriter writer, IReadOnlyList<ContentLocationEventData> eventDatas)
        {
            SynchronizeIfNeeded(
                _ =>
                {
                    writer.WriteCompact(eventDatas.Count);
                    foreach (var eventData in eventDatas)
                    {
                        eventData.Serialize(writer);
                    }

                    return Unit.Void;
                });
        }

        /// <nodoc />
        public IEnumerable<ContentLocationEventData> DeserializeEvents(BuildXLReader reader)
        {
            return SynchronizeIfNeeded(
                synchronized =>
                {
                    // Need to "materialize" the result if synchronization is needed.
                    // Otherwise the lock will be released before all the data is consumed from the 
                    if (synchronized)
                    {
                        return deserializeEventsCore().ToList();
                    }
                    else
                    {
                        return deserializeEventsCore();
                    }
                });

            IEnumerable<ContentLocationEventData> deserializeEventsCore()
            {
                var entriesCount = reader.ReadInt32Compact();

                for (int i = 0; i < entriesCount; i++)
                {
                    // Using default as eventTimeUtc because reconciliation events should not have touches.
                    yield return ContentLocationEventData.Deserialize(reader, eventTimeUtc: default);
                }
            }
        }

        /// <nodoc />
        public IReadOnlyList<ContentLocationEventData> DeserializeEvents(EventData message, DateTime? eventTimeUtc = null)
        {
            return SynchronizeIfNeeded(
                _ =>
                {
                    if (eventTimeUtc == null)
                    {
                        Contract.Assert(message.SystemProperties != null, "Either eventTimeUtc argument must be provided or message.SystemProperties must not be null. Did you forget to provide eventTimeUtc arguments in tests?");
                        eventTimeUtc = message.SystemProperties.EnqueuedTimeUtc;
                    }

                    var data = message.Body;
                    return _reader.DeserializeSequence(data.AsMemory(), reader => ContentLocationEventData.Deserialize(reader, eventTimeUtc.Value));
                });
        }

        /// <nodoc />
        public static IReadOnlyList<ContentLocationEventData> SplitLargeInstancesIfNeeded(OperationContext context, IReadOnlyList<ContentLocationEventData> source)
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
                        foreach (var entry in source[i].Split(context, MaxEventDataPayloadSize))
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

        private T SynchronizeIfNeeded<T>(Func<bool, T> func)
        {
            if (_synchronize)
            {
                lock (this)
                {
                    return func(true);
                }
            }
            else
            {
                return func(false);
            }
        }
    }
}
