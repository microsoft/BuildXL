using System;
using System.IO;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
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
    }
}
