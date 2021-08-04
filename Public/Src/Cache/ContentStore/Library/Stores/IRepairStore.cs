// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Interface for repair handling.
    /// </summary>
    /// <remarks>
    /// The delegate is not used and still in the code for backward compatibility reasons.
    /// </remarks>
    public delegate Task<Result<long>> TrimBulkAsync(Context context, IEnumerable<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint);

    /// <summary>
    ///     Extended features for content stores to support repair handling.
    /// </summary>
    public interface IRepairStore
    {
        /// <summary>
        ///     Invalidates all content for the machine in the content location store
        /// </summary>
        Task<BoolResult> RemoveFromTrackerAsync(Context context);
    }
}
