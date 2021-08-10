// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.ConfigurationHelper;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// ColdStorage is a store for handling large drives.
    /// It is queried after the MultiplexedStore and before the remote cache.
    /// The data is saved during eviction process of the <see cref="FileSystemContentStoreInternal"/>.
    /// </summary>
    public class ColdStorage : StartupShutdownSlimBase
    {
        private readonly IContentStore _store;

        private IContentSession? _session;

        protected const string SessionName = "cold_storage_session";

        protected const string ErrorMsg = "ColdStorage did not finish Startup yet";

        protected override Tracer Tracer { get; }  = new Tracer(nameof(ColdStorage));

        /// <summary>
        /// ColdStorage is owned by multiple FileSystemContentStoreInternal instances and each of
        /// wich will start and shut it down
        /// </summary>
        public override bool AllowMultipleStartupAndShutdowns => true;

        private readonly IAbsFileSystem _fileSystem;

        private readonly AbsolutePath _rootPath;

        public ColdStorage(IAbsFileSystem fileSystem, ColdStorageSettings coldStorageSettings)
        {
            _fileSystem = fileSystem;
            _rootPath = coldStorageSettings.GetAbsoulutePath();

            ConfigurationModel configurationModel
                = new ConfigurationModel(new ContentStoreConfiguration(new MaxSizeQuota(coldStorageSettings.CacheSizeQuotaString!)));

            ContentStoreSettings contentStoreSettings = FromColdStorageSettings(coldStorageSettings);

            _store = new FileSystemContentStore(fileSystem, SystemClock.Instance, _rootPath, configurationModel, null, contentStoreSettings, null);
        }

        private static ContentStoreSettings FromColdStorageSettings(ColdStorageSettings settings)
        {
            var result = new ContentStoreSettings()
            {
                CheckFiles = settings.CheckLocalFiles,
                SelfCheckSettings = CreateSelfCheckSettings(settings),
                OverrideUnixFileAccessMode = settings.OverrideUnixFileAccessMode,
                UseRedundantPutFileShortcut = settings.UseRedundantPutFileShortcut,
                TraceFileSystemContentStoreDiagnosticMessages = settings.TraceFileSystemContentStoreDiagnosticMessages,

                SkipTouchAndLockAcquisitionWhenPinningFromHibernation = settings.UseFastHibernationPin,
            };

            ApplyIfNotNull(settings.SilentOperationDurationThreshold, v => result.SilentOperationDurationThreshold = TimeSpan.FromSeconds(v));
            ApplyIfNotNull(settings.SilentOperationDurationThreshold, v => DefaultTracingConfiguration.DefaultSilentOperationDurationThreshold = TimeSpan.FromSeconds(v));
            ApplyIfNotNull(settings.DefaultPendingOperationTracingIntervalInMinutes, v => DefaultTracingConfiguration.DefaultPendingOperationTracingInterval = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(settings.ReserveSpaceTimeoutInMinutes, v => result.ReserveTimeout = TimeSpan.FromMinutes(v));

            ApplyIfNotNull(settings.UseAsynchronousFileStreamOptionByDefault, v => FileSystemDefaults.UseAsynchronousFileStreamOptionByDefault = v);

            ApplyIfNotNull(settings.UseHierarchicalTraceIds, v => Context.UseHierarchicalIds = v);

            return result;
        }

        private static SelfCheckSettings CreateSelfCheckSettings(ColdStorageSettings settings)
        {
            var selfCheckSettings = new SelfCheckSettings()
            {
                Epoch = settings.SelfCheckEpoch,
                StartSelfCheckInStartup = settings.StartSelfCheckAtStartup,
                Frequency = TimeSpan.FromMinutes(settings.SelfCheckFrequencyInMinutes),
            };

            ApplyIfNotNull(settings.SelfCheckProgressReportingIntervalInMinutes, minutes => selfCheckSettings.ProgressReportingInterval = TimeSpan.FromMinutes(minutes));
            ApplyIfNotNull(settings.SelfCheckDelayInMilliseconds, milliseconds => selfCheckSettings.HashAnalysisDelay = TimeSpan.FromMilliseconds(milliseconds));
            ApplyIfNotNull(settings.SelfCheckDefaultHddDelayInMilliseconds, milliseconds => selfCheckSettings.DefaultHddHashAnalysisDelay = TimeSpan.FromMilliseconds(milliseconds));

            return selfCheckSettings;
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await _store.StartupAsync(context).ThrowIfFailure();

            if (_fileSystem.DirectoryExists(_rootPath / "temp")) {
                _fileSystem.DeleteDirectory(_rootPath / "temp", DeleteOptions.All);
            }

            CreateSessionResult<IContentSession> sessionResult = _store.CreateSession(context, SessionName, ImplicitPin.None).ThrowIfFailure();
            _session = sessionResult.Session!;

            return await _session.StartupAsync(context);
        }

        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            BoolResult sessionResult = _session != null ? await _session.ShutdownAsync(context) : BoolResult.Success;
            BoolResult storeResult = await _store.ShutdownAsync(context);
            return sessionResult & storeResult;
        }

        public Task<PutResult> PutFileAsync(
            Context context,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            if (_session == null)
            {
                return Task.FromResult(new PutResult(new ContentHash(hashType), ErrorMsg));
            }
            return _session.PutFileAsync(context, hashType, path, realizationMode, cts, urgencyHint);
        }

        public Task<PutResult> PutFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            if (_session == null)
            {
                return Task.FromResult(new PutResult(contentHash, ErrorMsg));
            }
            return _session.PutFileAsync(context, contentHash, path, realizationMode, cts, urgencyHint);
        }

        public Task PutFileAsync(
            Context context,
            ContentHash contentHash,
            DisposableFile disposableFile)
        {
                return PutFileAsync(context, contentHash, disposableFile.Path, FileRealizationMode.Copy, CancellationToken.None, UrgencyHint.Low).ContinueWith(p =>
                {
                    disposableFile.Dispose();
                });
        }

        public Task<PlaceFileResult> PlaceFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            if (_session == null)
            {
                return Task.FromResult(new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound, ErrorMsg));
            }
            return _session.PlaceFileAsync(context, contentHash, path, accessMode, replacementMode, realizationMode, cts, urgencyHint);
        }

        public async Task<PlaceFileResult> CreateTempAndPutAsync(
            OperationContext context,
            ContentHash contentHash,
            IContentSession contentSession)
        {
            using (var disposableFile = new DisposableFile(context, _fileSystem, AbsolutePath.CreateRandomFileName(_rootPath / "temp")))
            {
                PlaceFileResult placeTempFileResult = await PlaceFileAsync(context, contentHash, disposableFile.Path, FileAccessMode.ReadOnly, FileReplacementMode.FailIfExists, FileRealizationMode.HardLink, context.Token);
                if (!placeTempFileResult.Succeeded)
                {
                    return placeTempFileResult;
                }
                PutResult putFileResult = await contentSession.PutFileAsync(context, contentHash, disposableFile.Path, FileRealizationMode.Any, context.Token);

                if (!putFileResult)
                {
                    return new PlaceFileResult(putFileResult);
                }
                else
                {
                    return new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy, putFileResult.ContentSize);
                }
            }
        }

        public async Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> FetchThenPutBulkAsync(OperationContext context, IReadOnlyList<ContentHashWithPath> args, IContentSession contentSession)
        {
            var putFilesBlock =
                new TransformBlock<Indexed<ContentHashWithPath>, Indexed<PlaceFileResult>>(
                    async indexed =>
                    {
                        return new Indexed<PlaceFileResult>(await CreateTempAndPutAsync(context, indexed.Item.Hash, contentSession), indexed.Index);
                    },
                    new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 8 });

            putFilesBlock.PostAll(args.AsIndexed());

            var copyFilesLocally =
                    await Task.WhenAll(
                        Enumerable.Range(0, args.Count).Select(i => putFilesBlock.ReceiveAsync(context.Token)));
            putFilesBlock.Complete();

            return copyFilesLocally.AsTasks();
        }

    }
}
