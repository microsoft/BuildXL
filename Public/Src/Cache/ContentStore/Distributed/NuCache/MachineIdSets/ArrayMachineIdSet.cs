// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Represents a set of machine ids that contains a content based on a flat list of machine ids.
    /// </summary>
    public class ArrayMachineIdSet : ArrayMachineIdSetBase
    {
        /// <summary>
        /// The indices of the machine ids represented by this set.
        /// </summary>
        private readonly ushort[] _machineIds;

        /// <inheritdoc />
        public override bool IsEmpty => Count == 0;

        /// <nodoc />
        public ArrayMachineIdSet(ushort[] machineIds)
        {
            _machineIds = machineIds;
        }

        /// <inheritdoc />
        public override bool this[int index] => _machineIds.Contains((ushort)index);

        /// <inheritdoc />
        public override int Count => _machineIds.Length;

        /// <inheritdoc />
        protected override IEnumerable<ushort> EnumerateRawMachineIds()
        {
            foreach (var id in _machineIds)
            {
                yield return id;
            }
        }

        /// <inheritdoc />
        public override int GetMachineIdIndex(MachineId currentMachineId)
        {
            int index = 0;
            foreach (var machineId in _machineIds)
            {
                if (new MachineId(machineId) == currentMachineId)
                {
                    return index;
                }

                index++;
            }

            return -1;
        }
    }
}
