// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Represents a mapping from id to location of a machine.
    /// </summary>
    public class MachineMapping
    {
        /// <summary>
        /// Gets or sets the machine id.
        /// NOTE: This is mutable to allow recovery in rare case where machine id is assigned incorrectly.
        /// </summary>
        public MachineId Id { get; internal set; }

        /// <nodoc />
        public MachineLocation Location { get; }

        /// <nodoc />
        public MachineMapping(MachineLocation location, MachineId id)
        {
            Id = id;
            Location = location;
        }

        /// <nodoc />
        public override string ToString()
        {
            return $"(Id: {Id}, Location: {Location})";
        }
    }
}
