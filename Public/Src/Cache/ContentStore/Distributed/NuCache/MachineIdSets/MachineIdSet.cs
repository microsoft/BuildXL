// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Serialization;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Represents a set of machine ids that contains a content.
    /// </summary>
    /// <remarks>
    /// The type and all of the derived types are immutable.
    /// </remarks>
    public abstract class MachineIdSet : IReadOnlyCollection<MachineId>
    {
        private static readonly ObjectPool<List<MachineId>> MachineIdListPool = new ObjectPool<List<MachineId>>(
            () => new List<MachineId>(),
            ids => ids.Clear());

        /// <nodoc />
        public const int BitMachineIdSetThreshold = 100;

        /// <summary>
        /// Returns an empty machine set.
        /// </summary>
        /// <remarks>
        /// Using a property getter instead of a field to avoid static initialization issues that can cause NRE or contract violations because the field can still be null
        /// in some very rare cases.
        /// </remarks>
        public static MachineIdSet Empty => ArrayMachineIdSet.EmptyInstance;

        /// <summary>
        /// Creates a list of machine ids that represents additions or removals of the content on them.
        /// </summary>
        public static MachineIdSet Create(bool exists, params MachineId[] machineIds)
        {
            if (machineIds.Length == 0)
            {
                return Empty;
            }

            return exists
                ? Empty.Add(machineIds)
                : LocationChangeMachineIdSet.EmptyInstance.SetExistence(MachineIdCollection.Create(machineIds), false);
        }

        /// <summary>
        /// Returns the format of a machine id set.
        /// </summary>
        protected abstract SetFormat Format { get; }

        /// <summary>
        /// Returns true if a machine id set is empty.
        /// </summary>
        public abstract bool IsEmpty { get; }

        /// <summary>
        /// Returns the bit value at position index for the machine id.
        /// </summary>
        public bool this[MachineId id] => this[id.Index];

        /// <summary>
        /// Returns the bit value at position index.
        /// </summary>
        public abstract bool this[int index] { get; }

        /// <summary>
        /// Gets the number of machine locations.
        /// </summary>
        public abstract int Count { get; }

        /// <summary>
        /// Returns a new instance of <see cref="MachineIdSet"/> based on the given <paramref name="machines"/> and <paramref name="exists"/>.
        /// </summary>
        public abstract MachineIdSet SetExistence(in MachineIdCollection machines, bool exists);

        /// <summary>
        /// Creates a new instance of the machine mid set with the machine existence or non-existence "flag" for a given <paramref name="machineId"/>.
        /// </summary>
        public MachineIdSet SetExistence(MachineId machineId, bool exists) => SetExistence(MachineIdCollection.Create(machineId), exists);

        /// <summary>
        /// Merges two machine id sets together.
        /// </summary>
        public MachineIdSet Merge(MachineIdSet other)
        {
            if (other is LocationChangeMachineIdSet locationChanges)
            {
                using var pooledAdditions = MachineIdListPool.GetInstance();
                using var pooledRemovals = MachineIdListPool.GetInstance();

                foreach (var lc in locationChanges.LocationStates)
                {
                    if (lc.IsAdd)
                    {
                        pooledAdditions.Instance.Add(lc.AsMachineId());
                    }
                    else
                    {
                        pooledRemovals.Instance.Add(lc.AsMachineId());
                    }
                }

                var additions = MachineIdCollection.Create(pooledAdditions.Instance);
                var removals = MachineIdCollection.Create(pooledRemovals.Instance);

                return SetExistence(additions, exists: true).SetExistence(removals, exists: false);
            }

            return SetExistence(MachineIdCollection.Create(other.ToArray()), exists: true);
        }

        /// <nodoc />
        public MachineIdSet Add(params MachineId[] machines) => SetExistence(MachineIdCollection.Create(machines), exists: true);

        /// <nodoc />
        public MachineIdSet Add(MachineId machine) => SetExistence(MachineIdCollection.Create(machine), exists: true);

        /// <nodoc />
        public MachineIdSet Remove(params MachineId[] machines) => SetExistence(MachineIdCollection.Create(machines), exists: false);

        /// <nodoc />
        public MachineIdSet Remove(MachineId machine) => SetExistence(MachineIdCollection.Create(machine), exists: false);

        /// <nodoc />
        public bool Contains(MachineId machine) => this[machine];

        /// <summary>
        /// Enumerates the bits in the machine id set
        /// </summary>
        public abstract IEnumerable<MachineId> EnumerateMachineIds();

        /// <summary>
        /// Returns a position of the <paramref name="currentMachineId"/> in the current machine id list.
        /// </summary>
        /// <returns>-1 if the given id is not part of the machine id list.</returns>
        /// <remarks>
        /// This method can be implemented on top of <see cref="EnumerateMachineIds"/> but it is a separate method
        /// because different subtypes can implement this operation with no extra allocations.
        /// </remarks>
        public abstract int GetMachineIdIndex(MachineId currentMachineId);

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            MachineIdSet serializableInstance = this;
            if (Format == SetFormat.Bits)
            {
                if (Count <= BitMachineIdSetThreshold)
                {
                    serializableInstance = new ArrayMachineIdSet(EnumerateMachineIds().Select(id => (ushort)id.Index));
                }
            }
            else
            {
                if (Format == SetFormat.Array && Count > BitMachineIdSetThreshold)
                {
                    // Not changing the format for LocationChangeMachineIdSet.
                    serializableInstance = BitMachineIdSet.Create(EnumerateMachineIds());
                }
            }

            writer.Write((byte)serializableInstance.Format);
            serializableInstance.SerializeCore(writer);
        }

        /// <nodoc />
        protected abstract void SerializeCore(BuildXLWriter writer);

        /// <summary>
        /// Returns true if deserialized instance would have a machine id with a given index.
        /// </summary>
        public static bool HasMachineId(ReadOnlySpan<byte> source, int index)
        {
            var reader = source.AsReader();
            var format = (SetFormat)reader.ReadByte();

            return format switch
            {
                SetFormat.Bits => BitMachineIdSet.HasMachineIdCore(reader.Remaining, index),
                SetFormat.Array => ArrayMachineIdSet.HasMachineIdCore(reader.Remaining, index),
                SetFormat.LocationChange => LocationChangeMachineIdSet.HasMachineIdCore(reader.Remaining, index),
                _ => throw new InvalidOperationException($"Unknown format '{format}'."),
            };
        }

        /// <nodoc />
        public static MachineIdSet Deserialize(BuildXLReader reader)
        {
            var format = (SetFormat)reader.ReadByte();

            return format switch
            {
                SetFormat.Bits => BitMachineIdSet.DeserializeCore(reader),
                SetFormat.Array => ArrayMachineIdSet.DeserializeCore(reader),
                SetFormat.LocationChange => LocationChangeMachineIdSet.DeserializeCore(reader),
                _ => throw new InvalidOperationException($"Unknown format '{format}'."),
            };
        }

        /// <nodoc />
        public static MachineIdSet Deserialize(ref SpanReader reader)
        {
            var format = (SetFormat)reader.ReadByte();

            return format switch
            {
                SetFormat.Bits => BitMachineIdSet.DeserializeCore(ref reader),
                SetFormat.Array => ArrayMachineIdSet.DeserializeCore(ref reader),
                SetFormat.LocationChange => LocationChangeMachineIdSet.DeserializeCore(ref reader),
                _ => throw new InvalidOperationException($"Unknown format '{format}'."),
            };
        }

        /// <summary>
        /// Format of a machine id set.
        /// </summary>
        protected enum SetFormat
        {
            /// <summary>
            /// Based on a bit vector.
            /// </summary>
            Bits,

            /// <summary>
            /// Based on an array that contains a list of machine ids.
            /// </summary>
            Array,

            /// <summary>
            /// Based on an array of location changes that can represents both additions and removals of locations.
            /// </summary>
            LocationChange
        }

        /// <inheritdoc />
        public IEnumerator<MachineId> GetEnumerator()
        {
            return EnumerateMachineIds().GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc />
        public override string ToString()
        {
            return $"Count: {Count}";
        }
    }
}
