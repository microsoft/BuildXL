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
    /// Represents a set of machine ids that contains a content based on a plain array of machine ids.
    /// </summary>
    public sealed class ArrayMachineIdSet : MachineIdSet
    {
        // The field is intentionally not made readonly to avoid defensive copies accessing
        // non-readonly struct.
        private ImmutableArray<ushort> _machineIds;

        /// <nodoc />
        public static ArrayMachineIdSet EmptyInstance { get; } = new ArrayMachineIdSet(ImmutableArray<ushort>.Empty);

        /// <inheritdoc />
        protected override SetFormat Format => SetFormat.Array;

        /// <inheritdoc />
        public override bool IsEmpty => Count == 0;

        /// <nodoc />
        public ArrayMachineIdSet(IEnumerable<ushort> sequence)
        {
            // Need to provide the count in CreateBuilder method in order to use
            // MoveToImmutable.
            // In this case we potentially enumerate the sequence twice but we won't materialize it.
            var ab = ImmutableArray.CreateBuilder<ushort>(sequence.Count());
            ab.AddRange(sequence);

            _machineIds = ab.MoveToImmutable();
        }

        /// <nodoc />
        internal ArrayMachineIdSet(ImmutableArray<ushort> machineIds)
        {
            _machineIds = machineIds;
        }

        /// <nodoc />
        public static ArrayMachineIdSet Create(in MachineIdCollection machines) => (ArrayMachineIdSet)EmptyInstance.SetExistence(machines, exists: true);

        /// <nodoc />
        public static ArrayMachineIdSet Create(in IEnumerable<MachineId> machines) => (ArrayMachineIdSet)EmptyInstance.SetExistence(MachineIdCollection.Create(machines.ToArray()), exists: true);

        /// <inheritdoc />
        public override bool this[int index] => _machineIds.Contains((ushort)index);

        /// <inheritdoc />
        public override int Count => _machineIds.Length;

        /// <inheritdoc />
        /// <inheritdoc />
        public override MachineIdSet SetExistence(in MachineIdCollection machines, bool exists)
        {
            // There are 4 special cases here:
            return machines.Count switch
            {
                // Nothing has changed
                0 => this,
                // Hot path: changing only one machine id
                1 => SetExistenceForOneMachine(machines.FirstMachineId(), exists),
                // The number of changes is small: using a builder and nested for loops
                int n when n < 10 => SetExistenceWithBuilder(machines, exists),
                // The number of changes is relatively high: using a hashset
                // to avoid quadratic complexity
                _ => SetExistenceWithHashSet(machines, exists),
            };
        }

        private ArrayMachineIdSet SetExistenceForOneMachine(MachineId machineId, bool exists)
        {
            var machines = _machineIds;
            if (exists)
            {
                if (!this[machineId])
                {
                    machines = machines.Add((ushort)machineId.Index);
                }
            }
            else
            {
                if (this[machineId])
                {
                    machines = machines.Remove((ushort)machineId.Index);
                }
            }

            return new ArrayMachineIdSet(machines);
        }

        private MachineIdSet SetExistenceWithBuilder(in MachineIdCollection machines, bool exists)
        {
            Lazy<ImmutableArray<ushort>.Builder> lazyBuilder = new Lazy<ImmutableArray<ushort>.Builder>(() => _machineIds.ToBuilder());

            foreach (var machineId in machines)
            {
                if (exists)
                {
                    if (!this[machineId])
                    {
                        lazyBuilder.Value.Add((ushort)machineId.Index);
                    }
                }
                else
                {
                    if (this[machineId])
                    {
                        lazyBuilder.Value.Remove((ushort)machineId.Index);
                    }
                }
            }

            return lazyBuilder.IsValueCreated ? new ArrayMachineIdSet(lazyBuilder.Value.ToImmutable()) : this;
        }

        private MachineIdSet SetExistenceWithHashSet(in MachineIdCollection machines, bool exists)
        {
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

            return new ArrayMachineIdSet(machineIds.ToImmutableArray());
        }

        /// <inheritdoc />
        public override IEnumerable<MachineId> EnumerateMachineIds()
        {
            foreach (var machineId in _machineIds)
            {
                yield return new MachineId(machineId);
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

        /// <inheritdoc />
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
            var machineIds = new ushort[count];

            for (int i = 0; i < count; i++)
            {
                machineIds[i] = (ushort)reader.ReadInt32Compact();
            }

            // For small number of elements, it is more efficient (in terms of memory)
            // to use a simple array and just search the id using sequential scan.

            // Using unsafe trick to create an instance of immutable array without copying a source array.
            // This is a semi-official trick "suggested" by the CLR architect here: https://github.com/dotnet/runtime/issues/25461
            ImmutableArray<ushort> immutableMachineIds = Unsafe.As<ushort[], ImmutableArray<ushort>>(ref machineIds);
            return new ArrayMachineIdSet(immutableMachineIds);
        }

        internal static MachineIdSet DeserializeCore(ref SpanReader reader)
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

            // Using unsafe trick to create an instance of immutable array without copying a source array.
            // This is a semi-official trick "suggested" by the CLR architect here: https://github.com/dotnet/runtime/issues/25461
            ImmutableArray<ushort> immutableMachineIds = Unsafe.As<ushort[], ImmutableArray<ushort>>(ref machineIds);
            return new ArrayMachineIdSet(immutableMachineIds);
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
    }
}
