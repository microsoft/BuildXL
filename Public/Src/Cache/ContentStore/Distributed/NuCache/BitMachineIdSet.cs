// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Represents a set of machine ids that contains a content.
    /// </summary>
    public sealed class BitMachineIdSet : MachineIdSet
    {
        /// <nodoc />
        protected override SetFormat Format => SetFormat.Bits;

        /// <summary>
        /// Returns an empty machine set.
        /// </summary>
        internal static readonly BitMachineIdSet EmptyInstance = new BitMachineIdSet(new byte[0], 0);

        /// <summary>
        /// Bitmask used to determine location ID.
        /// </summary>
        internal const byte MaxCharBitMask = 0x80;

        // TODO: consider switching to Span<T> (bug 1365340)

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

        /// <summary>
        /// Returns the bit value at position index.
        /// </summary>
        public override bool this[int index] => GetValue(Data, Offset, index);

        /// <summary>
        /// Gets the number of machine locations.
        /// </summary>
        public override int Count => Bits.BitCount(Data, Offset);

        /// <summary>
        /// Returns a new instance of <see cref="MachineIdSet"/> based on the given <paramref name="machines"/> and <paramref name="exists"/>.
        /// </summary>
        public override MachineIdSet SetExistence(IReadOnlyCollection<MachineId> machines, bool exists)
        {
            Contract.Requires(machines != null);

            return SetExistenceBits(machines, exists);
        }

        /// <summary>
        /// Returns a new instance of <see cref="BitMachineIdSet"/> based on the given <paramref name="machines"/> and <paramref name="exists"/>.
        /// </summary>
        public BitMachineIdSet SetExistenceBits(IReadOnlyCollection<MachineId> machines, bool exists)
        {
            if (machines.Count == 0)
            {
                return this;
            }

            var max = (machines.Max(m => m.Index) / 8) + 1;
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

        /// <summary>
        /// Enumerates the bits in the machine id set
        /// </summary>
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
        public override string ToString()
        {
            return $"Count: {Count}";
        }

        private static bool GetValue(byte[] data, int offset, int index)
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

            if (dataIndex >= data.Length)
            {
                Contract.Assert(false, $"data.Length={data.Length}, offset={offset}, index={index}, value={value}");
                return;
            }

            var bitPosition = 7 - (index % 8);
            unchecked {
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
