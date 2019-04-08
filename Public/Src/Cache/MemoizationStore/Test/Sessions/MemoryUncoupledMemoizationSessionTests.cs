// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    public class MemoryUncoupledMemoizationSessionTests : UncoupledMemoizationSessionTests
    {
        public MemoryUncoupledMemoizationSessionTests()
            : base(() => new MemoryFileSystem(TestSystemClock.Instance), TestGlobal.Logger)
        {
        }

        protected override IMemoizationStore CreateStore(DisposableDirectory testDirectory)
        {
            return new MemoryMemoizationStore(Logger);
        }

        [Fact]
        public Task Stats()
        {
            var context = new Context(Logger);
            return RunTestAsync(context, async store =>
            {
                var result = await store.GetStatsAsync(context);
                result.ShouldBeSuccess();
            });
        }
    }
}
