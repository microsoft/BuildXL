using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    public class RocksDbContentLocationDatabaseTests : TestBase
    {
        protected readonly MemoryClock Clock = new MemoryClock();

        protected readonly DisposableDirectory _workingDirectory;

        protected RocksDbContentLocationDatabaseConfiguration DefaultConfiguration { get; }

        public RocksDbContentLocationDatabaseTests(ITestOutputHelper output = null)
            : base(TestGlobal.Logger, output)
        {
            // Need to use unique folder for each test instance, because more then one test may be executed simultaneously.
            var uniqueOutputFolder = TestRootDirectoryPath / Guid.NewGuid().ToString();
            _workingDirectory = new DisposableDirectory(FileSystem, uniqueOutputFolder);
        }

        [Fact]
        public async Task DoesNotAllowWritingWhenInReadOnlyMode()
        {
            var configuration = new RocksDbContentLocationDatabaseConfiguration(_workingDirectory.Path)
            {
                CleanOnInitialize = false,
            };

            var context = new Context(Logger);
            var ctx = new OperationContext(context);

            // First, we create the database
            {
                var db = new RocksDbContentLocationDatabase(Clock, configuration, () => new MachineId[] { });
                await db.StartupAsync(ctx).ShouldBeSuccess();
                db.SetGlobalEntry("test", "hello");
                await db.ShutdownAsync(ctx).ShouldBeSuccess();
            }

            configuration.OpenReadOnly = true;
            {
                var db = new RocksDbContentLocationDatabase(Clock, configuration, () => new MachineId[] { });
                await db.StartupAsync(ctx).ShouldBeSuccess();

                db.TryGetGlobalEntry("test", out var readValue);
                readValue.Should().Be("hello");

                Assert.Throws<BuildXLException>(() =>
                {
                    db.SetGlobalEntry("test", "hello2");
                });

                await db.ShutdownAsync(ctx).ShouldBeSuccess();
            }
        }
    }
}
