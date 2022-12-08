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
    public abstract class MachineIdSet : IReadOnlyCollection<MachineId>, IEquatable<MachineIdSet>
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
        /// Returns an empty machine set that supports tracking additions and removals of the machine locations used with merge operators.
        /// </summary>
        public static LocationChangeMachineIdSet EmptyChangeSet => LocationChangeMachineIdSet.EmptyInstance;

        /// <summary>
        /// Returns an empty machine set that supports tracking additions and removals of the machine locations used with merge operators.
        /// </summary>
        public static SortedLocationChangeMachineIdSet SortedEmptyChangeSet => SortedLocationChangeMachineIdSet.EmptyInstance;

        /// <summary>
        /// Creates a list of machine ids that represents additions or removals of the content on them.
        /// </summary>
        public static LocationChangeMachineIdSet CreateChangeSet(bool sortLocations, bool exists, params MachineId[] machineIds) // Rename it to avoid confusion.
        {
            var set = sortLocations ? SortedEmptyChangeSet : EmptyChangeSet;
            if (machineIds.Length == 0)
            {
                return set;
            }

            return (LocationChangeMachineIdSet)set.SetExistence(MachineIdCollection.Create(machineIds), exists);
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
        public LocationChangeMachineIdSet Merge(MachineIdSet other, bool sortLocations)
        {
            MachineIdSet currentInstance = this;
            if (this is not LocationChangeMachineIdSet)
            {
                // If the current instance is not mergeable, re-creating a mergeable one to avoid losing removals.
                currentInstance = CreateChangeSet(sortLocations, exists: true, EnumerateMachineIds().ToArray());
            }

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

                // Once we move to .net core only we can change the SetExistence signature to return more derived type to avoid cast at all.
                return (LocationChangeMachineIdSet)currentInstance.SetExistence(additions, exists: true).SetExistence(removals, exists: false);
            }

            // The 'other' instance is not a mergeable one, it means that it has only a set of machines with the content.
            return (LocationChangeMachineIdSet)currentInstance.SetExistence(MachineIdCollection.Create(other.ToArray()), exists: true);
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
        public void Serialize(ref SpanWriter writer)
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
            serializableInstance.SerializeCore(ref writer);
        }

        /// <nodoc />
        protected abstract void SerializeCore(ref SpanWriter writer);

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
                SetFormat.LocationChangeSorted => SortedLocationChangeMachineIdSet.HasMachineIdCoreSorted(reader.Remaining, index),
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
                SetFormat.LocationChangeSorted => SortedLocationChangeMachineIdSet.DeserializeCoreSorted(ref reader),
                _ => throw new InvalidOperationException($"Unknown format '{format}'."),
            };
        }

        /// <summary>
        /// Format of a machine id set.
        /// </summary>
        public enum SetFormat
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
            LocationChange,

            /// <summary>
            /// Similar to <see cref="LocationChange"/> but all the locations are stored in a sorted form that allows merging them without deserialization.
            /// </summary>
            LocationChangeSorted,
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

        /// <inheritdoc />
        public bool Equals(MachineIdSet other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return EqualsCore(other);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj is null)
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            return Equals((MachineIdSet) obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            // This is not efficient, but this should not be used as a key in a map anyways.
            return ((int) Format, Count).GetHashCode();
        }

        /// <nodoc />
        protected virtual bool EqualsCore(MachineIdSet other)
        {
            return EnumerateMachineIds().SequenceEqual(other.EnumerateMachineIds());
        }
    }
}
