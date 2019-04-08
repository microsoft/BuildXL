// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Represents a set of machine ids that contains a content.
    /// </summary>
    public sealed class ArrayMachineIdSet : MachineIdSet
    {
        /// <nodoc />
        protected override SetFormat Format => SetFormat.Array;

        /// <summary>
        /// The indices of the machine ids represented by this set
        /// </summary>
        private readonly HashSet<ushort> _machineIds;

        /// <summary>
        /// Returns true if a machine id set is empty.
        /// </summary>
        public override bool IsEmpty => Count == 0;

        /// <nodoc />
        public ArrayMachineIdSet(ushort[] machineIds)
        {
            _machineIds = new HashSet<ushort>(machineIds);
        }

        /// <nodoc />
        private ArrayMachineIdSet(HashSet<ushort> machineIds)
        {
            _machineIds = machineIds;
        }

        /// <summary>
        /// Returns the bit value at position index.
        /// </summary>
        public override bool this[int index] => _machineIds.Contains((ushort)index);

        /// <summary>
        /// Gets the number of machine locations.
        /// </summary>
        public override int Count => _machineIds.Count;

        /// <summary>
        /// Returns a new instance of <see cref="MachineIdSet"/> based on the given <paramref name="machines"/> and <paramref name="exists"/>.
        /// </summary>
        public override MachineIdSet SetExistence(IReadOnlyCollection<MachineId> machines, bool exists)
        {
            Contract.Requires(machines != null);

            if (machines.Count == 0)
            {
                return this;
            }

            var machineIds = new HashSet<ushort>(_machineIds);
            foreach (var machine in machines)
            {
                if (exists)
                {
                    machineIds.Add((ushort)machine.Index);
                }
                else
                {
                    machineIds.Remove((ushort)machine.Index);
                }
            }

            return new ArrayMachineIdSet(machineIds);
        }

        /// <summary>
        /// Enumerates the bits in the machine id set
        /// </summary>
        public override IEnumerable<MachineId> EnumerateMachineIds()
        {
            foreach (var machineId in _machineIds)
            {
                yield return new MachineId(machineId);
            }
        }

        /// <nodoc />
        protected override void SerializeCore(BuildXLWriter writer)
        {
            // Use variable length encoding
            writer.WriteCompact(Count);
            foreach (var machineId in _machineIds)
            {
                // Use variable length encoding?
                writer.WriteCompact((int)machineId);
            }
        }

        internal static MachineIdSet DeserializeCore(BuildXLReader reader)
        {
            // Use variable length encoding
            var count = reader.ReadInt32Compact();
            var machineIds = new HashSet<ushort>();

            for (int i = 0; i < count; i++)
            {
                machineIds.Add((ushort)reader.ReadInt32Compact());
            }

            return new ArrayMachineIdSet(machineIds);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"Count: {Count}";
        }
    }
}
