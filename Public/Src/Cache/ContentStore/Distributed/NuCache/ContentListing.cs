// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Utilities.Collections;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Listing of <see cref="MachineContentEntry"/>s as a contiguous region of memory.
    /// </summary>
    public record ContentListing : IDisposable
    {
        internal const int PartitionCount = 256;

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
        public IEnumerable<ContentListing> GetPartitionSlices()
        {
            int start = 0;
            for (int i = 0; i < PartitionCount; i++)
            {
                var partitionId = (byte)i;
                var partition = GetPartitionContentSlice(partitionId, ref start);
                yield return partition;
            }
        }

        /// <summary>
        /// Gets the slice of the full listing containing the partition's content.
        /// </summary>
        private ContentListing GetPartitionContentSlice(byte partitionId, ref int start)
        {
            var entrySpan = EntrySpan;
            for (int i = start; i < entrySpan.Length; i++)
            {
                var entry = entrySpan[i];
                if (entry.PartitionId != partitionId)
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
        public Stream AsStream()
        {
            return new UnmanagedMemoryStream(_handle, _offset * (long)MachineContentEntry.ByteLength, _length * (long)MachineContentEntry.ByteLength, FileAccess.ReadWrite);
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
        public IEnumerable<MachineContentEntry> EnumerateChanges(IEnumerable<MachineContentEntry> nextEntries,
            PartitionChangeCounters counters = null)
        {
            counters ??= new PartitionChangeCounters();
            foreach (var diff in NuCacheCollectionUtilities.DistinctDiffSorted(EnumerateEntries(), nextEntries, i => i))
            {
                if (diff.mode == MergeMode.LeftOnly)
                {
                    counters.Removes++;
                    counters.RemoveContentSize += diff.item.Size.Value;
                    yield return diff.item with { Location = diff.item.Location.AsRemove() };
                }
                else
                {
                    counters.Adds++;
                    counters.AddContentSize += diff.item.Size.Value;
                    Contract.Assert(!diff.item.Location.IsRemove);
                    yield return diff.item;
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
    public record PartitionChangeCounters
    {
        public long Adds;
        public long Removes;
        public long AddContentSize;
        public long RemoveContentSize;
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
