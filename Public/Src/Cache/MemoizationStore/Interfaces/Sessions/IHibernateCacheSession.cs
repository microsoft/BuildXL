// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Utilities;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <summary>
    /// Inreface for cache sessions that support hibernation across restarts.
    /// </summary>
    public interface IHibernateCacheSession : IHibernateContentSession
    {
        /// <nodoc />
        IList<PublishingOperation> GetPendingPublishingOperations();

        /// <nodoc />
        Task SchedulePublishingOperationsAsync(Context context, IEnumerable<PublishingOperation> pendingOperations);
    }
}
