// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities.Collections;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// A memory efficient collection of <see cref="MachineId"/>.
    /// </summary>
    /// <remarks>
    /// This is effectively a union type between a single machine id and a list of machine ids.
    /// </remarks>
    public readonly struct MachineIdCollection : IEnumerable<MachineId>
    {
        private readonly MachineId _singleMachineId;
        private readonly IReadOnlyList<MachineId> _machineIds;

        private MachineIdCollection(MachineId singleMachineId)
            : this() =>
            _singleMachineId = singleMachineId;

        private MachineIdCollection(IReadOnlyList<MachineId> machineIds)
            : this() =>
            _machineIds = machineIds;

        /// <nodoc />
        public static MachineIdCollection Empty { get; } = new MachineIdCollection(CollectionUtilities.EmptyArray<MachineId>());

        /// <summary>
        /// Create a collection of machine ids with a single location.
        /// </summary>
        public static MachineIdCollection Create(MachineId machineId) => new MachineIdCollection(machineId);

        /// <summary>
        /// Creates a collection of machine ids with a list of locations.
        /// </summary>
        public static MachineIdCollection Create(IReadOnlyList<MachineId> machineIds) => new MachineIdCollection(machineIds);

        /// <summary>
        /// Create a collection of machine ids with a single location.
        /// </summary>
        public static implicit operator MachineIdCollection(MachineId machineId) => Create(machineId);

        /// <summary>
        /// Gets a first machine id.
        /// </summary>
        /// <returns></returns>
        public MachineId FirstMachineId() => _machineIds != null ? _machineIds.First() : _singleMachineId;

        /// <summary>
        /// Returns the number of machine locations.
        /// </summary>
        public int Count => _machineIds?.Count ?? 1;

        /// <summary>
        /// Gets the max machine id.
        /// </summary>
        /// <remarks>
        /// The method will fail if the collection is empty.
        /// </remarks>
        public int MaxIndex() => _machineIds != null ? _machineIds.Max(m => m.Index) : _singleMachineId.Index;

        /// <nodoc />
        public MachineIdCollectionEnumerator GetEnumerator()
        {
            return new MachineIdCollectionEnumerator(_singleMachineId, _machineIds);
        }

        /// <inheritdoc />
        IEnumerator<MachineId> IEnumerable<MachineId>.GetEnumerator() => GetEnumerator();

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// An enumerator used by <see cref="MachineIdCollection"/>
        /// </summary>
        public struct MachineIdCollectionEnumerator : IEnumerator<MachineId>
        {
            private readonly MachineId _singleMachineId;
            private readonly IReadOnlyList<MachineId> _machineIds;
            private int _index;

            /// <nodoc />
            public MachineIdCollectionEnumerator(MachineId singleMachineId, IReadOnlyList<MachineId> machineIds)
            {
                _singleMachineId = singleMachineId;
                _machineIds = machineIds;
                _index = -1;
            }

            /// <nodoc />
            public MachineId Current => _machineIds != null ? _machineIds[_index] : _singleMachineId;

            /// <nodoc/>
            public bool MoveNext()
            {
                // Even if the list is null, we still should return true on the first call of this method.
                if (_index + 1 >= (_machineIds?.Count ?? 1))
                {
                    
                    return false;
                }

                _index++;
                return true;
            }

            /// <inheritdoc />
            public void Dispose()
            {
            }

            /// <inheritdoc />
            public void Reset()
            {
            }

            object IEnumerator.Current => Current;
        }
    }
}
