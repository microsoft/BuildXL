// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Represents a set of machine ids that contains a content based on a hash set.
    /// </summary>
    public sealed class HashSetMachineIdSet : ArrayMachineIdSetBase
    {
        /// <summary>
        /// The indices of the machine ids represented by this set
        /// </summary>
        private readonly HashSet<ushort> _machineIds;

        /// <inheritdoc />
        public override bool IsEmpty => Count == 0;

        /// <nodoc />
        public HashSetMachineIdSet(ushort[] machineIds)
        {
            _machineIds = new HashSet<ushort>(machineIds);
        }

        /// <nodoc />
        internal HashSetMachineIdSet(HashSet<ushort> machineIds)
        {
            _machineIds = machineIds;
        }

        /// <inheritdoc />
        public override bool this[int index] => _machineIds.Contains((ushort)index);

        /// <inheritdoc />
        public override int Count => _machineIds.Count;

        /// <inheritdoc />
        protected override IEnumerable<ushort> EnumerateRawMachineIds()
        {
            foreach (var machineId in _machineIds)
            {
                yield return machineId;
            }
        }
    }
}
