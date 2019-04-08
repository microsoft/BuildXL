// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
