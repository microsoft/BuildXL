using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
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
            var firstConnectionString = "testGlobalString";
            var secondConnectionString = "password12345";
            var blobConnectionString = "blobConnectionString";

            testConfig.RedisGlobalStoreConnectionString = firstConnectionString;
            testConfig.RedisGlobalStoreSecondaryConnectionString = secondConnectionString;
            testConfig.CentralStore = new BlobCentralStoreConfiguration(new AzureBlobStorageCredentials(blobConnectionString), "testContainer", "testKey");
            string configString = ConfigurationPrinter.ConfigToString(testConfig);

            configString.Should().NotContain(firstConnectionString);
            configString.Should().NotContain(secondConnectionString);
            configString.Should().NotContain(blobConnectionString);
        }

        [Fact]
        public void TestAbsolutePathSerialization()
        {
            var testPath = new AbsolutePath("M:/TESTPATH");
            var testConfig = new RocksDbContentLocationDatabaseConfiguration(testPath);
            string configString = ConfigurationPrinter.ConfigToString(testConfig);

            configString.Should().NotContain("FileName:");
            configString.Should().NotContain("Path:");
        }
    }
}
