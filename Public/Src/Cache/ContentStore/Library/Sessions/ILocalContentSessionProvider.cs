// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Sessions;

namespace BuildXL.Cache.ContentStore.Sessions.Internal;

/// <summary>
/// Content session which optionally wraps a local content session
/// </summary>
public interface ILocalContentSessionProvider : IContentSession
{
    /// <summary>
    /// Gets the local content session
    /// </summary>
    IContentSession? TryGetLocalContentSession();
}
