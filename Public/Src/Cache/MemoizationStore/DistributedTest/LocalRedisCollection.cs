// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using ContentStoreTest.Distributed.Redis;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    /// <summary>
    /// Custom collection that uses <see cref="LocalRedisFixture"/>.
    /// </summary>
    /// <remarks>
    /// WARNING: there needs to be one of these per assembly that needs to usee <see cref="LocalRedisFixture"/>
    /// </remarks>
    [CollectionDefinition("Redis-based tests")]
    public class LocalRedisCollection : ICollectionFixture<LocalRedisFixture>
    {
        // This class has no code, and is never created. Its purpose is simply
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }
}
