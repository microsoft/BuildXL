// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Threading;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Base non-generic concurrent big set containing definition of state which is item type agnostic
    /// </summary>
    public abstract class ConcurrentBigSet
    {
        /// <summary>
        /// Encapsulates logic for manipulating buckets.
        /// </summary>
        protected sealed class Buckets
        {
            /// <summary>
            /// Each bucket entry is 4 bytes; let's try to keep the entry buffers outside of the large object heap.
            /// </summary>
            private const int ItemsPerEntryBufferBitWidth = 12;

            /// <summary>
            /// Indicates that bucket does not have a head node index set
            /// </summary>
            public const int InvalidHeadNodeIndex = -1;

            /// <summary>
            /// Indicates that the bucket has a pending split which will populate its head node and
            /// lookups/modifications for the bucket should be routed to the pre-split bucket number
            /// until the split is completed.
            /// </summary>
            private const int PendingSplitHeadNodeIndex = -2;

            /// <summary>
            /// Indicates that there is a split operation in progress.
            /// </summary>
            private const int SPLITTING_TRUE = 1;

            /// <summary>
            /// Indicates that there is NOT a split operation in progress.
            /// </summary>
            private const int SPLITTING_FALSE = 0;

            /// <summary>
            /// Stores pointers into nodes array indicating the first node in the bucket
            /// </summary>
            private readonly BigBuffer<int> m_buckets;

            /// <summary>
            /// The index of the next bucket to split. If outside the range [0, m_lastSplitBucketIndex], then there are no buckets to split.
            /// </summary>
            private int m_splitBucketCursor = -1;

            /// <summary>
            /// The number of buckets remaining to be split. This should only be decremented by thread which has completed
            /// splitting a bucket. It is set when the split operation is initiated.
            /// </summary>
            private int m_pendingSplitBucketCount;

            /// <summary>
            /// The index of the last valid bucket in the buckets buffer. This is always 2^n - 1 (for some n) and is valid as
            /// a bit mask for selecting the bucket number from
            /// </summary>
            private readonly int m_lastBucketIndex;

            /// <summary>
            /// The index of the last valid bucket prior to the split operation which doubles the number of buckets
            /// </summary>
            private readonly int m_preSplitBucketsLength;

            /// <summary>
            /// Buffer initializer for additional pages, once we are splitting
            /// </summary>
            private readonly BigBuffer<int>.BufferInitializer m_bucketsBufferInitializer = InitializeBucketBufferToPendingSplitHeadNodeIndices;

            /// <summary>
            /// This value is set to TRUE or FALSE depending on whether a split is in progress or not.
            /// </summary>
            public int IsSplitting;

            /// <summary>
            /// This number specifies a threshold on count after which a split will be initiated. This number attempts to maintain that the number of
            /// items / buckets = ratio.
            /// </summary>
            public readonly int SplitThreshold;

            /// <summary>
            /// The number of allocated buckets
            /// </summary>
            public int Capacity => m_lastBucketIndex + 1;

            /// <summary>
            /// Class constructor
            /// </summary>
            public Buckets(int capacity, int ratio, BigBuffer<int>.BufferInitializer bucketsBufferInitializer = null, bool initializeSequentially = false)
            {
                Contract.Requires(capacity > 0);
                var buckets = new BigBuffer<int>(ItemsPerEntryBufferBitWidth, 1);
                capacity = buckets.Initialize(capacity, bucketsBufferInitializer ?? InitializeBucketBufferToInvalidHeadNodeIndices, initializeSequentially);
                m_buckets = buckets;
                Contract.Assume(((capacity - 1) & capacity) == 0, "capacity must be a power of two");
                m_lastBucketIndex = capacity - 1;
                m_preSplitBucketsLength = capacity;
                SplitThreshold = checked(capacity * ratio);
                m_splitBucketCursor = int.MinValue;
            }

            private Buckets(BigBuffer<int> buckets, int lastBucketIndex, int preSplitBucketsLength, int splitThreshold)
            {
                m_buckets = buckets;
                m_lastBucketIndex = lastBucketIndex;
                m_preSplitBucketsLength = preSplitBucketsLength;
                m_pendingSplitBucketCount = preSplitBucketsLength;
                IsSplitting = SPLITTING_TRUE;
                SplitThreshold = splitThreshold;
                m_splitBucketCursor = -1;
            }

            private Buckets(BigBuffer<int>.BufferInitializer bucketsBufferInitializer, int capacity, int lastBucketIndex, int pendingSplitBucketCount, int preSplitBucketsLength, int isSplitting, int splitThreshold, int splitBucketCursor)
            {
                Contract.Requires(bucketsBufferInitializer != null);

                var buckets = new BigBuffer<int>(ItemsPerEntryBufferBitWidth, 1);
                buckets.Initialize(capacity, bucketsBufferInitializer, initializeSequentially: true);
                m_buckets = buckets;
                m_lastBucketIndex = lastBucketIndex;
                m_preSplitBucketsLength = preSplitBucketsLength;
                m_pendingSplitBucketCount = pendingSplitBucketCount;
                IsSplitting = isSplitting;
                SplitThreshold = splitThreshold;
                m_splitBucketCursor = splitBucketCursor;
            }

            /// <summary>
            /// Writes bucket.
            /// </summary>
            /// <remarks>
            /// This method is not threadsafe.
            /// </remarks>
            public void Serialize(BinaryWriter writer)
            {
                Contract.Requires(writer != null);
                int capacity = m_buckets.Capacity;
                int lastBucketIndex = m_lastBucketIndex;
                int pendingSplitBucketCount = m_pendingSplitBucketCount;
                int preSplitBucketsLength = m_preSplitBucketsLength;
                int isSplitting = IsSplitting;
                int splitThreshold = SplitThreshold;
                int splitBucketCursor = m_splitBucketCursor;

                writer.Write(capacity);
                writer.Write(lastBucketIndex);
                writer.Write(pendingSplitBucketCount);
                writer.Write(preSplitBucketsLength);
                writer.Write(isSplitting);
                writer.Write(splitThreshold);
                writer.Write(splitBucketCursor);

                for (int i = 0; i < capacity; i++)
                {
                    var value = m_buckets[i];
                    writer.Write(value);
                }
            }

            /// <summary>
            /// Creates a new instance by deserializing.
            /// </summary>
            public static Buckets Deserialize(BinaryReader reader)
            {
                Contract.Requires(reader != null);

                int capacity = reader.ReadInt32();
                int lastBucketIndex = reader.ReadInt32();
                int pendingSplitBucketCount = reader.ReadInt32();
                int preSplitBucketsLength = reader.ReadInt32();
                int isSplitting = reader.ReadInt32();
                int splitThreshold = reader.ReadInt32();
                int splitBucketCursor = reader.ReadInt32();

                byte[] buffer = null;
                var buckets = new Buckets(
                    bucketsBufferInitializer:
                        (bufferStart, bufferCount) =>
                        {
                            if (bufferStart + bufferCount > capacity)
                            {
                                throw new IOException("Unsupported format");
                            }

                            return reader.ReadInt32Array(ref buffer, bufferCount);
                        },
                    capacity: capacity,
                    lastBucketIndex: lastBucketIndex,
                    pendingSplitBucketCount: pendingSplitBucketCount,
                    preSplitBucketsLength: preSplitBucketsLength,
                    isSplitting: isSplitting,
                    splitThreshold: splitThreshold,
                    splitBucketCursor: splitBucketCursor);

                return buckets;
            }

            /// <summary>
            /// Gets or sets the head node index of the bucket at the given index
            /// </summary>
            /// <remarks>
            /// When splitting the index may represent a bucket that has not yet been created so this will
            /// redirect to the pre-split bucket in that case.
            /// </remarks>
            public int this[int index]
            {
                get
                {
                    if (Volatile.Read(ref IsSplitting) == SPLITTING_TRUE)
                    {
                        if (index >= m_buckets.Capacity)
                        {
                            // Index points toa  bucket which hasn't been created yet.
                            // Reroute to pre-split bucket.
                            index -= m_preSplitBucketsLength;
                            return m_buckets[index];
                        }

                        var headNodeIndex = m_buckets[index];
                        if (headNodeIndex == PendingSplitHeadNodeIndex)
                        {
                            // Index points to a bucket which hasn't been initialized yet
                            // with content from split bucket.
                            // Reroute to pre-split bucket.
                            index -= m_preSplitBucketsLength;
                            return m_buckets[index];
                        }

                        return headNodeIndex;
                    }

                    return m_buckets[index];
                }

                set
                {
                    if (Volatile.Read(ref IsSplitting) == SPLITTING_TRUE)
                    {
                        if (index >= m_buckets.Capacity)
                        {
                            index -= m_preSplitBucketsLength;
                            m_buckets[index] = value;
                            return;
                        }

                        var headNodeIndex = m_buckets[index];
                        if (headNodeIndex == PendingSplitHeadNodeIndex)
                        {
                            index -= m_preSplitBucketsLength;
                            m_buckets[index] = value;
                            return;
                        }
                    }

                    m_buckets[index] = value;
                }
            }

            /// <summary>
            /// Gets or sets the head node index of the bucket at the given index
            /// </summary>
            public void SetBucketHeadNodeIndexDirect(int index, int headNodeIndex)
            {
                m_buckets[index] = headNodeIndex;
            }

            /// <summary>
            /// Grows the buckets if the count is above the split threshold
            /// </summary>
            public static bool GrowIfNecessary(ref Buckets buckets, int count)
            {
                var oldBuckets = buckets;

                // If necessary, trigger a split.
                if (count >= oldBuckets.SplitThreshold)
                {
                    if (Interlocked.CompareExchange(ref oldBuckets.IsSplitting, SPLITTING_TRUE, SPLITTING_FALSE) == SPLITTING_FALSE)
                    {
                        int preSplitBucketsLength = buckets.m_lastBucketIndex + 1;
                        int newLastBucketIndex = (buckets.m_lastBucketIndex << 1) + 1;
                        int newSplitThreshold = buckets.SplitThreshold << 1;
                        var newBuckets = new Buckets(buckets.m_buckets, newLastBucketIndex, preSplitBucketsLength, newSplitThreshold);
                        if (Interlocked.CompareExchange(ref buckets, newBuckets, oldBuckets) == oldBuckets)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            /// <summary>
            /// Indicates that a single bucket has finished its split operation and terminates splitting if
            /// all buckets are completed
            /// </summary>
            public void EndBucketSplit()
            {
                if (Interlocked.Decrement(ref m_pendingSplitBucketCount) == 0)
                {
                    Volatile.Write(ref IsSplitting, SPLITTING_FALSE);
                }
            }

            /// <summary>
            /// Attempts to get the next bucket that requires splitting if splitting is active
            /// </summary>
            public bool TryGetBucketToSplit(out int bucketToSplitNo, out int targetBucketNo)
            {
                targetBucketNo = -1;
                bucketToSplitNo = -1;

                if (Volatile.Read(ref IsSplitting) == SPLITTING_TRUE)
                {
                    bucketToSplitNo = Interlocked.Increment(ref m_splitBucketCursor);
                    bool isValidBucketToSplit = unchecked((uint)bucketToSplitNo < (uint)m_preSplitBucketsLength);
                    if (isValidBucketToSplit)
                    {
                        targetBucketNo = bucketToSplitNo + m_preSplitBucketsLength;
                        m_buckets.Initialize(targetBucketNo + 1, m_bucketsBufferInitializer);
                        return true;
                    }
                }

                return false;
            }

            private static int[] InitializeBucketBufferToInvalidHeadNodeIndices(int start, int count)
            {
                // we are initializing for the first time
                int[] entryBuffer = new int[count];
                for (int i = 0; i < entryBuffer.Length; i++)
                {
                    entryBuffer[i] = InvalidHeadNodeIndex;
                }

                return entryBuffer;
            }

            private static int[] InitializeBucketBufferToPendingSplitHeadNodeIndices(int start, int count)
            {
                // Everything after the initial set of buffers needs to be set to Bucket.PendingSplit so
                // seeks to that buckets nodes will get redirected to the pre-split bucket
                int[] entryBuffer = new int[count];
                for (int i = 0; i < entryBuffer.Length; i++)
                {
                    entryBuffer[i] = PendingSplitHeadNodeIndex;
                }

                return entryBuffer;
            }

            /// <summary>
            /// Gets the bucket number reverting to the bucket which must be split to create the bucket if the
            /// actual bucket has not been allocated yet
            /// </summary>
            public int GetBucketNo(int bucketHashCode)
            {
                return bucketHashCode & m_lastBucketIndex;
            }
        }

        /// <summary>
        /// A node in a singly-linked list representing a particular hash table bucket.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        protected readonly struct Node
        {
            /// <summary>
            /// Indicates an unused node
            /// Top bit is not used in computed hashcode. See GetBucketHashCode.
            /// </summary>
            public const int UnusedHashCode = 1 << 31;

            internal readonly int Next;
            internal readonly int Hashcode;

            internal Node(int hashcode, int next)
            {
                Next = next;
                Hashcode = hashcode;
            }

            /// <inheritdoc />
            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture, "HashCode: '{0}' Next: '{1}'", Hashcode, Next);
            }
        }
    }
}
