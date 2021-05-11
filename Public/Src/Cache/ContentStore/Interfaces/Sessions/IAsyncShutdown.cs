// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Interfaces.Sessions
{
    /// <summary>
    /// Interface for a session which is expected to shut down asynchronously.
    /// </summary>
    public interface IAsyncShutdown
    {
        /// <summary>
        /// Marks the object for shutdown and returns a Task which signals that the object is ready for shutdown.
        /// </summary>
        Task<BoolResult> RequestShutdownAsync(Context context);
    }
}
