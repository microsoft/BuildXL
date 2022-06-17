// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Serialization;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Represents a set of machine ids that contains a content.
    /// </summary>
    public sealed class BitMachineIdSet : MachineIdSet
    {
        private int _count = -1;

        /// <nodoc />
        protected override SetFormat Format => SetFormat.Bits;

        /// <summary>
        /// Returns an empty machine set.
        /// </summary>
        internal static readonly BitMachineIdSet EmptyInstance = new BitMachineIdSet(Array.Empty<byte>(), 0);

        /// <summary>
        /// Bitmask used to determine location ID.
        /// </summary>
        internal const byte MaxCharBitMask = 0x80;

        /// <summary>
        /// Binary mask where each bit represents a machine with an available content.
        /// </summary>
        internal byte[] Data { get; }

        /// <summary>
        /// Offset in the <see cref="Data"/> for a bit mask.
        /// </summary>
        internal int Offset { get; }

        /// <summary>
        /// Returns true if a machine id set is empty.
        /// </summary>
        public override bool IsEmpty => Data.Length == 0;

        /// <nodoc />
        public BitMachineIdSet(byte[] data, int offset)
        {
            Contract.Requires(data != null);

            Data = data;
            Offset = offset;
        }

        /// <nodoc />
        public static BitMachineIdSet Create(in MachineIdCollection machines) => EmptyInstance.SetExistenceBits(machines, exists: true);

        /// <nodoc />
        public static BitMachineIdSet Create(in IEnumerable<MachineId> machines) => EmptyInstance.SetExistenceBits(MachineIdCollection.Create(machines.ToArray()), exists: true);

        /// <summary>
        /// Returns the bit value at position index.
        /// </summary>
        public override bool this[int index] => GetValue(Data, Offset, index);

        /// <summary>
        /// Gets the number of machine locations.
        /// </summary>
        // Caching the count for performance reasons because we can access it multiple times for the same instance.
        public override int Count => _count == -1 ? (_count = Bits.BitCount(Data, Offset)) : _count;

        /// <summary>
        /// Returns a new instance of <see cref="MachineIdSet"/> based on the given <paramref name="machines"/> and <paramref name="exists"/>.
        /// </summary>
        public override MachineIdSet SetExistence(in MachineIdCollection machines, bool exists)
        {
            return SetExistenceBits(machines, exists);
        }

        /// <summary>
        /// Returns a new instance of <see cref="BitMachineIdSet"/> based on the given <paramref name="machine"/> and <paramref name="exists"/>.
        /// </summary>
        public BitMachineIdSet SetExistenceBit(MachineId machine, bool exists)
        {
            if (this[machine] == exists)
            {
                return this;
            }
            else
            {
                return SetExistenceBits(machine, exists);
            }
        }

        /// <summary>
        /// Returns a new instance of <see cref="BitMachineIdSet"/> based on the given <paramref name="machines"/> and <paramref name="exists"/>.
        /// </summary>
        public BitMachineIdSet SetExistenceBits(in MachineIdCollection machines, bool exists)
        {
            if (machines.Count == 0)
            {
                return this;
            }

            var max = (machines.MaxIndex() / 8) + 1;
            var copiedLength = Data.Length - Offset;

            const int targetOffset = 0;
            var data = new byte[Math.Max(max, copiedLength)];
            Array.Copy(Data, Offset, data, targetOffset, copiedLength);

            foreach (var machine in machines)
            {
                SetValue(data, targetOffset, machine.Index, exists);
            }

            return new BitMachineIdSet(data, targetOffset);
        }

        /// <nodoc />
        protected override void SerializeCore(BuildXLWriter writer)
        {
            var count = Data.Length - Offset;

            // Use variable length encoding
            writer.WriteCompact(count);
            writer.Write(Data, Offset, count);
        }

        internal static MachineIdSet DeserializeCore(BuildXLReader reader)
        {
            var count = reader.ReadInt32Compact();

            var data = reader.ReadBytes(count);

            return new BitMachineIdSet(data, 0);
        }

        internal static MachineIdSet DeserializeCore(ref SpanReader reader)
        {
            var count = reader.ReadInt32Compact();

            var data = reader.ReadBytes(count);

            return new BitMachineIdSet(data, 0);
        }

        internal static bool HasMachineIdCore(ReadOnlySpan<byte> source, int index)
        {
            var reader = source.AsReader();
            var count = reader.ReadInt32Compact();

            var data = reader.ReadSpan(count);

            return GetValue(data, 0, index);
        }

        /// <inheritdoc />
        public override IEnumerable<MachineId> EnumerateMachineIds()
        {
            for (int i = Offset; i < Data.Length; i++)
            {
                byte redisChar = Data[i];

                int position = 0;
                while (redisChar != 0)
                {
                    if ((redisChar & MaxCharBitMask) != 0)
                    {
                        yield return new MachineId(((i - Offset) * 8) + position);
                    }

                    redisChar <<= 1;
                    position++;
                }
            }
        }

        /// <inheritdoc />
        public override int GetMachineIdIndex(MachineId currentMachineId)
        {
            int dataIndex = Offset + currentMachineId.Index / 8;
            if (dataIndex >= Data.Length)
            {
                return -1;
            }

            int machineIdIndex = 0;
            byte redisChar;
            for (int i = Offset; i < dataIndex; i++)
            {
                redisChar = Data[i];
                if (redisChar != 0)
                {
                    machineIdIndex += Bits.BitCount(redisChar);
                }
            }

            // The bit mask uses the most significant bits to specify lower machine ids.
            // It means that 0b10000000 should return 0-th machine Id index.
            var dataBitPosition = (currentMachineId.Index % 8);
            redisChar = Data[dataIndex];
            int position = 0;
            while (redisChar != 0)
            {
                if ((redisChar & MaxCharBitMask) != 0)
                {
                    if (position == dataBitPosition)
                    {
                        return machineIdIndex;
                    }

                    machineIdIndex++;
                }

                redisChar <<= 1;
                position++;
            }

            return -1;
        }

        /// <nodoc />
        public static bool GetValue(ReadOnlySpan<byte> data, int offset, int index)
        {
            int dataIndex = offset + index / 8;

            if (dataIndex >= data.Length)
            {
                return false;
            }

            // The bit mask uses the most significant bits to specify lower machine ids.
            // It means that 0b10000010 is translated into machines 0 and 6, but not 1 and 7.
            return (data[dataIndex] & (1 << (7 - (index % 8)))) != 0;
        }
        
        private static void SetValue(byte[] data, int offset, int index, bool value)
        {
            int dataIndex = offset + index / 8;

            Contract.Check(dataIndex < data.Length)?.Assert($"data.Length={data.Length}, offset={offset}, index={index}, value={value}");

            var bitPosition = 7 - (index % 8);
            unchecked
            {
                if (value)
                {
                    data[dataIndex] |= (byte)(1 << bitPosition);
                }
                else
                {
                    data[dataIndex] &= (byte)(~(1 << bitPosition));
                }
            }
        }
    }
}
