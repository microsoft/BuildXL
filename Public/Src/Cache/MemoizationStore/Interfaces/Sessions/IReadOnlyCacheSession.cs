// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <summary>
    ///     Read-only cache session.
    /// </summary>
    public interface IReadOnlyCacheSession : IReadOnlyMemoizationSession, IReadOnlyContentSession, IConfigurablePin
    {
    }

    /// <summary>
    ///     Read-only cache session.
    /// </summary>
    public interface IReadOnlyCacheSessionWithLevelSelectors : IReadOnlyCacheSession, IReadOnlyMemoizationSessionWithLevelSelectors
    {
    }
}
