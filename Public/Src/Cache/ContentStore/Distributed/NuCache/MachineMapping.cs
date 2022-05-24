// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Represents a mapping from id to location of a machine.
    /// </summary>
    public record struct MachineMapping(MachineId Id, MachineLocation Location);
}
