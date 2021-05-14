// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.ContentStore.Vsts;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Vsts.Internal;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Content.Common.Authentication;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Vsts
{
    /// <summary>
    /// Publishes metadata to the BuildCache service.
    /// </summary>
    public class BuildCachePublishingSession : StartupShutdownSlimBase, IPublishingSession
    {
        /// <nodoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(BuildCachePublishingSession));

        private readonly string _name;
        private readonly BuildCacheServiceConfiguration _config;
        private readonly SemaphoreSlim _publishingGate;
        private readonly string _pat;
        private readonly IAbsFileSystem _fileSystem;

        private ICachePublisher? _publisher;

        /// <summary>
        /// The publishing session needs somewhere to get content from in case it needs to publish a
        /// content hash list's contents. This should point towards some locally available cache.
        /// </summary>
        private readonly IContentSession _sourceContentSession;

        /// <nodoc />
        public BuildCachePublishingSession(BuildCacheServiceConfiguration config, string name, string pat, IContentSession sourceContentSession, IAbsFileSystem fileSystem, SemaphoreSlim publishGate)
        {
            _name = name;
            _config = config;
            _publishingGate = publishGate;
            _pat = pat;
            _fileSystem = fileSystem;
            _sourceContentSession = sourceContentSession;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await _sourceContentSession.StartupAsync(context).ThrowIfFailure();

            _publisher = CreatePublisher(_name, _config, _pat, context);
            await _publisher.StartupAsync(context).ThrowIfFailure();

            return BoolResult.Success;
        }

        /// <nodoc />
        protected virtual ICachePublisher CreatePublisher(string sessionName, BuildCacheServiceConfiguration config, string pat, Context context)
        {
            var credHelper = new VsoCredentialHelper();
            var credFactory = new VssCredentialsFactory(new VssBasicCredential(new NetworkCredential(string.Empty, pat)));

            var cache = BuildCacheCacheFactory.Create(
                _fileSystem,
                context.Logger,
                credFactory,
                config,
                writeThroughContentStoreFunc: null);

            cache.StartupAsync(context).GetAwaiter().GetResult().ThrowIfFailure();

            var sessionResult = cache.CreateSession(context, sessionName, ImplicitPin.None).ThrowIfFailure();
            var session = sessionResult.Session;

            Contract.Check(session is BuildCacheSession)?.Assert($"Session should be an instance of {nameof(BuildCacheSession)}. Actual type: {session.GetType()}");

            return (BuildCacheSession)session;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var publisherResult = BoolResult.Success;
            if (_publisher != null)
            {
                publisherResult = await _publisher.ShutdownAsync(context).ThrowIfFailure();
            }

            var contentSessionResult = await _sourceContentSession.ShutdownAsync(context);

            return publisherResult & contentSessionResult;
        }

        /// <inheritdoc />
        public Task<BoolResult> PublishContentHashListAsync(
            Context context,
            StrongFingerprint fingerprint,
            ContentHashListWithDeterminism contentHashList,
            CancellationToken token)
        {
            Contract.Check(_publisher != null)?.Assert("Startup should be run before attempting to publish.");

            var operationContext = new OperationContext(context, token);

            Tracer.Debug(operationContext, $"Enqueueing publish request for StrongFingerprint=[{fingerprint}], CHL=[{contentHashList.ToTraceString()}]");

            return _publishingGate.GatedOperationAsync(
                (timeSpentWaiting, gateCount) =>
                {
                    ContentHashList? hashListInRemote = null;
                    return operationContext.PerformOperationAsync(
                        Tracer,
                        async () =>
                        {
                            var remotePinResults = await Task.WhenAll(await _publisher.PinAsync(operationContext, contentHashList.ContentHashList.Hashes, token));
                            var missingFromRemote = remotePinResults
                                .Where(r => !r.Item.Succeeded)
                                .Select(r => contentHashList.ContentHashList.Hashes[r.Index])
                                .ToArray();

                            if (missingFromRemote.Length > 0)
                            {
                                await PushToRemoteAsync(operationContext, missingFromRemote).ThrowIfFailure();
                            }

                            var addOrGetResult = await _publisher.AddOrGetContentHashListAsync(operationContext, fingerprint, contentHashList, token).ThrowIfFailure();
                            hashListInRemote = addOrGetResult.ContentHashListWithDeterminism.ContentHashList;

                            return BoolResult.Success;
                        },
                        traceOperationStarted: false,
                        extraEndMessage: result =>
                            $"Added=[{result.Succeeded && hashListInRemote is null}], " +
                            $"StrongFingerprint=[{fingerprint}], " +
                            $"ContentHashList=[{contentHashList.ToTraceString()}], " +
                            $"TimeSpentWaiting=[{timeSpentWaiting}], " +
                            $"GateCount=[{gateCount}]");
                },
                token);
        }

        private async Task<BoolResult> PushToRemoteAsync(OperationContext context, IReadOnlyList<ContentHash> hashes)
        {
            Contract.Check(_publisher != null)?.Assert("Startup should be run before attempting to publish.");

            var pinResults = await Task.WhenAll(await _sourceContentSession.PinAsync(context, hashes, context.Token));
            var missingFromLocal = pinResults.Where(r => !r.Item.Succeeded);
            if (missingFromLocal.Any())
            {
                return new BoolResult($"Not all contents of the content hash list are available in the cache. Missing hashes: {string.Join(", ", missingFromLocal.Select(m => hashes[m.Index].ToShortString()))}");
            }

            // TODO: concurrency?
            foreach (var hash in hashes)
            {
                var streamResult = await _sourceContentSession.OpenStreamAsync(context, hash, context.Token).ThrowIfFailure();
                var stream = streamResult.Stream;

                var putStreamResult = await _publisher.PutStreamAsync(context, hash, stream, context.Token).ThrowIfFailure();
            }

            return BoolResult.Success;
        }
    }
}
