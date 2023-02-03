// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Distributed.Test.MetadataService;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using BuildXL.Cache.Host.Service.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities.Collections;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.Sessions
{
    public abstract class LocalLocationStoreDistributedContentTestsBase
        : LocalLocationStoreDistributedContentTestsBase<IContentStore, IContentSession>
    {
        protected LocalLocationStoreDistributedContentTestsBase(LocalRedisFixture redis, ITestOutputHelper output)
            : base(redis, output)
        {
        }

        protected override IContentStore UnwrapRootContentStore(IContentStore store)
        {
            return store;
        }

        protected override Task<GetStatsResult> GetStatsAsync(IContentStore store, Context context)
        {
            return store.GetStatsAsync(context);
        }

        protected override CreateSessionResult<IContentSession> CreateSession(IContentStore store, Context context, string name, ImplicitPin implicitPin)
        {
            return store.CreateSession(context, name, implicitPin);
        }

        protected override IContentStore CreateFromArguments(DistributedCacheServiceArguments arguments)
        {
            var factory = new DistributedContentStoreFactory(arguments);
            var topLevelStore = factory.CreateTopLevelStore().topLevelStore;
            return topLevelStore;
        }
    }

    public abstract class LocalLocationStoreDistributedContentTestsBase<TStore, TSession> : DistributedContentTests<TStore, TSession>
        where TSession : IContentSession
        where TStore : IStartupShutdown
    {
        protected static readonly Tracer Tracer = new Tracer(nameof(LocalLocationStoreDistributedContentTestsBase));

        protected const HashType ContentHashType = HashType.Vso0;
        protected const int ContentByteCount = 100;
        protected const int SafeToLazilyUpdateMachineCountThreshold = 3;
        protected const int ReplicaCreditInMinutes = 3;

        protected readonly LocalRedisFixture _redis;
        protected Action<TestDistributedContentSettings> _overrideDistributed = null;
        protected readonly ConcurrentDictionary<int, LocalLocationStoreConfiguration> _configurations
            = new();

        private const int InfiniteHeartbeatMinutes = 10_000;

        protected string _overrideScenarioName = Guid.NewGuid().ToString();

        protected bool _registerAdditionalLocationPerMachine = false;

        protected readonly ConcurrentDictionary<(string, int), AzuriteStorageProcess> _localStorages = new();

        protected Func<AbsolutePath, int, LocalLocationStoreConfiguration> CreateContentLocationStoreConfiguration { get; set; }
        protected AzuriteStorageProcess StorageProcess { get; private set; }
        protected AzuriteStorageProcess ContentMetadataStorageProcess { get; private set; }

        protected bool UseSeparateContentMetadataStorage { get; set; }

        protected AbsolutePath _redirectedSourcePath = new AbsolutePath(BuildXL.Utilities.OperatingSystemHelper.IsUnixOS ? "/X/cache" : @"X:\cache");
        protected AbsolutePath _redirectedTargetPath = new AbsolutePath(BuildXL.Utilities.OperatingSystemHelper.IsUnixOS ? "/X/cache" : @"X:\cache");

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

        public string MaxSize { get; set; } = "50 MB";

        protected Action<LocalLocationStoreConfiguration> _overrideLocationStore = null;
        protected Action<DistributedContentStoreSettings> _overrideDistributedContentStooreSettings = null;

        protected MemoryContentLocationEventStoreConfiguration MemoryEventStoreConfiguration { get; } = new MemoryContentLocationEventStoreConfiguration();

        protected TestHost Host { get; } = new TestHost();
        protected TestInfo[] TestInfos { get; private set; }

        public LocalLocationStoreDistributedContentTestsBase(LocalRedisFixture redis, ITestOutputHelper output)
            : base(output)
        {
            _redis = redis;
        }

        protected AzuriteStorageProcess GetStorage(Context context, ref int index)
        {
            index++;

            if (!_localStorages.TryGetValue((context.TraceId, index), out var localDatabase))
            {
                localDatabase = AzuriteStorageProcess.CreateAndStartEmpty(_redis, TestGlobal.Logger);
                _localStorages.TryAdd((context.TraceId, index), localDatabase);
            }

            return localDatabase;
        }

        public void ConfigureWithOneMaster(Action<TestDistributedContentSettings> overrideDistributed = null, Action<LocalLocationStoreConfiguration> overrideRedis = null)
        {
            _overrideDistributed = s =>
                                   {
                                       overrideDistributed?.Invoke(s);
                                   };
            _overrideLocationStore = overrideRedis;
        }

        protected override TestServerProvider CreateStore(
            Context context,
            IRemoteFileCopier fileCopier,
            DisposableDirectory testDirectory,
            int index,
            int iteration,
            uint grpcPort)
        {
            var rootPath = testDirectory.Path / "Root";

            int storageIndex = 0;
            StorageProcess = GetStorage(context, ref storageIndex);
            if (UseSeparateContentMetadataStorage)
            {
                ContentMetadataStorageProcess = GetStorage(context, ref storageIndex);
            }
            else
            {
                ContentMetadataStorageProcess = StorageProcess;
            }

            var verboseOperationLogging = new OperationLoggingConfiguration()
            {
                ErrorsOnly = false,
                StopMessage = true,
            };

            var settings = new TestDistributedContentSettings()
            {
                LogManager = new LogManagerConfiguration()
                {
                    Logs =
                    {
                        ["ResilientGlobalCacheService.*"] = verboseOperationLogging,
                        ["RedisWriteAheadEventStorage.*"] = verboseOperationLogging,
                    }
                },
                TestMachineIndex = index,
                TestIteration = iteration,
                IsDistributedContentEnabled = true,
                KeySpacePrefix = UniqueTestId,

                // By default, only first store is master eligible
                IsMasterEligible = index == 0,

                // Specify event hub and storage secrets even though they are not used in tests to satisfy DistributedContentStoreFactory
                EventHubSecretName = Host.StoreSecret("EventHub_Unspecified", "Unused"),
                AzureStorageSecretName = Host.StoreSecret("Storage", StorageProcess?.ConnectionString ?? "Unused"),
                ContentMetadataBlobSecretName = Host.StoreSecret("ContentMetadataBlob", ContentMetadataStorageProcess?.ConnectionString ?? "Unused"),

                UseDistributedCentralStorage = true,
                MachineActiveToClosedIntervalMinutes = 5,
                MachineActiveToExpiredIntervalMinutes = 10,

                SafeToLazilyUpdateMachineCountThreshold = SafeToLazilyUpdateMachineCountThreshold,

                RestoreCheckpointIntervalMinutes = 1,
                CreateCheckpointIntervalMinutes = 1,
                HeartbeatIntervalMinutes = 0.5,

                RetryIntervalForCopiesMs = DistributedContentSessionTests.DefaultRetryIntervalsForTest.Select(t => (int)t.TotalMilliseconds).ToArray(),

                CheckLocalFiles = true,

                // Tests disable reconciliation by default
                ReconcileMode = ReconciliationMode.None.ToString(),

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

                // Use very low to delay to keep tests with proactive replication from running a very long time
                ProactiveReplicationDelaySeconds = 0.001,

                ContentMetadataClientOperationTimeout = "1m",
                ContentMetadataClientConnectionTimeout = "1s",

                ContentLocationDatabaseOpenReadOnly = true,
                EnablePublishingCache = EnablePublishingCache,

                GrpcCopyClientConnectOnStartup = true,
            };

            if (ProactiveCopyLocationThreshold.HasValue)
            {
                settings.ProactiveCopyLocationsThreshold = ProactiveCopyLocationThreshold.Value;
            }

            _overrideDistributed?.Invoke(settings);

            var localCasSettings = new LocalCasSettings()
            {
                CasClientSettings = new LocalCasClientSettings()
                {
                    UseCasService = true,
                    DefaultCacheName = "Default",
                },
                DrivePreferenceOrder = new List<string>() { Path.GetPathRoot(rootPath.Path) },
                CacheSettings = new Dictionary<string, NamedCacheSettings>()
                                                       {
                                                           {
                                                               "Default",
                                                               new NamedCacheSettings()
                                                               {
                                                                   CacheRootPath = rootPath.Path,
                                                                   CacheSizeQuotaString = MaxSize,
                                                               }
                                                           }
                                                       },
                ServiceSettings = new LocalCasServiceSettings()
                {
                    GrpcPort = grpcPort,
                    GrpcPortFileName = Guid.NewGuid().ToString(),
                    ScenarioName = $"{_overrideScenarioName}_{index}",
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

            settings = ModifyDistributedContentSettingsAcrossAllTests(settings);

            var arguments = new DistributedCacheServiceArguments(
                new Context(Logger),
                new MockTelemetryFieldsProvider(),
                fileCopier,
                (IContentCommunicationManager)fileCopier,
                Host,
                new HostInfo("TestStamp", "TestRing", capabilities: new string[0]),
                Token,
                dataRootPath: localCasSettings.DefaultRootPath.Path,
                configuration: new DistributedCacheServiceConfiguration(localCasSettings, settings),
                keyspace: UniqueTestId,
                fileSystem: FileSystem
            )
            { Overrides = TestInfos[index].Overrides };

            arguments = ModifyArguments(arguments);
            TestInfos[index].Arguments = arguments;

            return CreateStore(context, arguments);
        }

        protected virtual TestDistributedContentSettings ModifyDistributedContentSettingsAcrossAllTests(TestDistributedContentSettings dcs)
        {
            return dcs;
        }

        protected virtual TestServerProvider CreateStore(Context context, DistributedCacheServiceArguments arguments)
        {
            if (UseGrpcServer)
            {
                var server = (ILocalContentServer<TStore>)new CacheServerFactory(arguments).CreateAsync(new OperationContext(context)).GetAwaiter().GetResult();
                TStore store = server.StoresByName["Default"];
                return new TestServerProvider(arguments, server, () => store);
            }
            else
            {
                var store = CreateFromArguments(arguments);
                return new TestServerProvider(arguments, store, () => store);
            }
        }

        protected virtual Task<BoolResult> CreateCheckpointAsync(InstanceRef storeRef, TestContext context)
        {
            return context.GetLocalLocationStore(storeRef.ResolveIndex(context)).HeartbeatAsync(context);
        }

        protected virtual Task<BoolResult> RestoreCheckpointAsync(InstanceRef storeRef, TestContext context)
        {
            return context.GetLocalLocationStore(storeRef.ResolveIndex(context)).HeartbeatAsync(context);
        }

        protected async Task UploadCheckpointOnMasterAndRestoreOnWorkers(TestContext context, bool reconcile = false, string clearStoragePrefix = null)
        {
            // Update time to trigger checkpoint upload and restore on master and workers respectively
            TestClock.UtcNow += TimeSpan.FromMinutes(2);

            var masterStore = context.GetMaster();

            // Heartbeat master first to upload checkpoint
            await CreateCheckpointAsync(masterStore, context).ShouldBeSuccess();

            if (clearStoragePrefix != null)
            {
                await StorageProcess.ClearAsync(clearStoragePrefix);
            }

            // Next heartbeat workers to restore checkpoint
            foreach (var workerStore in context.EnumerateWorkers())
            {
                await RestoreCheckpointAsync(workerStore, context).ShouldBeSuccess();
                // TODO: if reconcile, wait until we have completed reconciliation
            }
        }

        protected record struct InstanceRef(
            LocalLocationStore LocalLocationStore = null,
            TransitioningContentLocationStore LocationStore = null,
            int? Index = null)
        {
            public int ResolveIndex(TestContext context)
            {
                return Index
                    ?? ResolveIndex(context, LocalLocationStore, (c, i) => c.GetLocalLocationStore(i))
                    ?? ResolveIndex(context, LocationStore, (c, i) => c.GetLocationStore(i))
                    ?? throw Contract.AssertFailure("Could not find instance");
            }

            public int? ResolveIndex<T>(TestContext context, T instance, Func<TestContext, int, T> getIndexInstance)
                where T : class
            {
                for (int i = 0; i < context.Stores.Count; i++)
                {
                    if (getIndexInstance(context, i) == instance)
                    {
                        return i;
                    }
                }

                return null;
            }

            public static implicit operator InstanceRef(LocalLocationStore value) => new InstanceRef(LocalLocationStore: value);
            public static implicit operator InstanceRef(int value) => new InstanceRef(Index: value);
            public static implicit operator InstanceRef(TransitioningContentLocationStore value) => new InstanceRef(LocationStore: value);
        }

        protected async Task OpenStreamAndDisposeAsync(IContentSession session, Context context, ContentHash hash)
        {
            var openResult = await session.OpenStreamAsync(context, hash, Token).ShouldBeSuccess();
            using (openResult.Stream)
            {
                // Just dispose stream.
            }
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            foreach (var database in _localStorages.Values)
            {
                database.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void InitializeTestRun(int storeCount)
        {
            var persistentState = new MockPersistentEventStorageState();
            TestInfos = Enumerable.Range(0, storeCount).Select(i =>
            {
                return new TestInfo(i)
                {
                    Overrides = new TestHostOverrides(this, i)
                    {
                        PersistentState = persistentState
                    }
                };
            }).ToArray();

            base.InitializeTestRun(storeCount);
        }

        public record SessionAndStore(ICacheSession Session, IStartupShutdownSlim Store);

        public class TestDistributedContentSettings : DistributedContentSettings
        {
            public int TestMachineIndex { get; set; }

            public int TestIteration { get; set; } = 0;
        }

        protected record TestInfo(int Index)
        {
            public DistributedCacheServiceArguments Arguments;
            public SessionAndStore ClientSesssion;
            public TestHostOverrides Overrides;
        }

        protected class TestHost : IDistributedCacheServiceHost, IDistributedCacheServiceHostInternal
        {
            private readonly Dictionary<string, string> _secrets = new Dictionary<string, string>();

            public string StoreSecret(string key, string value)
            {
                if (value is null)
                {
                    return value;
                }

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

            public Task<RetrievedSecrets> RetrieveSecretsAsync(List<RetrieveSecretsRequest> requests, CancellationToken token)
            {
                return Task.FromResult(new RetrievedSecrets(requests.GroupBy(r => r.Name).Select(g => g.First()).ToDictionary(r => r.Name, r => (Secret)new PlainTextSecret(_secrets[r.Name]))));
            }

            public void OnStartedService() { }
            public Task OnStartingServiceAsync() => Task.CompletedTask;
            public void OnTeardownCompleted() { }
            public Task OnStartedServiceAsync(OperationContext context, ICacheServerServices services) => Task.CompletedTask;
            public Task OnStoppingServiceAsync(OperationContext context) => Task.CompletedTask;
        }

        protected class TestHostOverrides : DistributedCacheServiceHostOverrides
        {
            private readonly LocalLocationStoreDistributedContentTestsBase<TStore, TSession> _tests;
            private readonly int _storeIndex;

            public FailureMode PersistentFailureMode { get; set; } = FailureMode.None;
            public FailureMode VolatileFailureMode { get; set; } = FailureMode.None;
            public MockPersistentEventStorageState PersistentState { get; set; } = new MockPersistentEventStorageState();

            public TestHostOverrides(LocalLocationStoreDistributedContentTestsBase<TStore, TSession> tests, int storeIndex)
            {
                _tests = tests;
                _storeIndex = storeIndex;
            }

            public override IWriteBehindEventStorage Override(IWriteBehindEventStorage storage)
            {
                var failingStorage = new FailingPersistentEventStorage(PersistentFailureMode, storage);
                return failingStorage;
            }

            public override IWriteAheadEventStorage Override(IWriteAheadEventStorage storage)
            {
                var failingStorage = new FailingVolatileEventStorage(VolatileFailureMode, storage);
                return failingStorage;
            }

            public override IClock Clock => _tests.TestClock;

            public override void Override(LocalLocationStoreConfiguration configuration)
            {
                configuration.InlinePostInitialization = true;

                // Set recompute time to zero to force recomputation on every heartbeat
                configuration.MachineStateRecomputeInterval = TimeSpan.Zero;

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

                _tests._overrideLocationStore?.Invoke(configuration);

                _tests._configurations[_storeIndex] = configuration;

                if (!_tests.UseGrpcServer)
                {
                    var accessor = new TestGlobalCacheClientAccessor(_tests.GlobalTestContext);
                    configuration.GlobalCacheClientAccessorForTests = accessor;
                }
            }

            public override void Override(DistributedContentStoreSettings settings)
            {
                settings.InlineOperationsForTests = true;
                settings.SetPostInitializationCompletionAfterStartup = true;

                _tests._overrideDistributedContentStooreSettings?.Invoke(settings);
            }
        }

        protected class TestGlobalCacheClientAccessor : StartupShutdownSlimBase, IClientAccessor<MachineLocation, IGlobalCacheService>
        {
            protected override Tracer Tracer { get; } = new Tracer(nameof(TestGlobalCacheClientAccessor));

            private readonly BoxRef<TestContext> _context;

            public TestGlobalCacheClientAccessor(BoxRef<TestContext> context)
            {
                _context = context;
            }

            public Task<TResult> UseAsync<TResult>(OperationContext context, MachineLocation key, Func<IGlobalCacheService, Task<TResult>> operation)
            {
                var ctx = _context.Value;

                for (var i = 0; i < ctx.Stores.Count; i++)
                {
                    var location = ctx.GetLocationStore(i).LocalMachineLocation;

                    if (location.Equals(key))
                    {
                        var arg = ctx.GetContentMetadataService(i);
                        return operation(arg);
                    }
                }

                throw new InvalidOperationException($"Attempt to call {nameof(UseAsync)} with a non-existing machine location `{key}`");
            }
        }

        private class MockTelemetryFieldsProvider : ITelemetryFieldsProvider
        {
            public string BuildId => "BuildId";

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
