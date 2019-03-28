// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Text;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming
{
    /// <summary>
    /// Type of an event happened in the system.
    /// </summary>
    public enum EventKind
    {
        /// <nodoc />
        AddLocation,

        /// <nodoc />
        RemoveLocation,

        /// <nodoc />
        Touch,

        /// <nodoc />
        Reconcile,
    }

    /// <summary>
    /// Base class that describe the event.
    /// </summary>
    public abstract class ContentLocationEventData : IEquatable<ContentLocationEventData>
    {
        // Need some wiggle room for auxiliary instance data needed for assessing serialized instance size.
        private const int SerializedSizeBase = 128;

        /// <summary>
        /// Indicates whether an Add/Remove event is related to reconciliation.
        /// NOTE: This is not (de)serialized, but is determined when specifically handling a reconciliation event.
        /// </summary>
        public bool Reconciling { get; internal set; }

        /// <nodoc />
        public EventKind Kind { get; }

        /// <summary>
        /// A current machine id.
        /// </summary>
        public MachineId Sender { get; }

        /// <summary>
        /// A list of affected content hashes.
        /// </summary>
        /// <remarks>
        /// The list is not null or empty.
        /// </remarks>
        public IReadOnlyList<ShortHash> ContentHashes { get; }

        /// <nodoc />
        protected ContentLocationEventData(EventKind kind, MachineId sender, IReadOnlyList<ShortHash> contentHashes)
        {
            Contract.Requires(contentHashes != null);
            Contract.RequiresDebug(contentHashes.Count != 0); // We want to detect this precondition in tests/debug mode, but don't want to break in prod because of this.

            Kind = kind;
            Sender = sender;
            ContentHashes = contentHashes;
        }

        /// <nodoc />
        public static ContentLocationEventData Deserialize(BuildXLReader reader, DateTime eventTimeUtc)
        {
            Contract.Requires(reader != null);

            var kind = (EventKind)reader.ReadByte();
            var sender = MachineId.Deserialize(reader);
            var hashes = reader.ReadReadOnlyList(r => r.ReadShortHash());

            switch (kind)
            {
                case EventKind.AddLocation:
                    return new AddContentLocationEventData(sender, hashes, reader.ReadReadOnlyList(r => r.ReadInt64Compact()));
                case EventKind.RemoveLocation:
                    return new RemoveContentLocationEventData(sender, hashes);
                case EventKind.Touch:
                    return new TouchContentLocationEventData(sender, hashes, eventTimeUtc);
                case EventKind.Reconcile:
                    return new ReconcileContentLocationEventData(sender, reader.ReadString());
                default:
                    throw new ArgumentOutOfRangeException($"Unknown event kind '{kind}'.");
            }
        }

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write((byte)Kind);
            Sender.Serialize(writer);
            writer.WriteReadOnlyList(ContentHashes, (w, hash) => w.Write(hash));

            switch (this) {
                case AddContentLocationEventData addContentLocationEventData:
                    writer.WriteReadOnlyList(addContentLocationEventData.ContentSizes, (w, size) => w.WriteCompact(size));
                    break;
                case RemoveContentLocationEventData removeContentLocationEventData:
                case TouchContentLocationEventData touchContentLocationEventData:
                    // Do nothing. No extra data. Touch timestamp is taken from event enqueue time
                    break;
                case ReconcileContentLocationEventData reconcileContentLocationEventData:
                    writer.Write(reconcileContentLocationEventData.BlobId);
                    break;
            }
        }

        /// <summary>
        /// Returns an estimated size of the instance
        /// </summary>
        public long EstimateSerializedInstanceSize()
        {
            switch (this)
            {
                case AddContentLocationEventData addContentLocationEventData:
                    return SerializedSizeBase + ContentHashes.Count * ShortHash.SerializedLength + addContentLocationEventData.ContentSizes.Count * sizeof(long);
                default:
                    return SerializedSizeBase + ContentHashes.Count * ShortHash.SerializedLength;
            }
        }

        /// <summary>
        /// Splits the current instance into a smaller instances
        /// </summary>
        public IReadOnlyList<ContentLocationEventData> Split(long maxEstimatedSize)
        {
            // First, need to compute the number of hashes that will fit into maxEstimatedSize.
            var maxHashCount = MaxContentHashesCount(maxEstimatedSize);

            // Then we need to split the instance into a sequence of instances.
            var hashes = ContentHashes.Split(maxHashCount).ToList();
            var result = new List<ContentLocationEventData>(hashes.Count);

            Contract.Assert(hashes.Sum(c => c.Count) == ContentHashes.Count);

            switch (this)
            {
                case AddContentLocationEventData addContentLocationEventData:
                    var contentSizes = addContentLocationEventData.ContentSizes.Split(maxHashCount).ToList();
                    Contract.Assert(hashes.Count == contentSizes.Count);

                    result.AddRange(hashes.Select((t, index) => new AddContentLocationEventData(Sender, t, contentSizes[index])));
                    break;
                case RemoveContentLocationEventData _:
                    result.AddRange(hashes.Select(t => new RemoveContentLocationEventData(Sender, t)));
                    break;
                case TouchContentLocationEventData touchContentLocationEventData:
                    result.AddRange(hashes.Select(t => new TouchContentLocationEventData(Sender, t, touchContentLocationEventData.AccessTime)));
                    break;
            }

            Contract.AssertDebug(result.TrueForAll(v => v.EstimateSerializedInstanceSize() < maxEstimatedSize));

            return result;

            int MaxContentHashesCount(long estimatedSize)
            {
                switch (this)
                {
                    case AddContentLocationEventData _:
                        return (int)(estimatedSize - SerializedSizeBase) / (ShortHash.SerializedLength + sizeof(long));
                    default:
                        return (int)(estimatedSize - SerializedSizeBase) / (ShortHash.SerializedLength);
                }
            }
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return EqualityComparer<ContentLocationEventData>.Default.Equals(this, obj as ContentLocationEventData);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (Kind.GetHashCode(), Sender.GetHashCode(), ContentHashes.SequenceHashCode()).GetHashCode();
        }

        /// <nodoc />
        public virtual bool Equals(ContentLocationEventData other)
        {
            return Kind == other.Kind && Sender == other.Sender && ContentHashes.SequenceEqual(other.ContentHashes);
        }
    }

    /// <nodoc />
    public sealed class AddContentLocationEventData : ContentLocationEventData
    {
        /// <summary>
        /// A list of content sizes associated with <see cref="ContentLocationEventData.ContentHashes"/>.
        /// </summary>
        public IReadOnlyList<long> ContentSizes { get; }

        /// <nodoc />
        public AddContentLocationEventData(MachineId sender, IReadOnlyList<ShortHashWithSize> addedContent)
            : this(sender, addedContent.SelectList(c => c.Hash), addedContent.SelectList(c => c.Size))
        {
        }

        /// <nodoc />
        public AddContentLocationEventData(MachineId sender, IReadOnlyList<ShortHash> contentHashes, IReadOnlyList<long> contentSizes)
            : base(EventKind.AddLocation, sender, contentHashes)
        {
            Contract.Requires(contentSizes != null);
            Contract.Requires(contentSizes.Count == contentHashes.Count);

            ContentSizes = contentSizes;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var sb = new StringBuilder();
            for(int i = 0; i < ContentHashes.Count; i++)
            {
                sb.Append($"Hash={ContentHashes[i]}, Size={ContentSizes[i]}");
            }

            return $"Event: {Kind}, Sender: {Sender}, {sb}";
        }

        /// <inheritdoc />
        public override bool Equals(ContentLocationEventData other)
        {
            var rhs = (AddContentLocationEventData)other;
            return base.Equals(other) && ContentSizes.SequenceEqual(rhs.ContentSizes);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (base.GetHashCode(), ContentSizes.GetHashCode()).GetHashCode();
        }
    }

    /// <nodoc />
    public sealed class RemoveContentLocationEventData : ContentLocationEventData
    {
        /// <nodoc />
        public RemoveContentLocationEventData(MachineId sender, IReadOnlyList<ShortHash> contentHashes)
            : base(EventKind.RemoveLocation, sender, contentHashes)
        {
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"Event: {Kind}, Sender: {Sender}, {string.Join(", ", ContentHashes)}";
        }
    }

    /// <nodoc />
    public sealed class ReconcileContentLocationEventData : ContentLocationEventData
    {
        /// <summary>
        /// The blob identifier of the reconciliation blob
        /// </summary>
        public string BlobId { get; }

        /// <nodoc />
        public ReconcileContentLocationEventData(MachineId sender, string blobId)
            : base(EventKind.Reconcile, sender, CollectionUtilities.EmptyArray<ShortHash>())
        {
            BlobId = blobId;
        }
    }

    /// <nodoc />
    public sealed class TouchContentLocationEventData : ContentLocationEventData
    {
        /// <nodoc />
        public TouchContentLocationEventData(MachineId sender, IReadOnlyList<ShortHash> contentHashes, DateTime accessTime)
            : base(EventKind.Touch, sender, contentHashes)
        {
            AccessTime = accessTime;
        }

        /// <summary>
        /// Date and time when the content was accessed for the last time.
        /// </summary>
        public DateTime AccessTime { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{base.ToString()}, AccessTime: {AccessTime}";
        }

        /// <inheritdoc />
        public override bool Equals(ContentLocationEventData other)
        {
            var otherTouch = (TouchContentLocationEventData)other;
            return base.Equals(other) && AccessTime == otherTouch.AccessTime;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (base.GetHashCode(), AccessTime.GetHashCode()).GetHashCode();
        }

    }
}
