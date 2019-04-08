// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using ContentStoreTest.Performance;
using ContentStoreTest.Sessions;
using ContentStoreTest.Stores;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable SA1402 // File may only contain a single class

// ReSharper disable UnusedMember.Global
namespace ContentStoreTest.Performance.Sessions
{
    public abstract class FileSystemContentPerformanceTests : ContentPerformanceTests
    {
        protected FileSystemContentPerformanceTests(
            ILogger logger, InitialSize initialSize, PerformanceResultsFixture resultsFixture, ITestOutputHelper output = null)
            : base(logger, initialSize, resultsFixture, output)
        {
        }

        protected override IContentStore CreateStore(AbsolutePath rootPath, string cacheName, ContentStoreConfiguration configuration)
        {
            var configurationModel = new ConfigurationModel(configuration);
            return new TestFileSystemContentStore(FileSystem, SystemClock.Instance, rootPath, configurationModel);
        }

        protected override Task<IReadOnlyList<ContentHash>> EnumerateContentHashesAsync(IReadOnlyContentSession session)
        {
            var testSession = (TestFileSystemContentSession)session;
            return testSession.EnumerateHashes();
        }
    }

    [Trait("Category", "Performance")]
    public class FullFileSystemContentPerformanceTests
        : FileSystemContentPerformanceTests, IClassFixture<PerformanceResultsFixture>
    {
        public FullFileSystemContentPerformanceTests(ITestOutputHelper output, PerformanceResultsFixture resultsFixture)
            : base(TestGlobal.Logger, InitialSize.Full, resultsFixture, output)
        {
        }
    }

    [Trait("Category", "Performance")]
    public class EmptyFileSystemContentPerformanceTests
        : FileSystemContentPerformanceTests, IClassFixture<PerformanceResultsFixture>
    {
        public EmptyFileSystemContentPerformanceTests(ITestOutputHelper output, PerformanceResultsFixture resultsFixture)
            : base(TestGlobal.Logger, InitialSize.Empty, resultsFixture, output)
        {
        }
    }

    [Trait("Category", "Performance")]
    public class NoLoggingFileSystemContentPerformanceTests
        : FileSystemContentPerformanceTests, IClassFixture<PerformanceResultsFixture>
    {
        public NoLoggingFileSystemContentPerformanceTests(ITestOutputHelper output, PerformanceResultsFixture resultsFixture)
            : base(NullLogger.Instance, InitialSize.Full, resultsFixture, output)
        {
        }
    }
}
