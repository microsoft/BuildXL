// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using BuildXL.Cache.Host.Service.Internal;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.Sessions
{
    public abstract class LocalLocationStoreDistributedContentTestsBase : DistributedContentTests
    {
        protected static readonly Tracer Tracer = new Tracer(nameof(LocalLocationStoreDistributedContentTestsBase));

        protected const HashType ContentHashType = HashType.Vso0;
        protected const int ContentByteCount = 100;
        protected const int SafeToLazilyUpdateMachineCountThreshold = 3;
        protected const int ReplicaCreditInMinutes = 3;

        protected readonly LocalRedisFixture _redis;
        protected Action<TestDistributedContentSettings> _overrideDistributed = null;
        protected readonly Dictionary<int, RedisContentLocationStoreConfiguration> _configurations
            = new Dictionary<int, RedisContentLocationStoreConfiguration>();

        private const int InfiniteHeartbeatMinutes = 10_000;

        protected string _overrideScenarioName = null;

        protected bool _enableSecondaryRedis = false;
        protected bool _poolSecondaryRedisDatabase = true;
        protected bool _registerAdditionalLocationPerMachine = false;

        protected readonly ConcurrentDictionary<(string, int), LocalRedisProcessDatabase> _localDatabases = new();

        protected Func<AbsolutePath, int, RedisContentLocationStoreConfiguration> CreateContentLocationStoreConfiguration { get; set; }
        protected LocalRedisProcessDatabase PrimaryGlobalStoreDatabase { get; private set; }
        protected LocalRedisProcessDatabase _secondaryGlobalStoreDatabase;

        protected AbsolutePath _redirectedSourcePath = new AbsolutePath(@"X:\cache");
        protected AbsolutePath _redirectedTargetPath = new AbsolutePath(@"X:\cache");

        protected bool EnableProactiveCopy { get; set; } = false;

        protected ProactiveCopyMode ProactiveCopyMode { get; set; } = ProactiveCopyMode.OutsideRing;
        protected int? ProactiveCopyLocationThreshold { get; set; } = null;
        protected int? ProactivePushCountLimit { get; set; }
        protected bool EnableProactiveReplication { get; set; } = false;
        protected bool ProactiveCopyOnPuts { get; set; } = true;
        protected bool ProactiveCopyOnPins { get; set; } = false;
        protected bool ProactiveCopyUsePreferredLocations { get; set; } = false;
        protected int ProactiveCopyRetries { get; set; } = 0;

        protected bool UseRealStorage { get; set; } = false;
        protected bool UseRealEventHub { get; set; } = false;

        protected bool EnablePublishingCache { get; set; } = false;

        protected Action<RedisContentLocationStoreConfiguration> _overrideRedis = null;
        protected Action<DistributedContentStoreSettings> _overrideDistributedContentStooreSettings = null;

        protected MemoryContentLocationEventStoreConfiguration MemoryEventStoreConfiguration { get; } = new MemoryContentLocationEventStoreConfiguration();

        protected TestHost Host { get; } = new TestHost();

        public LocalLocationStoreDistributedContentTestsBase(LocalRedisFixture redis, ITestOutputHelper output)
            : base(output)
        {
            _redis = redis;
        }

        protected LocalRedisProcessDatabase GetDatabase(Context context, ref int index, bool useDatabasePool = true)
        {
            if (!useDatabasePool)
            {
                return LocalRedisProcessDatabase.CreateAndStartEmpty(_redis, TestGlobal.Logger, SystemClock.Instance);
            }

            index++;

            if (!_localDatabases.TryGetValue((context.TraceId, index), out var localDatabase))
            {
                localDatabase = LocalRedisProcessDatabase.CreateAndStartEmpty(_redis, TestGlobal.Logger, SystemClock.Instance);
                _localDatabases.TryAdd((context.TraceId, index), localDatabase);
            }

            return localDatabase;
        }

        public void ConfigureWithOneMaster(Action<TestDistributedContentSettings> overrideDistributed = null, Action<RedisContentLocationStoreConfiguration> overrideRedis = null)
        {
            _overrideDistributed = s =>
                                   {
                                       s.IsPinBetterEnabled = true;
                                       overrideDistributed?.Invoke(s);
                                   };
            _overrideRedis = overrideRedis;
        }

        protected override (IContentStore store, IStartupShutdown server) CreateStore(
            Context context,
            IRemoteFileCopier fileCopier,
            DisposableDirectory testDirectory,
            int index,
            int iteration,
            uint grpcPort)
        {
            var rootPath = testDirectory.Path / "Root";

            int dbIndex = 0;
            PrimaryGlobalStoreDatabase = GetDatabase(context, ref dbIndex);
            if (_enableSecondaryRedis)
            {
                _secondaryGlobalStoreDatabase = GetDatabase(context, ref dbIndex, _poolSecondaryRedisDatabase);
            }

            var settings = new TestDistributedContentSettings()
                           {
                               TestMachineIndex = index,
                               TestIteration = iteration,
                               IsDistributedContentEnabled = true,
                               KeySpacePrefix = "TestPrefix",

                               // By default, only first store is master eligible
                               IsMasterEligible = index == 0,

                               GlobalRedisSecretName = Host.StoreSecret("PrimaryRedis", PrimaryGlobalStoreDatabase.ConnectionString),
                               SecondaryGlobalRedisSecretName = _enableSecondaryRedis ? Host.StoreSecret("SecondaryRedis", _secondaryGlobalStoreDatabase.ConnectionString) : null,
                               RedisInternalLogSeverity = Severity.Info.ToString(),

                               // Specify event hub and storage secrets even thoug they are not used in tests to satisfy DistributedContentStoreFactory
                               EventHubSecretName = Host.StoreSecret("EventHub_Unspecified", "Unused"),
                               AzureStorageSecretName = Host.StoreSecret("Storage_Unspecified", "Unused"),

                               IsContentLocationDatabaseEnabled = true,
                               UseDistributedCentralStorage = true,
                               RedisMemoizationExpiryTimeMinutes = 60,
                               MachineActiveToClosedIntervalMinutes = 5,
                               MachineActiveToExpiredIntervalMinutes = 10,
                               IsDistributedEvictionEnabled = true,
                               IsRepairHandlingEnabled = true,

                               UseUnsafeByteStringConstruction = true,

                               SafeToLazilyUpdateMachineCountThreshold = SafeToLazilyUpdateMachineCountThreshold,

                               RestoreCheckpointIntervalMinutes = 1,
                               CreateCheckpointIntervalMinutes = 1,
                               HeartbeatIntervalMinutes = InfiniteHeartbeatMinutes,

                               RetryIntervalForCopiesMs = DistributedContentSessionTests.DefaultRetryIntervalsForTest.Select(t => (int)t.TotalMilliseconds).ToArray(),

                               RedisBatchPageSize = 1,
                               CheckLocalFiles = true,

                               // Tests disable reconciliation by default
                               ReconcileMode = ReconciliationMode.None.ToString(),

                               IsPinBetterEnabled = false,
                               PinMinUnverifiedCount = 1,
                               // Low risk and high risk tolerance for machine or file loss to prevent pin better from kicking in
                               MachineRisk = 0.0000001,

                               TraceProactiveCopy = true,
                               ProactiveCopyMode = EnableProactiveCopy ? ProactiveCopyMode.ToString() : nameof(ProactiveCopyMode.Disabled),
                               PushProactiveCopies = true,
                               EnableProactiveReplication = EnableProactiveReplication,
                               ProactiveCopyRejectOldContent = true,
                               ProactiveCopyOnPut = ProactiveCopyOnPuts,
                               ProactiveCopyOnPin = ProactiveCopyOnPins,
                               ProactiveCopyGetBulkBatchSize = 1,
                               ProactiveCopyUsePreferredLocations = ProactiveCopyUsePreferredLocations,
                               ProactiveCopyMaxRetries = ProactiveCopyRetries,

                               ContentLocationDatabaseOpenReadOnly = true,
                               EnablePublishingCache = EnablePublishingCache,
                           };

            if (ProactiveCopyLocationThreshold.HasValue)
            {
                settings.ProactiveCopyLocationsThreshold = ProactiveCopyLocationThreshold.Value;
            }

            _overrideDistributed?.Invoke(settings);

            var localCasSettings = new LocalCasSettings()
                                   {
                                       UseScenarioIsolation = false,
                                       CasClientSettings = new LocalCasClientSettings()
                                                           {
                                                               UseCasService = true,
                                                               DefaultCacheName = "Default",
                                                           },
                                       PreferredCacheDrive = Path.GetPathRoot(rootPath.Path),
                                       CacheSettings = new Dictionary<string, NamedCacheSettings>()
                                                       {
                                                           {
                                                               "Default",
                                                               new NamedCacheSettings()
                                                               {
                                                                   CacheRootPath = rootPath.Path,
                                                                   CacheSizeQuotaString = "50MB"
                                                               }
                                                           }
                                                       },
                                       ServiceSettings = new LocalCasServiceSettings()
                                                         {
                                                             GrpcPort = grpcPort,
                                                             GrpcPortFileName = Guid.NewGuid().ToString(),
                                                             ScenarioName = _overrideScenarioName ?? Guid.NewGuid().ToString(),
                                                             MaxProactivePushRequestHandlers = ProactivePushCountLimit,
                                                         }
                                   };

            if (_registerAdditionalLocationPerMachine)
            {
                localCasSettings.CacheSettings["RedirectedCas"] = new NamedCacheSettings()
                                                                  {
                                                                      CacheRootPath = (_redirectedSourcePath / index.ToString()).Path,
                                                                      CacheSizeQuotaString = "1MB"
                                                                  };
            }

            var configuration = new DistributedCacheServiceConfiguration(localCasSettings, settings);

            var arguments = new DistributedCacheServiceArguments(
                Logger,
                fileCopier,
                (IContentCommunicationManager)fileCopier,
                Host,
                new HostInfo("TestStamp", "TestRing", capabilities: new string[0]),
                Token,
                dataRootPath: rootPath.Path,
                configuration: configuration,
                keyspace: ContentLocationStoreFactory.DefaultKeySpace,
                fileSystem: FileSystem
            );

            arguments.Overrides = new TestHostOverrides(this, index);

            if (UseGrpcServer)
            {
                var server = (ILocalContentServer<IContentStore>)new CacheServerFactory(arguments).Create();
                IContentStore store = server.StoresByName["Default"];
                if (store is MultiplexedContentStore multiplexedStore)
                {
                    store = multiplexedStore.PreferredContentStore;
                }

                return (store, server);
            }
            else
            {
                var factory = new DistributedContentStoreFactory(arguments);

                var topLevelStore = factory.CreateTopLevelStore().topLevelStore;
                return (topLevelStore, null);
            }
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            foreach (var database in _localDatabases.Values)
            {
                database.Dispose();
            }

            if (!_poolSecondaryRedisDatabase && !_secondaryGlobalStoreDatabase.Closed)
            {
                _secondaryGlobalStoreDatabase.Dispose(close: true);
            }

            base.Dispose(disposing);
        }

        public class TestDistributedContentSettings : DistributedContentSettings
        {
            public int TestMachineIndex { get; set; }

            public int TestIteration { get; set; } = 0;
        }

        protected class TestHost : IDistributedCacheServiceHost
        {
            private readonly Dictionary<string, string> _secrets = new Dictionary<string, string>();

            public string StoreSecret(string key, string value)
            {
                _secrets[key] = value;
                return key;
            }

            public void RequestTeardown(string reason)
            {
            }

            public string GetSecretStoreValue(string key)
            {
                return _secrets[key];
            }

            public Task<Dictionary<string, Secret>> RetrieveSecretsAsync(List<RetrieveSecretsRequest> requests, CancellationToken token)
            {
                return Task.FromResult(requests.ToDictionary(r => r.Name, r => (Secret)new PlainTextSecret(_secrets[r.Name])));
            }

            public void OnStartedService() { }
            public Task OnStartingServiceAsync() => Task.CompletedTask;
            public void OnTeardownCompleted() { }
        }

        protected class TestHostOverrides : DistributedCacheServiceHostOverrides
        {
            private readonly LocalLocationStoreDistributedContentTestsBase _tests;
            private readonly int _storeIndex;

            public TestHostOverrides(LocalLocationStoreDistributedContentTestsBase tests, int storeIndex)
            {
                _tests = tests;
                _storeIndex = storeIndex;
            }

            public override IClock Clock => _tests.TestClock;

            public override void Override(RedisContentLocationStoreConfiguration configuration)
            {
                configuration.InlinePostInitialization = true;

                // Set recompute time to zero to force recomputation on every heartbeat
                configuration.MachineStateRecomputeInterval = TimeSpan.Zero;

                if (!_tests.UseRealStorage)
                {
                    configuration.CentralStore = new LocalDiskCentralStoreConfiguration(_tests.TestRootDirectoryPath / "centralStore", "checkpoints-key");
                }

                if (!_tests.UseRealEventHub)
                {
                    // Propagate epoch from normal configuration to in-memory configuration
                    _tests.MemoryEventStoreConfiguration.Epoch = configuration.EventStore.Epoch;
                    configuration.EventStore = _tests.MemoryEventStoreConfiguration;
                }

                if (configuration.CentralStore is BlobCentralStoreConfiguration blobConfig)
                {
                    blobConfig.EnableGarbageCollect = false;
                }

                _tests._overrideRedis?.Invoke(configuration);

                _tests._configurations[_storeIndex] = configuration;
            }

            public override void Override(DistributedContentStoreSettings settings)
            {
                settings.InlineOperationsForTests = true;
                settings.SetPostInitializationCompletionAfterStartup = true;

                _tests._overrideDistributedContentStooreSettings?.Invoke(settings);
            }
        }
    }
}
