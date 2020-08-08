// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Roxis.Common;
using BuildXL.Cache.Roxis.Server;
using FluentAssertions;
using FsCheck;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.Roxis.Test
{
    public class RoxisDatabaseOperationsTests : TestWithOutput
    {
        public RoxisDatabaseOperationsTests(ITestOutputHelper output) : base(output)
        {
        }

        public async Task WithDatabaseAsync(Func<RoxisDatabase, MemoryClock, Task> test)
        {
            var databasePath = Guid.NewGuid().ToString();
            using var disposableDatabaseGuard = new DisposableDirectory(new PassThroughFileSystem(), databasePath);

            var clock = new MemoryClock();
            var databaseConfiguration = new RoxisDatabaseConfiguration()
            {
                Path = databasePath,
            };
            var database = new RoxisDatabase(databaseConfiguration, clock);

            // TODO: use proper logger
            var logger = NullLogger.Instance;
            var tracingContext = new Context(logger);
            var context = new OperationContext(tracingContext);

            await database.StartupAsync(context).ThrowIfFailureAsync();
            await test(database, clock);
            await database.ShutdownAsync(context).ThrowIfFailureAsync();
        }

        [Fact]
        public void CantGetNewRegister()
        {
            Prop.ForAll<byte[]>(key =>
            {
                WithDatabaseAsync(async (db, clock) =>
                {
                    var r = await db.HandleAsync<GetResult>(new GetCommand { Key = key });
                    r.Value.Should().BeNull();
                }).Wait();
            }).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void SetAndGetWorks()
        {
            Prop.ForAll<byte[], byte[]>((key, value) =>
            {
                WithDatabaseAsync(async (db, clock) =>
                {
                    await db.HandleAsync<SetResult>(new SetCommand() { Key = key, Value = value, Overwrite = false, ExpiryTimeUtc = null });

                    var r = await db.HandleAsync<GetResult>(new GetCommand { Key = key });
                    r.Value.Should().NotBeNull();
                    ((byte[])r.Value!).Should().BeEquivalentTo(value);
                }).Wait();
            }).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void SetWithExpiryAndGetWorks()
        {
            Prop.ForAll<byte[], byte[], NormalFloat>((key, value, timeToLive) =>
            {
                var timeToLiveSeconds = Math.Abs((double)timeToLive);

                WithDatabaseAsync(async (db, clock) =>
                {
                    var expiryTimeUtc = clock.UtcNow + TimeSpan.FromSeconds(timeToLiveSeconds);
                    await db.RegisterSetAsync(new SetCommand() { Key = key, Value = value, Overwrite = false, ExpiryTimeUtc = expiryTimeUtc });

                    var r1 = await db.RegisterGetAsync(new GetCommand() { Key = key });
                    clock.UtcNow = expiryTimeUtc;
                    var r2 = await db.RegisterGetAsync(new GetCommand() { Key = key });

                    if (timeToLiveSeconds > 0)
                    {
                        r1.Value.Should().NotBeNull();
                        ((byte[])r1.Value!).Should().BeEquivalentTo(value);
                    }
                    r2.Value.Should().BeNull();
                }).Wait();
            }).QuickCheckThrowOnFailure();
        }

        [Fact]
        public Task CompareExchangeSimple()
        {
            ByteString key = "TestKey";
            ByteString initialValue = "TestValue";
            ByteString replacementValue = "AA";

            return WithDatabaseAsync(async (db, clock) =>
            {
                // This is equivalent to a Set, because the key shouldn't exist
                var r1 = await db.HandleAsync<CompareExchangeResult>(new CompareExchangeCommand()
                {
                    Key = key,
                    Value = initialValue,
                    Comparand = null
                });
                r1.Previous.Should().BeNull();
                r1.Exchanged.Should().BeTrue();

                var r2 = await db.HandleAsync<CompareExchangeResult>(new CompareExchangeCommand()
                {
                    Key = key,
                    Value = replacementValue,
                    Comparand = initialValue
                });
                r2.Previous.Should().BeEquivalentTo(initialValue);
                r2.Exchanged.Should().BeTrue();

                var r3 = await db.HandleAsync<CompareExchangeResult>(new CompareExchangeCommand()
                {
                    Key = key,
                    Value = initialValue,
                    Comparand = initialValue
                });
                r3.Previous.Should().BeEquivalentTo(replacementValue);
                r3.Exchanged.Should().BeFalse();
            });
        }

        [Fact]
        public Task CompareExchangeWithCompareKey()
        {
            ByteString kvKey = "TestKey";
            ByteString kvValue = "TestKey1";
            ByteString kvReplacement = "TestKey2";

            ByteString cmpKey = "CompareKey";
            ByteString cmpValue = "CompareKey1";
            ByteString cmpReplacement = "CompareKey2";

            return WithDatabaseAsync(async (db, clock) =>
            {
                var r1 = await db.HandleAsync<CompareExchangeResult>(new CompareExchangeCommand()
                {
                    Key = kvKey,
                    Value = kvValue,
                    Comparand = null,
                });
                r1.Previous.Should().BeNull();
                r1.Exchanged.Should().BeTrue();

                var r2 = await db.HandleAsync<CompareExchangeResult>(new CompareExchangeCommand()
                {
                    Key = cmpKey,
                    Value = cmpValue,
                    Comparand = null,
                });
                r2.Previous.Should().BeNull();
                r2.Exchanged.Should().BeTrue();

                // First attempt it with a bad comparison
                var r3 = await db.HandleAsync<CompareExchangeResult>(new CompareExchangeCommand()
                {
                    Key = kvKey,
                    Value = kvReplacement,
                    CompareKey = cmpKey,
                    CompareKeyValue = cmpReplacement,
                    Comparand = kvValue,
                });
                r3.Previous.Should().BeEquivalentTo(cmpValue);
                r3.Exchanged.Should().BeFalse();

                // Now attempt it with a good comparison
                var r4 = await db.HandleAsync<CompareExchangeResult>(new CompareExchangeCommand()
                {
                    Key = kvKey,
                    Value = kvReplacement,
                    CompareKey = cmpKey,
                    CompareKeyValue = cmpReplacement,
                    Comparand = cmpValue,
                });
                r4.Previous.Should().BeEquivalentTo(cmpValue);
                r4.Exchanged.Should().BeTrue();

                // Validate exchanges took place as expected
                var r5 = await db.HandleAsync<GetResult>(new GetCommand() { Key = kvKey });
                r5.Value.Should().BeEquivalentTo(kvReplacement);

                var r6 = await db.HandleAsync<GetResult>(new GetCommand() { Key = cmpKey });
                r6.Value.Should().BeEquivalentTo(cmpReplacement);
            });
        }
    }
}
