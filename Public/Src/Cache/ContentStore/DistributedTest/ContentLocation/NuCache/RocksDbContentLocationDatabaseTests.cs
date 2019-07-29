using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    public class RocksDbContentLocationDatabaseTests : TestWithOutput
    {
        protected readonly MemoryClock Clock = new MemoryClock();

        protected readonly DisposableDirectory _workingDirectory;

        protected ContentLocationDatabaseConfiguration DefaultConfiguration { get; } = null;

        public RocksDbContentLocationDatabaseTests(ITestOutputHelper output)
            : base(output)
        {
            // Need to use unique folder for each test instance, because more then one test may be executed simultaneously.
            var uniqueOutputFolder = Guid.NewGuid().ToString();
            _workingDirectory = new DisposableDirectory(new PassThroughFileSystem(TestGlobal.Logger), Path.Combine(uniqueOutputFolder, "redis"));

            DefaultConfiguration = new RocksDbContentLocationDatabaseConfiguration(_workingDirectory.Path / "rocksdb");
        }

        private async Task RunTest(Action<OperationContext, ContentLocationDatabase> action) => await RunCustomTest(DefaultConfiguration, action);

        private async Task RunCustomTest(ContentLocationDatabaseConfiguration configuration, Action<OperationContext, ContentLocationDatabase> action, OperationContext? overwrite = null)
        {
            var tracingContext = new Context(TestGlobal.Logger);
            var operationContext = overwrite ?? new OperationContext(tracingContext);

            var database = ContentLocationDatabase.Create(Clock, configuration, () => new MachineId[] { });
            await database.StartupAsync(operationContext).ShouldBeSuccess();
            database.SetDatabaseMode(isDatabaseWritable: true);

            action(operationContext, database);

            await database.ShutdownAsync(operationContext).ShouldBeSuccess();
        }
    }
}
