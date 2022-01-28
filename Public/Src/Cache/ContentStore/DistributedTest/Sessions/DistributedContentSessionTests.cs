// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.InterfacesTest.Sessions;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace ContentStoreTest.Distributed.Sessions
{
    [Collection("Redis-based tests")]
    [Trait("Category", "LongRunningTest")]
    public class DistributedContentSessionTests : ContentSessionTests
    {
        private readonly LocalRedisFixture _redis;

        private readonly LocalLocationStoreDistributedContentTests _tests;

        internal static IReadOnlyList<TimeSpan> DefaultRetryIntervalsForTest = new List<TimeSpan>()
        {
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(10),
        };

        public DistributedContentSessionTests(LocalRedisFixture redis, ITestOutputHelper output)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, canHibernate: true, output)
        {
            _redis = redis;
            _tests = new LocalLocationStoreDistributedContentTests(_redis, output);
        }

        [Fact]
        public Task OpenStreamWithBuildIdBasedSession()
        {
            Name = $"{Constants.BuildIdPrefix}{Guid.NewGuid()}";
            return OpenStreamExisting();
        }

        protected override Task RunTestAsync(
            ImplicitPin implicitPin,
            DisposableDirectory directory,
            Func<Context, IContentSession, Task> funcAsync)
        {
            if (directory is not null)
            {
                return Task.CompletedTask;
            }

            _tests.MaxSize = MaxSize.ToString();

            _tests.ConfigureWithOneMaster(d =>
            {
                d.UseFullEvictionSort = true;
            });

            return _tests.RunTestAsync(1, (testContext) =>
            {
                return funcAsync(testContext, testContext.Sessions[0]);
            }, implicitPin);
        }

        protected override Task RunReadOnlyTestAsync(ImplicitPin implicitPin, Func<Context, IReadOnlyContentSession, Task> funcAsync)
        {
            return RunTestAsync(implicitPin, null, (ctx, session) => funcAsync(ctx, session));
        }

        protected override IContentStore CreateStore(DisposableDirectory testDirectory, ContentStoreConfiguration configuration)
        {
            throw new NotImplementedException();
        }

        protected virtual DistributedContentStoreSettings CreateSettings()
        {
            throw new NotImplementedException();
        }

        public override void Dispose()
        {
            _tests.Dispose();
            base.Dispose();
        }
    }
}
