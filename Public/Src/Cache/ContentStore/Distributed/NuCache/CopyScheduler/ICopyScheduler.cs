// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Utilities;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.CopyScheduling
{
    /// <nodoc />
    public interface ICopyScheduler
    {
        /// <nodoc />
        Task<CopySchedulerResult<CopyFileResult>> ScheduleOutboundPullAsync(OutboundPullCopy request);

        /// <nodoc />
        Task<CopySchedulerResult<T>> ScheduleOutboundPushAsync<T>(OutboundPushCopy<T> request)
            where T: class;
    }
}
