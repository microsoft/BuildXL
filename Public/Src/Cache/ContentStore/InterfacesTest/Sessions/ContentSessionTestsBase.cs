// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Sessions
{
    public abstract class ContentSessionTestsBase : TestWithOutput
    {
        protected virtual string Name { get; set; } =  "name";

        protected const int ContentByteCount = 100;
        protected const HashType ContentHashType = HashType.Vso0;
        protected const long DefaultMaxSize = 1 * 1024 * 1024;
        protected static readonly CancellationToken Token = CancellationToken.None;

        protected readonly IAbsFileSystem FileSystem;
        protected readonly ILogger Logger;

        private bool _disposed;

        protected ContentSessionTestsBase(Func<IAbsFileSystem> createFileSystemFunc, ILogger logger, ITestOutputHelper output = null)
            : base(output)
        {
            FileSystem = createFileSystemFunc();
            Logger = logger;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            base.Dispose();

            if (_disposed)
            {
                return;
            }

            FileSystem?.Dispose();
            Logger?.Flush();

            _disposed = true;
        }

        protected virtual long MaxSize => DefaultMaxSize;

        protected ContentStoreConfiguration CreateStoreConfiguration()
        {
            return new ContentStoreConfiguration(new MaxSizeQuota($"{MaxSize}"));
        }

        protected abstract IContentStore CreateStore(DisposableDirectory testDirectory, ContentStoreConfiguration configuration);

        protected virtual async Task RunReadOnlyTestAsync(ImplicitPin implicitPin, Func<Context, IReadOnlyContentSession, Task> funcAsync)
        {
            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var config = CreateStoreConfiguration();

                using (var store = CreateStore(directory, config))
                {
                    try
                    {
                        await store.StartupAsync(context).ShouldBeSuccess();

                        var createResult = store.CreateReadOnlySession(context, Name, implicitPin).ShouldBeSuccess();
                        using (var session = createResult.Session)
                        {
                            try
                            {
                                await session.StartupAsync(context).ShouldBeSuccess();
                                await funcAsync(context, session);
                            }
                            finally
                            {
                                await session.ShutdownAsync(context).ShouldBeSuccess();
                            }
                        }
                    }
                    finally
                    {
                        await store.ShutdownAsync(context).ShouldBeSuccess();
                    }
                }
            }
        }

        protected virtual async Task RunTestAsync(ImplicitPin implicitPin, DisposableDirectory directory, Func<Context, IContentSession, Task> funcAsync)
        {
            var context = new Context(Logger);

            bool useNewDirectory = directory == null;
            if (useNewDirectory)
            {
                directory = new DisposableDirectory(FileSystem);
            }

            try
            {
                var config = CreateStoreConfiguration();

                using (var store = CreateStore(directory, config))
                {
                    try
                    {
                        await store.StartupAsync(context).ShouldBeSuccess();

                        var createResult = store.CreateSession(context, Name, implicitPin).ShouldBeSuccess();
                        using (var session = createResult.Session)
                        {
                            try
                            {
                                Assert.False(session.StartupStarted);
                                Assert.False(session.StartupCompleted);
                                Assert.False(session.ShutdownStarted);
                                Assert.False(session.ShutdownCompleted);

                                await session.StartupAsync(context).ShouldBeSuccess();

                                await funcAsync(context, session);
                            }
                            finally
                            {
                                await session.ShutdownAsync(context).ShouldBeSuccess();
                            }

                            Assert.True(session.StartupStarted);
                            Assert.True(session.StartupCompleted);
                            Assert.True(session.ShutdownStarted);
                            Assert.True(session.ShutdownCompleted);
                        }
                    }
                    finally
                    {
                        await store.ShutdownAsync(context).ShouldBeSuccess();
                    }
                }
            }
            finally
            {
                if (useNewDirectory)
                {
                    directory.Dispose();
                }
            }
        }
    }
}
