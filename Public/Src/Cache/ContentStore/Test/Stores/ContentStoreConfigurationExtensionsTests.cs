// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        public void FileRoundtripSucceeds()
        {
            var configuration1 = new ContentStoreConfiguration(
                new MaxSizeQuota("10GB", "7GB"), new DiskFreePercentQuota("10", "13"));

            configuration1.Write(_fileSystem, _rootPath);

            var result = _fileSystem.ReadContentStoreConfiguration(_rootPath);
            var configuration2 = result.Value;

            configuration2.MaxSizeQuota.Hard.Should().Be(10L * 1024 * 1024 * 1024);
            configuration2.MaxSizeQuota.Soft.Should().Be(7L * 1024 * 1024 * 1024);
            configuration2.DiskFreePercentQuota.Hard.Should().Be(10);
            configuration2.DiskFreePercentQuota.Soft.Should().Be(13);
        }

        [Fact]
        public void WriteNoExistDirectoryThrows()
        {
            var configuration = new ContentStoreConfiguration();
            var rootPath = _rootPath / "noexist";
            Action func = () => configuration.Write(_fileSystem, rootPath);
            Assert.Throws<CacheException>(func);
        }

        [Fact]
        public void ReadNoExistDirectoryGivesError()
        {
            var rootPath = _rootPath / "noexist";
            var result = _fileSystem.ReadContentStoreConfiguration(rootPath);
            result.Succeeded.Should().BeFalse();
        }

        [Fact]
        public void ReadNoExistFileGivesError()
        {
            var result = _fileSystem.ReadContentStoreConfiguration(_rootPath);
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
