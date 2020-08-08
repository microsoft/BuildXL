// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Roxis.Common;

namespace BuildXL.Cache.Roxis.Client
{
    /// <summary>
    /// Interface for a Roxis client
    /// </summary>
    public interface IRoxisClient
    {
        Task<Result<CommandResponse>> ExecuteAsync(OperationContext context, CommandRequest request);
    }
}
