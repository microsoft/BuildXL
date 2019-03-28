// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Stores
{
    public sealed class FileSystemContentStoreTests : TestBase
    {
        private const long MB = 1024 * 1024;
        private static readonly ITestClock Clock = new MemoryClock();

        public FileSystemContentStoreTests()
            : base(() => new MemoryFileSystem(Clock), TestGlobal.Logger)
        {
        }

        [Fact]
        public async Task StartupWithoutCallerConfigurationWhenConfigurationFileDoesNotExistGivesError()
        {
            using (var disposableDirectory = new DisposableDirectory(FileSystem))
            {
                var context = new Context(Logger);

                using (var store = new FileSystemContentStore(FileSystem, Clock, disposableDirectory.Path))
                {
                    var r = await store.StartupAsync(context);
                    r.ShouldBeError("ContentStoreConfiguration is missing");
                }
            }
        }

        [Fact]
        public async Task StartupWithCallerConfigurationWhenConfigurationFilesDoesNotExistCreatesIt()
        {
            using (var disposableDirectory = new DisposableDirectory(FileSystem))
            {
                var rootPath = disposableDirectory.Path;
                var context = new Context(Logger);
                var configuration = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(1);
                var configurationModel = new ConfigurationModel(
                    configuration,
                    ConfigurationSelection.UseFileAllowingInProcessFallback,
                    MissingConfigurationFileOption.WriteOnlyIfNotExists);

                using (var store = new FileSystemContentStore(FileSystem, Clock, rootPath, configurationModel))
                {
                    try
                    {
                        var r = await store.StartupAsync(context);
                        r.ShouldBeSuccess();
                        FileSystem.FileExists(rootPath / ContentStoreConfigurationExtensions.FileName).Should().BeTrue();
                    }
                    finally
                    {
                        await store.ShutdownAsync(context).ShouldBeSuccess();
                    }
                }
            }
        }

        [Theory]
        [InlineData(ConfigurationSelection.UseFileAllowingInProcessFallback)]
        [InlineData(ConfigurationSelection.RequireAndUseInProcessConfiguration)]
        public async Task StartupWithCallerConfigurationWhenConfigurationFileExists(ConfigurationSelection selection)
        {
            using (var disposableDirectory = new DisposableDirectory(FileSystem))
            {
                var rootPath = disposableDirectory.Path;
                var context = new Context(Logger);

                var configurationExisting = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(5);
                await configurationExisting.Write(FileSystem, rootPath);

                var configurationNew = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(7);
                var configurationModel = new ConfigurationModel(configurationNew, selection);

                using (var store = new TestFileSystemContentStore(FileSystem, Clock, rootPath, configurationModel))
                {
                    try
                    {
                        var r = await store.StartupAsync(context);
                        r.ShouldBeSuccess();

                        var expectedQuota = selection == ConfigurationSelection.UseFileAllowingInProcessFallback
                            ? 5 * MB
                            : 7 * MB;

                        store.Configuration.MaxSizeQuota.Hard.Should().Be(expectedQuota);
                    }
                    finally
                    {
                        await store.ShutdownAsync(context).ShouldBeSuccess();
                    }
                }
            }
        }
    }
}
