// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.MemoizationStore.Test.Extensions
{
    public class ContentSessionExtensionsTests : TestBase
    {
        private const string Name = "name";
        private const HashType HasherType = HashType.Vso0;
        private const int RandomContentByteCount = 100;
        private static readonly CancellationToken Token = CancellationToken.None;

        public ContentSessionExtensionsTests(ITestOutputHelper output)
            : base(() => new MemoryFileSystem(TestSystemClock.Instance), TestGlobal.Logger, output)
        {
        }

        [Fact]
        public async Task FalseOnNullContentSession()
        {
            var context = new Context(Logger);
            var contentHashList = new ContentHashList(new ContentHash[] {});
            (await ((IContentSession)null).EnsureContentIsAvailableAsync(context, contentHashList, Token)).Should().BeFalse();
        }

        [Fact]
        public async Task TrueOnNullContentHashList()
        {
            var context = new Context(Logger);
            await RunTestAsync(context, async session =>
            {
                (await session.EnsureContentIsAvailableAsync(context, null, Token)).Should().BeTrue();
            });
        }

        [Fact]
        public async Task FalseOnMissingContent()
        {
            var context = new Context(Logger);
            var contentHashList = ContentHashList.Random(HasherType);
            await RunTestAsync(context, async session =>
            {
                (await session.EnsureContentIsAvailableAsync(context, contentHashList, Token)).Should().BeFalse();
            });
        }

        [Fact]
        public async Task TrueOnExistingContent()
        {
            var context = new Context(Logger);
            await RunTestAsync(context, async session =>
            {
                var putResult = await session.PutRandomAsync(
                    context, HasherType, false, RandomContentByteCount, Token);
                var contentHashList = new ContentHashList(new[] {putResult.ContentHash});
                (await session.EnsureContentIsAvailableAsync(context, contentHashList, Token)).Should().BeTrue();
            });
        }

        private async Task RunTestAsync(Context context, Func<IContentSession, Task> funcAsync)
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var rootPath = testDirectory.Path;
                var configuration = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(1);
                var configurationModel = new ConfigurationModel(configuration);

                using (var store = new FileSystemContentStore(
                    FileSystem, SystemClock.Instance, rootPath, configurationModel))
                {
                    try
                    {
                        var startupStoreResult = await store.StartupAsync(context);
                        startupStoreResult.ShouldBeSuccess();

                        var createResult = store.CreateSession(context, Name, ImplicitPin.None);
                        createResult.ShouldBeSuccess();
                        using (var session = createResult.Session)
                        {
                            try
                            {
                                var startupSessionResult = await session.StartupAsync(context);
                                startupSessionResult.ShouldBeSuccess();

                                await funcAsync(session);
                            }
                            finally
                            {
                                var shutdownSessionResult = await session.ShutdownAsync(context);
                                shutdownSessionResult.ShouldBeSuccess();
                            }
                        }
                    }
                    finally
                    {
                        var shutdownStoreResult = await store.ShutdownAsync(context);
                        shutdownStoreResult.ShouldBeSuccess();
                    }
                }
            }
        }
    }
}
