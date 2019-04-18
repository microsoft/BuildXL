// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Represents a set of machine ids that contains a content based on a collection in memory.
    /// </summary>
    public abstract class ArrayMachineIdSetBase : MachineIdSet
    {
        /// <summary>
        /// The threshold when array instance is used instead of an instance based on a hash set.
        /// </summary>
        public const int ArrayThreshold = 20;

        /// <inheritdoc />
        protected override SetFormat Format => SetFormat.Array;

        /// <nodoc />
        protected ArrayMachineIdSetBase()
        {
        }

        /// <inheritdoc />
        public override MachineIdSet SetExistence(IReadOnlyCollection<MachineId> machines, bool exists)
        {
            Contract.Requires(machines != null);

            if (machines.Count == 0)
            {
                return this;
            }

            var machineIds = new HashSet<ushort>(EnumerateRawMachineIds());
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

            return machineIds.Count <= ArrayThreshold ? (MachineIdSet)new ArrayMachineIdSet(machineIds.ToArray()) : new HashSetMachineIdSet(machineIds);
        }

        /// <inheritdoc />
        public override IEnumerable<MachineId> EnumerateMachineIds()
        {
            foreach (var machineId in EnumerateRawMachineIds())
            {
                yield return new MachineId(machineId);
            }
        }

        /// <nodoc />
        protected abstract IEnumerable<ushort> EnumerateRawMachineIds();

        /// <inheritdoc />
        protected override void SerializeCore(BuildXLWriter writer)
        {
            // Use variable length encoding
            writer.WriteCompact(Count);
            foreach (var machineId in EnumerateRawMachineIds())
            {
                // Use variable length encoding?
                writer.WriteCompact((int)machineId);
            }
        }

        internal static MachineIdSet DeserializeCore(BuildXLReader reader)
        {
            // Use variable length encoding
            var count = reader.ReadInt32Compact();
            var machineIds = new ushort[count];

            for (int i = 0; i < count; i++)
            {
                machineIds[i] = (ushort)reader.ReadInt32Compact();
            }

            // For small number of elements, it is more efficient (in terms of memory)
            // to use a simple array and just search the id using sequential scan.
            return count <= ArrayThreshold ? (MachineIdSet)new ArrayMachineIdSet(machineIds) : new HashSetMachineIdSet(machineIds);
        }


        internal static bool HasMachineIdCore(BuildXLReader reader, int index)
        {
            var count = reader.ReadInt32Compact();

            for (int i = 0; i < count; i++)
            {
                var machineId = (ushort)reader.ReadInt32Compact();
                if (machineId == index)
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"Count: {Count}";
        }
    }
}
