// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.Host.Service;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.Host.Configuration.Test
{
    public class TestConfigurationSerialization
    {
        [Fact]
        public void MaskConfigurationProperties()
        {
            var testConfig = new RedisContentLocationStoreConfiguration();
            var blobConnectionString = "blobConnectionString";

            testConfig.CentralStore = new BlobCentralStoreConfiguration(new AzureBlobStorageCredentials(blobConnectionString), "testContainer", "testKey");
            string configString = ConfigurationPrinter.ConfigToString(testConfig);

            configString.Should().NotContain(blobConnectionString);
        }

        [Fact]
        public void TestAbsolutePathSerialization()
        {
            var testPath = new AbsolutePath(OperatingSystemHelper.IsWindowsOS ? "M:/TESTPATH" : "/M/TESTPATH");
            var testConfig = new RocksDbContentLocationDatabaseConfiguration(testPath);
            string configString = ConfigurationPrinter.ConfigToString(testConfig);

            configString.Should().NotContain("FileName:");
            configString.Should().NotContain("Path:");
        }
    }
}
