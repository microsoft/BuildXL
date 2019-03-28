// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.InterfacesTest.Utils;
using FluentAssertions;
using Xunit;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace ContentStoreTest.Stores
{
    public sealed class ContentStoreConfigurationExtensionsTests : IDisposable
    {
        private readonly IAbsFileSystem _fileSystem = new MemoryFileSystem(new MemoryClock());
        private readonly AbsolutePath _rootPath = new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("C"));

        public void Dispose()
        {
            _fileSystem.Dispose();
        }

        [Fact]
        public async Task FileRoundtripSucceeds()
        {
            var configuration1 = new ContentStoreConfiguration(
                new MaxSizeQuota("10GB", "7GB"), new DiskFreePercentQuota("10", "13"));

            await configuration1.Write(_fileSystem, _rootPath);

            var result = await _fileSystem.ReadContentStoreConfigurationAsync(_rootPath);
            var configuration2 = result.Data;

            configuration2.MaxSizeQuota.Hard.Should().Be(10L * 1024 * 1024 * 1024);
            configuration2.MaxSizeQuota.Soft.Should().Be(7L * 1024 * 1024 * 1024);
            configuration2.DiskFreePercentQuota.Hard.Should().Be(10);
            configuration2.DiskFreePercentQuota.Soft.Should().Be(13);
        }

        [Fact]
        public Task WriteNoExistDirectoryThrows()
        {
            var configuration = new ContentStoreConfiguration();
            var rootPath = _rootPath / "noexist";
            Func<Task> funcAsync = async () => await configuration.Write(_fileSystem, rootPath);
            return Assert.ThrowsAsync<CacheException>(funcAsync);
        }

        [Fact]
        public async Task ReadNoExistDirectoryGivesError()
        {
            var rootPath = _rootPath / "noexist";
            var result = await _fileSystem.ReadContentStoreConfigurationAsync(rootPath);
            result.Succeeded.Should().BeFalse();
        }

        [Fact]
        public async Task ReadNoExistFileGivesError()
        {
            var result = await _fileSystem.ReadContentStoreConfigurationAsync(_rootPath);
            result.Succeeded.Should().BeFalse();
        }

        [Theory]
        [InlineData("1", "1", null)]
        [InlineData("ab:cd", "ab", "cd")]
        public void ExtractHardSoftSucceeds(string expression, string hard, string soft)
        {
            var tuple = expression.ExtractHardSoft();
            tuple.Item1.Should().Be(hard);
            tuple.Item2.Should().Be(soft);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1:2:3")]
        public void ExtractHardSoftThrows(string expression)
        {
            Action a = () => expression.ExtractHardSoft();
            a.Should().Throw<CacheException>();
        }
    }
}
