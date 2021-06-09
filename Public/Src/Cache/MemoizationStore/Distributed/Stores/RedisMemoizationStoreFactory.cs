// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;

namespace BuildXL.Cache.MemoizationStore.Distributed.Stores
{
    /// <nodoc />
    public class RedisMemoizationStoreFactory : ContentLocationStoreFactory
    {
        /// <nodoc />
        protected new RedisMemoizationStoreConfiguration Configuration => (RedisMemoizationStoreConfiguration)base.Configuration;

        /// <nodoc />
        public RedisMemoizationStoreFactory(
            ContentLocationStoreFactoryArguments arguments,
            RedisMemoizationStoreConfiguration configuration)
            : base(arguments, configuration)
        {
        }

        /// <nodoc />
        public IMemoizationStore CreateMemoizationStore(ILogger logger)
        {
            var primaryRedisDatabaseFactory = RedisDatabaseFactoryForRedisGlobalStore;
            Contract.Assert(primaryRedisDatabaseFactory != null);
            var primaryRedisDatabaseAdapter = CreateDatabase(primaryRedisDatabaseFactory);

            var secondaryRedisDatabaseFactory = RedisDatabaseFactoryForRedisGlobalStoreSecondary;
            Contract.Assert(secondaryRedisDatabaseFactory != null);
            var secondaryRedisDatabaseAdapter = CreateDatabase(secondaryRedisDatabaseFactory, optional: true);

            var memoizationDb = new RedisMemoizationDatabase(
                primaryRedisDatabaseAdapter,
                secondaryRedisDatabaseAdapter,
                Clock,
                Configuration.MemoizationExpiryTime,
                Configuration.MemoizationOperationTimeout,
                Configuration.MemoizationSlowOperationCancellationTimeout);
            return new RedisMemoizationStore(logger, memoizationDb);
        }

        private RedisDatabaseAdapter CreateDatabase(RedisDatabaseFactory factory, bool optional = false)
        {
            if (factory != null)
            {
                return new RedisDatabaseAdapter(factory, KeySpace);
            }
            else
            {
                Contract.Assert(optional);
                return null;
            }
        }
    }
}
