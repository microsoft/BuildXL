using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Logging;
using ContentStoreTest.Test;
using FluentAssertions;
using Microsoft.WindowsAzure.Storage.Blob;
using Xunit;

namespace BuildXL.Cache.Logging.Test
{
    public class AzureBlobStorageLogTests : TestBase
    {
        public AzureBlobStorageLogTests()
            : base(TestGlobal.Logger)
        {
        }

        public Task WithConfiguration(Func<AzureBlobStorageLogConfiguration, OperationContext, IClock, IAbsFileSystem, ITelemetryFieldsProvider, AzureBlobStorageCredentials, Task> action)
        {
            var fileSystem = new PassThroughFileSystem();
            using var workspace = new DisposableDirectory(fileSystem);

            // See: https://docs.microsoft.com/en-us/azure/storage/common/storage-use-emulator#connect-to-the-emulator-account-using-a-shortcut
            var credentials = new AzureBlobStorageCredentials(connectionString: "UseDevelopmentStorage=true");

            var tracingContext = new Context(Logger);
            var context = new OperationContext(tracingContext);
            var configuration = new AzureBlobStorageLogConfiguration(workspace.Path);
            var clock = SystemClock.Instance;
            var telemetryFieldsProvider = new MockTelemetryFieldsProvider();
            return action(configuration, context, clock, fileSystem, telemetryFieldsProvider, credentials);
        }

        [Fact(Skip = "Usage with Azure Storage Emulator only")]
        public Task MakesFilesAvailableInBlobStorage()
        {
            return WithConfiguration(async (configuration, context, clock, fileSystem, telemetryFieldsProvider, credentials) =>
            {
                configuration.DrainUploadsOnShutdown = true;
                var log = new AzureBlobStorageLog(configuration, context, clock, fileSystem, telemetryFieldsProvider, credentials);

                await log.StartupAsync().ThrowIfFailure();
                {
                    log.Write("This is a test string\n");
                }
                await log.ShutdownAsync().ThrowIfFailure();

                var cloudBlobClient = credentials.CreateCloudBlobClient();
                var container = cloudBlobClient.GetContainerReference(configuration.ContainerName);
                var directory = container.GetDirectoryReference("");
                (await ListBlobsAsync(container, directory)).Count().Should().Be(1);
            });
        }

        private async Task<List<IListBlobItem>> ListBlobsAsync(CloudBlobContainer container, CloudBlobDirectory path)
        {
            var segment = await path.ListBlobsSegmentedAsync(null);
            var list = new List<IListBlobItem>();
            list.AddRange(segment.Results);
            while (segment.ContinuationToken != null)
            {
                segment = await container.ListBlobsSegmentedAsync(segment.ContinuationToken);
                list.AddRange(segment.Results);
            }

            return list;
        }

        private class MockTelemetryFieldsProvider : ITelemetryFieldsProvider
        {
            public string BuildId { get; } = "MockBuildId";

            public string ServiceName { get; } = "MockServiceName";

            public string APEnvironment { get; } = "MockAPEnvironment";

            public string APCluster { get; } = "MockAPCluster";

            public string APMachineFunction { get; } = "MockAPMachineFunction";

            public string MachineName { get; } = "MockMachineName";

            public string ServiceVersion { get; } = "MockServiceVersion";

            public string Stamp { get; } = "MockStamp";

            public string Ring { get; } = "MockRing";

            public string ConfigurationId { get; } = "MockConfigurationId";
        }
    }
}
