// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using ProtoBuf;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Represents a mapping from id to location of a machine.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public record struct MachineMapping(MachineId Id, MachineLocation Location)
    {
        /// <summary>
        /// This parameterless constructor exists only to allow ProtoBuf.NET initialization
        /// </summary>
        public MachineMapping() : this(MachineId.Invalid, MachineLocation.Invalid) { }
    }
}
