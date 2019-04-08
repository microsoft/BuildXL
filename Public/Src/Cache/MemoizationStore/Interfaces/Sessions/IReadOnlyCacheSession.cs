// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <summary>
    ///     Read-only cache session.
    /// </summary>
    public interface IReadOnlyCacheSession : IReadOnlyMemoizationSession, IReadOnlyContentSession
    {
    }

    /// <summary>
    ///     Read-only cache session.
    /// </summary>
    public interface IReadOnlyCacheSessionWithLevelSelectors : IReadOnlyCacheSession, IReadOnlyMemoizationSessionWithLevelSelectors
    {
    }
}
