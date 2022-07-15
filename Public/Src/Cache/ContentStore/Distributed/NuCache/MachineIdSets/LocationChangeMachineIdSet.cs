// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using BuildXL.Utilities;
using BuildXL.Utilities.Serialization;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Represents an add/presence of content or a remove/absence on a set of machines.
    /// </summary>
    /// <remarks>
    /// This type is used with RocksDb merge operators.
    /// </remarks>
    public sealed class LocationChangeMachineIdSet : MachineIdSet
    {
        // Consider keeping an array of LocationChange
        // The field is intentionally not made readonly to avoid defensive copies accessing
        // non-readonly struct.
        // ushort here should be used as 'LocationChange' instance.
        private ImmutableArray<LocationChange> _locationStates;

        /// <nodoc />
        public static LocationChangeMachineIdSet EmptyInstance { get; } = new (ImmutableArray<LocationChange>.Empty);

        /// <inheritdoc />
        protected override SetFormat Format => SetFormat.LocationChange;

        /// <inheritdoc />
        public override bool IsEmpty => Count == 0;

        /// <inheritdoc />
        public override int Count { get; }

        /// <nodoc />
        internal LocationChangeMachineIdSet(ImmutableArray<LocationChange> machineIds)
        {
            _locationStates = machineIds;

            // Need to count the number of machines with the location information because some of the states
            // represent the location removal events.
            Count = EnumerateMachineIds().Count();
        }

        /// <nodoc />
        public static ArrayMachineIdSet Create(in MachineIdCollection machines) => (ArrayMachineIdSet)EmptyInstance.SetExistence(machines, exists: true);

        /// <nodoc />
        public static ArrayMachineIdSet Create(IEnumerable<MachineId> machines) => (ArrayMachineIdSet)EmptyInstance.SetExistence(MachineIdCollection.Create(machines.ToArray()), exists: true);

        /// <inheritdoc />
        public override bool this[int index]
        {
            get
            {
                // 'isRemove' flag is ignored during the search.
                var inputLocation = LocationChange.Create(new MachineId(index), isRemove: true);
                var arrayIndex = _locationStates.IndexOf(inputLocation, LocationChangeMachineIdComparer.Instance);
                if (arrayIndex == -1)
                {
                    return false;
                }

                return _locationStates[arrayIndex].IsAdd;
            }
        }

        /// <inheritdoc />
        public override MachineIdSet SetExistence(in MachineIdCollection machines, bool exists)
        {
            // There are 3 special cases here:
            return machines.Count switch
            {
                // Nothing has changed
                0 => this,
                // Hot path: changing only one machine id
                1 => SetExistenceForOneMachine(machines.FirstMachineId(), exists),
                // The number of changes is small: using a builder and nested for loops
                _ => SetExistenceWithBuilder(machines, exists),
            };
        }

        private MachineIdSet SetExistenceForOneMachine(MachineId machineId, bool exists)
        {
            var locationStates = _locationStates;

            // In IndexOf the 'IsRemove' property will be ignored.
            var locationChange = LocationChange.Create(machineId, isRemove: !exists);
            var arrayIndex = _locationStates.IndexOf(locationChange, LocationChangeMachineIdComparer.Instance);
            if (arrayIndex != -1)
            {
                locationStates = locationStates.RemoveAt(arrayIndex);
            }

            locationStates = locationStates.Add(locationChange);

            return new LocationChangeMachineIdSet(locationStates);
        }

        private MachineIdSet SetExistenceWithBuilder(in MachineIdCollection machines, bool exists)
        {
            ImmutableArray<LocationChange>.Builder builder = null;

            foreach (var machineId in machines)
            {
                builder ??= _locationStates.ToBuilder();

                // isRemove flag has no impact on the search, because LocationChangeMachineIdComparer ignores that
                // and only compares the Index.
                var inputLocation = LocationChange.Create(machineId, isRemove: true);
                var arrayIndex = builder.IndexOf(inputLocation, 0, builder.Count, LocationChangeMachineIdComparer.Instance);
                if (arrayIndex != -1)
                {
                    builder.RemoveAt(arrayIndex);
                }

                builder.Add(LocationChange.Create(machineId, isRemove: !exists));
            }

            return builder != null ? new LocationChangeMachineIdSet(builder.ToImmutable()) : this;
        }

        /// <summary>
        /// Gets all the location changes of the current instance.
        /// </summary>
        public ImmutableArray<LocationChange> LocationStates => _locationStates;

        /// <inheritdoc />
        public override IEnumerable<MachineId> EnumerateMachineIds()
        {
            foreach (var locationState in _locationStates)
            {
                var locationChange = locationState;
                if (locationChange.IsAdd)
                {
                    yield return locationChange.AsMachineId();
                }
            }
        }

        /// <inheritdoc />
        public override int GetMachineIdIndex(MachineId currentMachineId)
        {
            int index = 0;
            foreach (var locationState in _locationStates)
            {
                if (locationState.AsMachineId() == currentMachineId)
                {
                    return index;
                }

                index++;
            }

            return -1;
        }

        /// <inheritdoc />
        protected override void SerializeCore(BuildXLWriter writer)
        {
            // Use variable length encoding
            writer.WriteCompact(_locationStates.Length);
            foreach (var locationChange in _locationStates)
            {
                // Use variable length encoding?
                writer.Write(unchecked((ushort)locationChange.Value));
            }
        }

        internal static MachineIdSet DeserializeCore(BuildXLReader reader)
        {
            // Use variable length encoding
            var count = reader.ReadInt32Compact();
            var machineIds = new LocationChange[count];

            for (int i = 0; i < count; i++)
            {
                machineIds[i] = new LocationChange(reader.ReadUInt16());
            }

            // For small number of elements, it is more efficient (in terms of memory)
            // to use a simple array and just search the id using sequential scan.

            // Using unsafe trick to create an instance of immutable array without copying a source array.
            // This is a semi-official trick "suggested" by the CLR architect here: https://github.com/dotnet/runtime/issues/25461
            ImmutableArray<LocationChange> immutableMachineIds = Unsafe.As<LocationChange[], ImmutableArray<LocationChange>>(ref machineIds);
            return new LocationChangeMachineIdSet(immutableMachineIds);
        }

        internal static MachineIdSet DeserializeCore(ref SpanReader reader)
        {
            // Use variable length encoding
            var count = reader.ReadInt32Compact();
            var machineIds = new LocationChange[count];

            for (int i = 0; i < count; i++)
            {
                machineIds[i] = new LocationChange(reader.ReadUInt16());
            }

            // For small number of elements, it is more efficient (in terms of memory)
            // to use a simple array and just search the id using sequential scan.

            // Using unsafe trick to create an instance of immutable array without copying a source array.
            // This is a semi-official trick "suggested" by the CLR architect here: https://github.com/dotnet/runtime/issues/25461
            ImmutableArray<LocationChange> immutableMachineIds = Unsafe.As<LocationChange[], ImmutableArray<LocationChange>>(ref machineIds);
            return new LocationChangeMachineIdSet(immutableMachineIds);
        }

        internal static bool HasMachineIdCore(ReadOnlySpan<byte> data, int index)
        {
            var reader = data.AsReader();
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

        /// <summary>
        /// A special comparer for <see cref="short"/> that compares instance of <see cref="LocationChange"/> and compares indices not direct values.
        /// </summary>
        private class LocationChangeMachineIdComparer : IEqualityComparer<LocationChange>
        {
            public static LocationChangeMachineIdComparer Instance { get; } = new LocationChangeMachineIdComparer();

            /// <inheritdoc />
            public bool Equals(LocationChange x, LocationChange y)
            {
                return x.Index == y.Index;
            }

            /// <inheritdoc />
            public int GetHashCode(LocationChange obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}
