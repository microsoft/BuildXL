// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Engine.Distribution.OpenBond;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Engine.Distribution
{
    internal interface IMasterClient
    {
        Task<RpcCallResult<Unit>> AttachCompletedAsync(AttachCompletionInfo attachCompletionInfo);
        Task<RpcCallResult<Unit>> NotifyAsync(WorkerNotificationArgs notificationArgs, IList<long> semiStableHashes);
        Task CloseAsync();
    }
}