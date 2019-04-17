// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
    }
}
