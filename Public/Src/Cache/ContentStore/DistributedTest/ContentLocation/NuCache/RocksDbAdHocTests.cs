using System.Diagnostics;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Engine.Cache.KeyValueStores;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation.NuCache
{
    public class RocksDbAdHocTests : TestBase
    {
        public RocksDbAdHocTests(ITestOutputHelper output = null)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, output)
        {
        }

        [Fact]
        public void ReadOnlyWorksWithHardlinks()
        {
            var storeDirectory = TestRootDirectoryPath / "store";
            var key = ThreadSafeRandom.GetBytes(10);
            var value = ThreadSafeRandom.GetBytes(10);

            // Create a RocksDb instance and write to it
            using (var accessor = KeyValueStoreAccessor.Open(storeDirectory.ToString()).Result)
            {
                var result = accessor.Use(store => store.Put(key, value));
                result.Succeeded.Should().BeTrue();
            }

            // Hardlink it to the target path
            var hardlinkPath = TestRootDirectoryPath / "hardlinks";
            FileSystem.CreateDirectory(hardlinkPath);
            foreach (var file in FileSystem.EnumerateFiles(storeDirectory, EnumerateOptions.None))
            {
                var source = file.FullPath;
                var target = hardlinkPath / file.FullPath.FileName;

                Logger.Info($"Found {source}. Linking to {target}");
                var result = FileSystem.CreateHardLink(source, target, replaceExisting: true);
                result.Should().Be(CreateHardLinkResult.Success);

                // Make sure nothing can be written to any of the files
                FileSystem.SetFileAttributes(target, System.IO.FileAttributes.Normal);
                FileSystem.SetFileAttributes(target, System.IO.FileAttributes.ReadOnly);
                FileSystem.DenyFileWrites(target);
                FileSystem.DenyAttributeWrites(target);
            }

            // Open the target in read-only mode and make sure we read the same value
            using (var accessor = KeyValueStoreAccessor.Open(hardlinkPath.ToString(), openReadOnly: true).Result)
            {
                var result = accessor.Use(store =>
                  {
                      var found = store.TryGetValue(key, out var foundValue);
                      found.Should().BeTrue();
                      foundValue.Should().BeEquivalentTo(value);
                  });
                result.Succeeded.Should().BeTrue();
            }
        }
    }
}
