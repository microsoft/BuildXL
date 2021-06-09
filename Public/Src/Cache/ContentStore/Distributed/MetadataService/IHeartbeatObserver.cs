// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    public interface IHeartbeatObserver
    {
        public Task OnSuccessfulHeartbeatAsync(OperationContext context, Role role);
    }
}
