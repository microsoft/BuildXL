// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;
using BuildXL.Utilities.Serialization;

#nullable enable annotations

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Represents an entry of content on a single machine. Can represent presence of the content or a removal.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public record struct MachineContentEntry(
            ShardHash ShardHash,
            LocationChange Location,
            CompactSize Size,
            CompactTime AccessTime)
            : IComparable<MachineContentEntry>, IEquatable<MachineContentEntry>
    {
        /// <summary>
        /// Get the length of the binary representation of an <see cref="MachineContentEntry"/>
        /// </summary>
        public static int ByteLength { get; } = Unsafe.SizeOf<MachineContentEntry>();

        /// <summary>
        /// Gets the partition id based on the <see cref="ShardHash"/>.
        /// </summary>
        public byte PartitionId => ShardHash[0];

        public ShortHash Hash => ShardHash.ToShortHash();

        public static MachineContentEntry Create(
            ShortHash hash,
            LocationChange location,
            long size,
            DateTime accessTime)
        {
            return new MachineContentEntry(new ShardHash(hash), location, size, accessTime);
        }

        public int CompareTo(MachineContentEntry other)
        {
            return ShardHash.ChainCompareTo(other.ShardHash)
                ?? Location.CompareTo(other.Location);
        }

        public bool Equals(MachineContentEntry other)
        {
            return CompareTo(other) == 0;
        }

        public override int GetHashCode()
        {
            return (ShardHash, Location).GetHashCode();
        }
    }

    /// <summary>
    /// Represents an add/presence of content or a remove/absence.
    /// </summary>
    public record struct LocationChange(ushort Value) : IComparable<LocationChange>
    {
        private const ushort RemoveBit = 1 << 15;
        private const ushort RemoveBitMask = unchecked((ushort)~RemoveBit);

        /// <summary>
        /// True if the location for a machine with index <see cref="Index"/> was removed.
        /// </summary>
        public bool IsRemove => (Value & RemoveBit) != 0;

        /// <summary>
        /// True if the location for a machine with index <see cref="Index"/> was added.
        /// </summary>
        public bool IsAdd => (Value & RemoveBit) == 0;

        /// <summary>
        /// Returns true if the instance is valid.
        /// </summary>
        /// <remarks>
        /// The property returns false if created with a default constructor.
        /// </remarks>
        public bool IsValid => Value != 0;

        /// <summary>
        /// Gets the machine index for a changed location.
        /// </summary>
        public int Index => Value & RemoveBitMask;

        /// <summary>
        /// Converts the current instance to <see cref="MachineId"/>.
        /// </summary>
        public MachineId AsMachineId()
        {
            return new MachineId(Index);
        }

        /// <summary>
        /// Creates a location add event.
        /// </summary>
        public static LocationChange CreateAdd(MachineId machine)
        {
            return Create(machine, isRemove: false);
        }

        /// <summary>
        /// Creates a location remove event.
        /// </summary>
        public static LocationChange CreateRemove(MachineId machine)
        {
            return Create(machine, isRemove: true);
        }

        /// <nodoc />
        public static LocationChange Create(MachineId machine, bool isRemove)
        {
            var value = unchecked((ushort)(isRemove ? (RemoveBit | machine.Index) : machine.Index));
            return new LocationChange(value);
        }

        /// <nodoc />
        public LocationChange AsRemove()
        {
            return new LocationChange(unchecked((ushort)(RemoveBit | Value)));
        }

        /// <nodoc />
        public LocationChange AsAdd()
        {
            return new LocationChange(unchecked((ushort)Index));
        }

        /// <inheritdoc />
        public int CompareTo(LocationChange other)
        {
            return Index.ChainCompareTo(other.Index) ?? Value.CompareTo(other.Value);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if (IsAdd)
            {
                return $"Add({Index})";
            }

            if (IsRemove)
            {
                return $"Remove({Index})";
            }

            return "Invalid";
        }
    }

    /// <summary>
    /// Represents the size of content in 6 bytes vs 8 bytes for long.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 6)]
    public readonly record struct CompactSize : IComparable<CompactSize>
    {
        public long Value
        {
            get
            {
                var value = MemoryMarshal.Read<long>(MemoryMarshal.AsBytes(stackalloc[] { this, default }));
                return value;
            }
        }

        public int CompareTo(CompactSize other)
        {
            return Value.CompareTo(other.Value);
        }

        public static implicit operator CompactSize(long size)
        {
            return MemoryMarshal.Read<CompactSize>(MemoryMarshal.AsBytes(stackalloc[] { size }));
        }
    }

    /// <summary>
    /// Same as <see cref="ShortHash"/> but hash type is last byte rather than first byte. For use in cases,
    /// where the hash will be sorted based on its binary representation.
    /// </summary>
    public readonly struct ShardHash : IComparable<ShardHash>, IEquatable<ShardHash>
    {
        private readonly ShortReadOnlyFixedBytes _bytes;

        public byte this[int index] => _bytes[index];

        private ShardHash(ShortReadOnlyFixedBytes bytes)
        {
            _bytes = bytes;
        }

        public ShardHash(ShortHash hash)
        {
            var hashBytes = MemoryMarshal.AsBytes(stackalloc[] { hash, hash });

            // Read from offset 1 so that hash type byte is last byte
            _bytes = MemoryMarshal.Read<ShortReadOnlyFixedBytes>(hashBytes.Slice(1));
        }

        public ShortHash ToShortHash()
        {
            var hashBytes = MemoryMarshal.AsBytes(stackalloc[] { _bytes, _bytes });

            // Read from offset 1 so that hash type byte is last byte
            return MemoryMarshal.Read<ShortHash>(hashBytes.Slice(ShortHash.SerializedLength - 1));
        }

        /// <inheritdoc />
        public int CompareTo(ShardHash other)
        {
            return _bytes.CompareTo(other._bytes);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return ToShortHash().ToString();
        }

        /// <inheritdoc />
        public bool Equals(ShardHash other)
        {
            return _bytes.Equals(other._bytes);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return _bytes.GetHashCode();
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <nodoc />
        public static bool operator ==(ShardHash left, ShardHash right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(ShardHash left, ShardHash right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Optional content information. The data format is [Size?, LatestAccessTime?, EarliestAccessTime?].
    /// Properties are only written if they are not the default value.
    /// </summary>
    public record struct MachineContentInfo
    {
        public static readonly MachineContentInfo Default = new MachineContentInfo();

        /// <nodoc />
        public MachineContentInfo() : this(null)
        {
        }

        /// <nodoc />
        public MachineContentInfo(long? size, CompactTime? latestAccessTime = null, CompactTime? earliestAccessTime = null)
        {
            Size = size;
            if (latestAccessTime != null)
            {
                UpdateAccessTimes(latestAccessTime.Value.Value);
            }

            if (earliestAccessTime != null)
            {
                UpdateAccessTimes(earliestAccessTime.Value.Value);
            }
        }

        /// <summary>
        /// The size of the entry if available
        /// </summary>
        public long? Size
        {
            get => _size >= 0 ? _size : null;
            set => _size = value ?? -1;
        }

        /// <summary>
        /// The last access time if available
        /// </summary>
        public CompactTime? LatestAccessTime
        {
            get => _latestAccessTime != DefaultInvalidLatestAccessTime ? new CompactTime(_latestAccessTime) : null;
        }

        /// <summary>
        /// The earliest access time if available. When entries are fully merged
        /// this represents the creation time.
        /// </summary>
        public CompactTime? EarliestAccessTime
        {
            get => _earliestAccessTime != DefaultInvalidEarliestTime ? new CompactTime(_earliestAccessTime) : null;
        }

        /// <summary>
        /// Updates the access times with the new value.
        /// </summary>
        private void UpdateAccessTimes(uint accessTimeValue)
        {
            if (accessTimeValue != DefaultInvalidLatestAccessTime && accessTimeValue != DefaultInvalidEarliestTime)
            {
                _latestAccessTime = Math.Max(accessTimeValue, _latestAccessTime);
                _earliestAccessTime = Math.Min(accessTimeValue, _earliestAccessTime);
            }
        }

        /// <summary>
        /// Earliest access time is set to uint.MaxValue as default to
        /// ensure when it is merged with real time (via Math.Min) the real
        /// time wins
        /// </summary>
        private const uint DefaultInvalidEarliestTime = uint.MaxValue;

        /// <summary>
        /// Latest access time is set to uint.MinValue as default to
        /// ensure when it is merged with real time (via Math.Mas) the real
        /// time wins
        /// </summary>
        private const uint DefaultInvalidLatestAccessTime = uint.MinValue;

        private long _size = -1;
        private uint _latestAccessTime = DefaultInvalidLatestAccessTime;
        private uint _earliestAccessTime = DefaultInvalidEarliestTime;

        /// <summary>
        /// Diffs the two <see cref="MachineContentInfo"/> instances returning a difference
        /// </summary>
        public static MachineContentInfo Diff(MachineContentInfo baseline, MachineContentInfo current)
        {
            var diff = new MachineContentInfo();
            if (current._earliestAccessTime < baseline._earliestAccessTime)
            {
                diff.UpdateAccessTimes(current._earliestAccessTime);
            }

            if (current._latestAccessTime > baseline._latestAccessTime)
            {
                diff.UpdateAccessTimes(current._latestAccessTime);
            }

            if (current._size > baseline._size)
            {
                diff._size = current._size;
            }

            return diff;
        }

        /// <summary>
        /// Merges the two entries into a new entry
        /// </summary>
        public static MachineContentInfo Merge(MachineContentInfo info1, MachineContentInfo info2)
        {
            info1.Merge(info2);
            return info1;
        }

        /// <summary>
        /// Merges the information of the given entry into this instance
        /// </summary>
        public void Merge(MachineContentInfo other)
        {
            _size = Math.Max(_size, other._size);
            if (other._earliestAccessTime != DefaultInvalidEarliestTime)
            {
                UpdateAccessTimes(other._earliestAccessTime);
            }

            if (other._latestAccessTime != DefaultInvalidLatestAccessTime)
            {
                UpdateAccessTimes(other._earliestAccessTime);
            }
        }

        /// <summary>
        /// Merges the information of the given entry into this instance
        /// </summary>
        public void Merge(MachineContentEntry entry)
        {
            _size = Math.Max(_size, entry.Size.Value);
            if (entry.AccessTime.Value != 0)
            {
                UpdateAccessTimes(entry.AccessTime.Value);
            }
        }

        /// <summary>
        /// Deserializes the info from a span.
        /// NOTE: The entry is expected to appear at the end of
        /// the span. If entry needs to have other data after, it should
        /// be serialized with a length prefix.
        /// </summary>
        public static MachineContentInfo Read(ref SpanReader reader)
        {
            var result = new MachineContentInfo();
            result.ReadFrom(ref reader);
            return result;
        }

        private void ReadFrom(ref SpanReader reader)
        {
            if (!reader.IsEnd)
            {
                _latestAccessTime = reader.ReadUInt32Compact();
                _earliestAccessTime = _latestAccessTime;
                if (!reader.IsEnd)
                {
                    _size = reader.ReadInt64Compact() - 1;
                    if (!reader.IsEnd)
                    {
                        _earliestAccessTime = reader.ReadUInt32Compact();
                    }
                }
            }

            Contract.Check(reader.IsEnd)?.Assert(
                $"Machine content info is expected to appear at end of span, but span has {reader.RemainingLength} bytes remaining.");
        }

        /// <summary>
        /// Write the fields only if they are not the default and prior fields are serialized.
        /// If size is the default value, it is forced to be serialized if latestAccessTime is specified since fields
        /// can only be serialized if earlier fields are present
        ///
        /// Entry can take form:
        /// Touch:  [LatestAccessTime]
        /// Add:    [LatestAccessTime, Size, CreationTime?]
        /// Remove: []
        /// </summary>
        public void WriteTo(ref SpanWriter writer)
        {
            if (_latestAccessTime != DefaultInvalidLatestAccessTime || _size >= 0)
            {
                writer.WriteUInt32Compact(_latestAccessTime);

                bool mustSerializationEarliestTime = _earliestAccessTime != DefaultInvalidEarliestTime && _earliestAccessTime != _latestAccessTime;
                var finalSize = Math.Max(_size, -1) + 1;
                if (finalSize > 0 || mustSerializationEarliestTime)
                {
                    writer.WriteInt64Compact(finalSize);

                    if (mustSerializationEarliestTime)
                    {
                        writer.WriteUInt32Compact(_earliestAccessTime);
                    }
                }
            }
        }
    }
}
