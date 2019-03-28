// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using FluentAssertions;
using BuildXL.Cache.ContentStore.Utils;
using Xunit.Abstractions;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace ContentStoreTest.Stores
{
    public abstract class FileSystemContentStoreInternalTestBase : TestBase
    {
        protected readonly int MaxShortPath = FileSystemConstants.MaxShortPath;
        protected const int ValueSize = 100;
        protected const int MaxSizeHard = 1000;
        protected const int MaxSizeSoft = 800;
        private const int MaxSizeTarget = 780;
        protected const HashType ContentHashType = HashType.Vso0;
        protected static readonly string HashOfEmptyFile = new byte[] {}.CalculateHash(ContentHashType).ToHex();
        private static readonly MaxSizeQuota MaxSizeQuota = new MaxSizeQuota(MaxSizeHard, MaxSizeSoft);
        private static readonly ContentStoreConfiguration Config = new ContentStoreConfiguration(MaxSizeQuota);

        protected static readonly char[] Drives = { 'C', 'D' };

        protected static int BlobSizeToStartSoftPurging(int numberOfBlobs)
        {
            var blobSize = MaxSizeHard;
            blobSize *= Math.Min(MaxSizeSoft + 5, 100);
            blobSize /= 100;
            blobSize /= numberOfBlobs;
            return blobSize;
        }

        protected FileSystemContentStoreInternalTestBase(Func<IAbsFileSystem> createFileSystemFunc, ILogger logger, ITestOutputHelper output = null)
            : base(createFileSystemFunc, logger, output)
        {
            MaxSizeQuota.Soft.Should().Be(MaxSizeSoft);
            MaxSizeQuota.Target.Should().Be(MaxSizeTarget);
        }

        protected Task TestStore(Context context, ITestClock clock, Func<TestFileSystemContentStoreInternal, Task> func)
        {
            return TestStoreImpl(context, clock, func);
        }

        protected Task TestStore
            (
            Context context,
            ITestClock clock,
            DisposableDirectory testDirectory,
            Func<TestFileSystemContentStoreInternal, Task> func,
            Action<TestFileSystemContentStoreInternal> preStartupAction = null
            )
        {
            return TestStoreImpl(context, clock, func, testDirectory, preStartupAction: preStartupAction);
        }

        protected Task TestStore
            (
            Context context,
            ITestClock clock,
            IContentChangeAnnouncer announcer,
            Func<TestFileSystemContentStoreInternal, Task> func
            )
        {
            return TestStoreImpl(context, clock, func, announcer: announcer);
        }

        protected Task TestStore
        (
            Context context,
            ITestClock clock,
            Func<TestFileSystemContentStoreInternal, Task> func,
            NagleQueue<ContentHash> nagleBlock
        )
        {
            return TestStoreImpl(context, clock, func, nagleBlock: nagleBlock);
        }

        private async Task TestStoreImpl
            (
            Context context,
            ITestClock clock,
            Func<TestFileSystemContentStoreInternal, Task> func,
            DisposableDirectory testDirectory = null,
            IContentChangeAnnouncer announcer = null,
            NagleQueue<ContentHash> nagleBlock = null,
            Action<TestFileSystemContentStoreInternal> preStartupAction = null
            )
        {
            using (var tempTestDirectory = new DisposableDirectory(FileSystem))
            {
                DisposableDirectory disposableDirectory = testDirectory ?? tempTestDirectory;
                using (var store = Create(disposableDirectory.Path, clock, nagleBlock))
                {
                    if (announcer != null)
                    {
                       store.Announcer = announcer;
                    }

                    store.Should().NotBeNull();
                    try
                    {
                        preStartupAction?.Invoke(store);
                        var r = await store.StartupAsync(context);
                        r.ShouldBeSuccess();
                        await func(store);
                    }
                    finally
                    {
                        if (!store.ShutdownStarted)
                        {
                            await store.ShutdownAsync(context).ShouldBeSuccess();
                        }
                    }
                }
            }
        }

        protected virtual TestFileSystemContentStoreInternal Create(AbsolutePath rootPath, ITestClock clock, NagleQueue<ContentHash> nagleBlock = null)
        {
            return new TestFileSystemContentStoreInternal(FileSystem, clock, rootPath, Config, nagleQueue: nagleBlock);
        }

        protected virtual TestFileSystemContentStoreInternal CreateElastic(
            AbsolutePath rootPath,
            ITestClock clock,
            NagleQueue<ContentHash> nagleBlock = null,
            MaxSizeQuota initialQuota = null,
            int? windowSize = default(int?))
        {
            var maxSizeQuota = initialQuota ?? new MaxSizeQuota(MaxSizeHard, MaxSizeSoft);

            // Some tests rely on maxSizeQuota being set in the configuration although it is ignored if elasticity is enabled.
            var config = new ContentStoreConfiguration(maxSizeQuota: maxSizeQuota, enableElasticity: true, initialElasticSize: maxSizeQuota, historyWindowSize: windowSize);

            return new TestFileSystemContentStoreInternal(FileSystem, clock, rootPath, config, nagleQueue: nagleBlock);
        }

        protected async Task<ElasticSizeRule.LoadQuotaResult> LoadElasticQuotaAsync(AbsolutePath rootPath)
        {
            var filePath = rootPath / ElasticSizeRule.BinaryFileName;

            if (!FileSystem.FileExists(filePath))
            {
                return null;
            }

            using (var stream = await FileSystem.OpenReadOnlyAsync(filePath, FileShare.Delete))
            {
                using (var reader = new BinaryReader(stream))
                {
                    return new ElasticSizeRule.LoadQuotaResult(new MaxSizeQuota(reader.ReadInt64(), reader.ReadInt64()), reader.ReadInt64());
                }
            }
        }
    }
}
