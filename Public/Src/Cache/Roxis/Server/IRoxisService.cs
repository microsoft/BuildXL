// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.Roxis.Common;

namespace BuildXL.Cache.Roxis.Server
{
    /// <summary>
    /// The minimum set of operations to provide in order to run a Roxis server. This is essentially just the "command
    /// handling" logic.
    /// </summary>
    public interface IRoxisService
    {
        public Task<CommandResponse> HandleAsync(CommandRequest request);
    }
}
