// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Utilities.Collections;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Listing of <see cref="MachineContentEntry"/>s as a contiguous region of memory.
    /// </summary>
    public record ContentListing : IDisposable
    {
        private readonly SafeAllocHHandle _handle;
        private readonly int _offset;
        private int _length;
        private readonly int _fullLength;
        private readonly bool _ownsHandle;

        internal unsafe Span<MachineContentEntry> FullSpanForTests => FullSpan;

        private unsafe Span<MachineContentEntry> FullSpan => new Span<MachineContentEntry>(
            _handle.DangerousGetHandle().ToPointer(),
            _fullLength);

        public Span<MachineContentEntry> EntrySpan => FullSpan.Slice(_offset, _length);

        /// <summary>
        /// Gets the byte span representation of the listing
        /// </summary>
        public Span<byte> Bytes => MemoryMarshal.AsBytes(EntrySpan);

        /// <summary>
        /// Creates a <see cref="ContentListing"/> based on local content info
        /// </summary>
        public ContentListing(IReadOnlyList<ContentInfo> contentInfo, MachineId machineId)
            : this(GetEntries(contentInfo, machineId))
        {
        }

        /// <summary>
        /// Creates a <see cref="ContentListing"/> based on a list of entries
        /// </summary>
        public ContentListing(IReadOnlyList<MachineContentEntry> entries)
            : this(
                  SafeAllocHHandle.Allocate((long)entries.Count * MachineContentEntry.ByteLength),
                  0,
                  entries.Count,
                  ownsHandle: true)
        {
            var entrySpan = EntrySpan;
            for (int i = 0; i < entries.Count; i++)
            {
                entrySpan[i] = entries[i];
            }
        }

        /// <summary>
        /// Creates a projection of the entries as <see cref="MachineContentEntry"/>s.
        /// </summary>
        private static IReadOnlyList<MachineContentEntry> GetEntries(
            IReadOnlyList<ContentInfo> contentInfo,
            MachineId machineId)
        {
            var location = LocationChange.CreateAdd(machineId);
            return contentInfo.SelectList(info =>
                MachineContentEntry.Create(info.ContentHash, location, info.Size, info.LastAccessTimeUtc));
        }

        private ContentListing(SafeAllocHHandle handle, int offset, int length, bool ownsHandle)
        {
            _handle = handle;
            _offset = offset;
            _length = length;
            _fullLength = (int)(_handle.Length / MachineContentEntry.ByteLength);
            _ownsHandle = ownsHandle;
        }

        /// <summary>
        /// Creates an unitialized content listing based on a binary length. Typically intended for download.
        /// </summary>
        public static ContentListing CreateFromByteLength(int byteLength)
        {
            var entryCount = Math.DivRem(byteLength, MachineContentEntry.ByteLength, out var remainder);
            Contract.Check(remainder == 0)?
                .Assert($"Byte length '{byteLength}' must be evenly divisible by entry byte length {MachineContentEntry.ByteLength}");
            return new(SafeAllocHHandle.Allocate(byteLength),
                  0,
                  entryCount,
                  ownsHandle: true);
        }

        /// <summary>
        /// Sorts and deduplicates the listing in place
        /// </summary>
        public void SortAndDeduplicate()
        {
            var entrySpan = EntrySpan;
            if (entrySpan.Length == 0)
            {
                return;
            }

            SpanSortHelper.Sort(entrySpan);

            entrySpan = entrySpan.InPlaceSortedDedupe((e1, e2) =>
            {
                if (e1.Equals(e2))
                {
                    return true;
                }

                return false;
            });
            _length = entrySpan.Length;
        }

        /// <summary>
        /// Gets a slice of the listing based on the <paramref name="entryStart"/> and <paramref name="entryCount"/>.
        /// </summary>
        public ContentListing GetSlice(int entryStart, int entryCount)
        {
            return new ContentListing(_handle, entryStart, entryCount, ownsHandle: false);
        }

        /// <summary>
        /// Enumerates the partition slices from the listing
        /// </summary>
        public IEnumerable<ContentListing> GetPartitionSlices(IEnumerable<PartitionId> ids)
        {
            int start = 0;
            foreach (var partitionId in ids)
            {
                var partition = GetPartitionContentSlice(partitionId, ref start);
                yield return partition;
            }
        }

        /// <summary>
        /// Gets the slice of the full listing containing the partition's content.
        /// </summary>
        private ContentListing GetPartitionContentSlice(PartitionId partitionId, ref int start)
        {
            var entrySpan = EntrySpan;
            for (int i = start; i < entrySpan.Length; i++)
            {
                var entry = entrySpan[i];
                if (!partitionId.Contains(entry.PartitionId))
                {
                    var result = GetSlice(start, i - start);
                    start = i;
                    return result;
                }
            }

            return GetSlice(start, entrySpan.Length - start);
        }


        /// <summary>
        /// Gets an unmanaged stream over the listing.
        /// </summary>
        public StreamWithLength AsStream()
        {
            var stream = new UnmanagedMemoryStream(_handle, _offset * (long)MachineContentEntry.ByteLength, _length * (long)MachineContentEntry.ByteLength, FileAccess.ReadWrite);
            return stream.WithLength(stream.Length);
        }

        /// <summary>
        /// Enumerate entries in the listing
        /// </summary>
        public IEnumerable<MachineContentEntry> EnumerateEntries()
        {
            var length = EntrySpan.Length;
            if (length == 0)
            {
                yield break;
            }

            var pointer = new UnsafePointer<MachineContentEntry>(ref EntrySpan[0]);

            for (int i = 0; i < length; i++)
            {
                if (i != 0)
                {
                    pointer.Increment();
                }

                yield return pointer.Value;
            }

        }

        /// <summary>
        /// Computes the difference from this listing to <paramref name="nextEntries"/>.
        /// </summary>
        public IEnumerable<MachineContentEntry> EnumerateChanges(
            IEnumerable<MachineContentEntry> nextEntries,
            BoxRef<DiffContentStatistics> counters,
            bool synthesizeUniqueHashEntries)
        {
            counters ??= new DiffContentStatistics();
            foreach (var diff in NuCacheCollectionUtilities.DistinctMergeSorted(EnumerateEntries(), nextEntries, i => i, i => i))
            {
                if (diff.mode == MergeMode.LeftOnly)
                {
                    counters.Value.Removes.Add(diff.left);
                    yield return diff.left with { Location = diff.left.Location.AsRemove() };
                }
                else
                {
                    var entry = diff.Either();
                    bool isUnique = counters.Value.Total.Add(entry);
                    if (diff.mode == MergeMode.RightOnly)
                    {
                        counters.Value.Adds.Add(entry, isUnique);
                        yield return diff.right;
                    }
                    else if (isUnique && synthesizeUniqueHashEntries)
                    {
                        // Synthesize fake entry for hash
                        // This ensures that touches happen even when no content adds/removes
                        // have happened for the hash.
                        // NOTE: This should not be written to the database.
                        yield return new MachineContentEntry() with { ShardHash = entry.ShardHash };
                    }
                }
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"Range=[{_offset}, {_offset + _length}) EntryCount={_length} ByteLength={MemoryMarshal.AsBytes(EntrySpan).Length}";
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_ownsHandle)
            {
                _handle.Dispose();
            }
        }
    }

    /// <summary>
    /// Counters for <see cref="ContentListing.EnumerateChanges"/>
    /// </summary>
    public record PartitionUpdateStatistics
    {
        // TODO: Log(size) statistics?
        public DiffContentStatistics DiffStats { get; init; } = new DiffContentStatistics();
    }

    public record ContentStatistics
    {
        public long TotalCount { get; set; }
        public long TotalSize { get; set; }
        public long UniqueCount { get; set; }
        public long UniqueSize { get; set; }
        public ShardHash? First { get; set; }
        public ShardHash? Last { get; set; }
        public int MaxHashFirstByteDifference { get; set; }
        public MachineContentInfo Info = MachineContentInfo.Default;

        public bool Add(MachineContentEntry entry, bool? isUnique = default)
        {
            var last = Last;
            Last = entry.ShardHash;
            First ??= Last;

            Info.Merge(entry);
            var size = entry.Size.Value;

            TotalCount++;
            TotalSize += size;

            isUnique ??= last != Last;
            if (isUnique.Value)
            {
                if (last != null)
                {
                    var bytes1 = MemoryMarshal.AsBytes(stackalloc[] { last.Value });
                    var bytes2 = MemoryMarshal.AsBytes(stackalloc[] { Last.Value });
                    MaxHashFirstByteDifference = Math.Max(MaxHashFirstByteDifference, GetFirstByteDifference(bytes1, bytes2));
                }

                UniqueCount++;
                UniqueSize += size;
            }

            return isUnique.Value;
        }

        private static int GetFirstByteDifference(Span<byte> bytes1, Span<byte> bytes2)
        {
            for (int i = 0; i < bytes1.Length; i++)
            {
                if (bytes1[i] != bytes2[i])
                {
                    return i;
                }
            }

            return bytes1.Length;
        }
    }

    public record DiffContentStatistics()
    {
        public ContentStatistics Adds { get; init; } = new();
        public ContentStatistics Removes { get; init; } = new();
        public ContentStatistics Deletes { get; init; } = new();
        public ContentStatistics Touchs { get; init; } = new();
        public ContentStatistics Total { get; init; } = new();
    }

    /// <summary>
    /// Handle to block of memory allocated by <see cref="Marshal.AllocHGlobal"/>
    /// </summary>
    internal sealed class SafeAllocHHandle : SafeBuffer
    {
        public SafeAllocHHandle() : base(true) { }

        private SafeAllocHHandle(IntPtr handle, long length) : base(true)
        {
            SetHandle(handle);
            Length = length;
        }

        /// <summary>
        /// Allocate a block of memory and return the handle.
        /// </summary>
        public static SafeAllocHHandle Allocate(long length)
        {
            var result = new SafeAllocHHandle(Marshal.AllocHGlobal((IntPtr)length), length);
            result.Initialize((ulong)length);
            return result;
        }

        internal static SafeAllocHHandle InvalidHandle
        {
            get { return new SafeAllocHHandle(IntPtr.Zero, 0); }
        }

        /// <summary>
        /// The length of the block of memory
        /// </summary>
        public long Length { get; }

        /// <inheritdoc />
        protected override bool ReleaseHandle()
        {
            if (handle != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(handle);
            }

            return true;
        }
    }

    /// <summary>
    /// Unsafe pointer to a typed blittable entry in a block of memory
    /// </summary>
    public unsafe class UnsafePointer<T>
    {
        private void* _ptr;
        public UnsafePointer(ref T location)
        {
            _ptr = Unsafe.AsPointer(ref location);
        }

        public T Value => Unsafe.AsRef<T>(_ptr);

        public void Increment()
        {
            _ptr = Unsafe.Add<T>(_ptr, 1);
        }
    }
}
