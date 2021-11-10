using System;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
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
    public class RocksDbContentMetadataDatabaseTests : TestBase
    {
        protected readonly MemoryClock Clock = new MemoryClock();

        protected readonly DisposableDirectory _workingDirectory;

        protected RocksDbContentLocationDatabaseConfiguration DefaultConfiguration { get; }

        public RocksDbContentMetadataDatabaseTests(ITestOutputHelper output = null)
            : base(TestGlobal.Logger, output)
        {
            // Need to use unique folder for each test instance, because more then one test may be executed simultaneously.
            var uniqueOutputFolder = TestRootDirectoryPath / Guid.NewGuid().ToString();
            _workingDirectory = new DisposableDirectory(FileSystem, uniqueOutputFolder);
        }

        private enum KeyCheckResult
        {
            Missing,
            Different,
            Valid
        }

        [Fact]
        public async Task TestGarbageCollect()
        {
            var configuration = new RocksDbContentMetadataDatabaseConfiguration(_workingDirectory.Path)
            {
                CleanOnInitialize = false,
            };

            var context = new Context(Logger);
            var ctx = new OperationContext(context);

            var keys = Enumerable.Range(0, 10).Select(i => (ShortHash)ContentHash.Random()).ToArray();

            void setBlob(RocksDbContentMetadataDatabase db, ShortHash key)
            {
                db.PutBlob(key, key.ToByteArray());
            }

            KeyCheckResult checkBlob(RocksDbContentMetadataDatabase db, ShortHash key)
            {
                if (db.TryGetBlob(key, out var blob))
                {
                    if (ByteArrayComparer.ArraysEqual(blob, key.ToByteArray()))
                    {
                        return KeyCheckResult.Valid;
                    }
                    else
                    {
                        return KeyCheckResult.Different;
                    }
                }
                else
                {
                    return KeyCheckResult.Missing;
                }
            }

            {
                var db = new RocksDbContentMetadataDatabase(Clock, configuration);
                await db.StartupAsync(ctx).ShouldBeSuccess();
                db.SetGlobalEntry("test", "hello");
                setBlob(db, keys[0]);
                checkBlob(db, keys[0]).Should().Be(KeyCheckResult.Valid);

                await db.GarbageCollectAsync(ctx, force: true).ShouldBeSuccess();
                setBlob(db, keys[1]);
                checkBlob(db, keys[0]).Should().Be(KeyCheckResult.Valid);
                checkBlob(db, keys[1]).Should().Be(KeyCheckResult.Valid);

                await db.GarbageCollectAsync(ctx, force: true).ShouldBeSuccess();
                checkBlob(db, keys[0]).Should().Be(KeyCheckResult.Missing);
                checkBlob(db, keys[1]).Should().Be(KeyCheckResult.Valid);

                await db.ShutdownAsync(ctx).ShouldBeSuccess();
            }

            {
                var db = new RocksDbContentMetadataDatabase(Clock, configuration);
                await db.StartupAsync(ctx).ShouldBeSuccess();

                db.TryGetGlobalEntry("test", out var readValue);
                readValue.Should().Be("hello");

                setBlob(db, keys[2]);

                checkBlob(db, keys[0]).Should().Be(KeyCheckResult.Missing);
                checkBlob(db, keys[1]).Should().Be(KeyCheckResult.Valid);
                checkBlob(db, keys[2]).Should().Be(KeyCheckResult.Valid);
                await db.GarbageCollectAsync(ctx, force: true).ShouldBeSuccess();

                checkBlob(db, keys[0]).Should().Be(KeyCheckResult.Missing);
                checkBlob(db, keys[1]).Should().Be(KeyCheckResult.Missing);
                checkBlob(db, keys[2]).Should().Be(KeyCheckResult.Valid);

                await db.ShutdownAsync(ctx).ShouldBeSuccess();
            }
        }
    }
}
