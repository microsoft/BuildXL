// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;
using BuildXL.Cache.ContentStore.Utils;

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
    public record struct LocationChange(short Value) : IComparable<LocationChange>
    {
        private const int RemoveBit = 1 << 15;

        public bool IsRemove => Value < 0;
        public bool IsValid => Value != 0;

        public int Index => (Value & ~RemoveBit);

        public MachineId ToMachineId()
        {
            return new MachineId(Index);
        }

        public static LocationChange CreateAdd(MachineId machine)
        {
            return Create(machine, isRemove: false);
        }

        public static LocationChange CreateRemove(MachineId machine)
        {
            return Create(machine, isRemove: true);
        }

        public static LocationChange Create(MachineId machine, bool isRemove)
        {
            var value = unchecked((short)(isRemove ? (RemoveBit | machine.Index) : machine.Index));
            return new LocationChange(value);
        }

        public LocationChange AsRemove()
        {
            return new LocationChange(unchecked((short)(RemoveBit | Value)));
        }
        public LocationChange AsAdd()
        {
            return new LocationChange(unchecked((short)Index));
        }

        public int CompareTo(LocationChange other)
        {
            return Index.ChainCompareTo(other.Index) ?? Value.CompareTo(other.Value);
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

        public int CompareTo(ShardHash other)
        {
            return _bytes.CompareTo(other._bytes);
        }

        public bool Equals(ShardHash other)
        {
            return _bytes.Equals(other._bytes);
        }

        public override int GetHashCode()
        {
            return _bytes.GetHashCode();
        }
    }
}