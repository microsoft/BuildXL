// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

/// <summary>
/// This structure contains a set of machines that are running a distributed build. The order in this list matters,
/// because the first machine is the leader and the rest are workers.
/// </summary>
public record BuildRing
{
    public MachineLocation Leader => Builders[0];

    public IReadOnlyList<MachineLocation> Builders { get; }

    public BuildRing(IReadOnlyList<MachineLocation> builders)
    {
        Contract.Requires(builders.Count > 0);
        Builders = builders;
    }
}
