// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;

namespace BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions
{
    public abstract class UncoupledMemoizationSessionTests : MemoizationSessionTestBase
    {
        protected UncoupledMemoizationSessionTests(Func<IAbsFileSystem> createFileSystemFunc, ILogger logger)
            : base(createFileSystemFunc, logger)
        {
        }

        protected override Task RunTestAsync(
            Context context, DisposableDirectory testDirectory, Func<IMemoizationStore, IMemoizationSession, Task> funcAsync, Func<DisposableDirectory, IMemoizationStore> createStoreFunc = null)
        {
            return RunTestAsync(
                context,
                testDirectory,
                async store =>
            {
                var createResult = store.CreateSession(context, Name);
                createResult.ShouldBeSuccess();
                using (var session = createResult.Session)
                {
                    try
                    {
                        var r = await session.StartupAsync(context);
                        r.ShouldBeSuccess();

                        await funcAsync(store, session);
                    }
                    finally
                    {
                        var r = await session.ShutdownAsync(context);
                        r.ShouldBeSuccess();
                    }
                }
            },
            createStoreFunc);
        }
    }
}
