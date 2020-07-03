// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <summary>
    ///     Writable cache session.
    /// </summary>
    public interface ICacheSession : IReadOnlyCacheSession, IMemoizationSession, IContentSession
    {
    }
}
