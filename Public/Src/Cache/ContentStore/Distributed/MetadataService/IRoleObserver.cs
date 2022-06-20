// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// Observes updates to the role of the current machine.
    /// </summary>
    public interface IRoleObserver : IStartupShutdownSlim
    {
        Task OnRoleUpdatedAsync(OperationContext context, MasterElectionState electionState);
    }
}
