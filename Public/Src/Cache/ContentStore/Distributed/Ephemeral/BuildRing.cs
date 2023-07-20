// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

/// <summary>
/// This structure contains a set of machines that are running a distributed build. The order in this list matters,
/// because the first machine is the leader and the rest are workers.
/// </summary>
public record BuildRing
{
    private readonly List<MachineLocation> _builders;

    public string Id { get; }

    public MachineLocation Leader => Builders[0];

    public IReadOnlyList<MachineLocation> Builders => _builders;

    public BuildRing(string id, List<MachineLocation> builders)
    {
        Contract.RequiresNotNullOrEmpty(id);
        Contract.Requires(builders.Count > 0);

        Id = id;
        _builders = builders;
    }

    public bool Remove(MachineLocation location)
    {
        return _builders.Remove(location);
    }

    public bool Contains(MachineLocation location)
    {
        return Builders.Contains(location);
    }
}
