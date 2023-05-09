// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Stores;

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
