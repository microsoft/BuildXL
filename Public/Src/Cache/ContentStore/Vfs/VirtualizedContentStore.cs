// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.ContentStore.Vfs.Provider;

namespace BuildXL.Cache.ContentStore.Vfs
{
    /// <summary>
    /// A store which virtualizes calls to an underlying content store (i.e. content will
    /// be lazily materialized using the projected file system filter driver)
    /// </summary>
    public class VirtualizedContentStore : StartupShutdownBase, IContentStore
    {
        private readonly IContentStore _innerStore;
        internal VfsCasConfiguration Configuration { get; }
        private readonly ILogger _logger;
        internal VfsTree Tree { get; }

        /// <summary>
        /// Internal for testing purposes only.
        /// </summary>
        internal VfsProvider Provider { get; set; }
        private VfsContentManager _contentManager;
        private IContentSession _vfsContentSession;

        protected override Tracer Tracer { get; } = new Tracer(nameof(VirtualizedContentStore));

        /// <nodoc />
        public VirtualizedContentStore(IContentStore innerStore, ILogger logger, VfsCasConfiguration configuration)
        {
            _logger = logger;
            _innerStore = innerStore;
            Configuration = configuration;

            Tree = new VfsTree(Configuration);
        }

        /// <inheritdoc />
        public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateSessionCore<IReadOnlyContentSession>(context, name, implicitPin);
        }

        /// <inheritdoc />
        public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateSessionCore<IContentSession>(context, name, implicitPin);
        }

        private CreateSessionResult<T> CreateSessionCore<T>(Context context, string name, ImplicitPin implicitPin)
            where T : class, IName
        {
            var operationContext = OperationContext(context);
            return operationContext.PerformOperation(
                Tracer,
                () =>
                {
                    var innerSessionResult = _innerStore.CreateSession(context, name, implicitPin).ThrowIfFailure();
                    var session = new VirtualizedContentSession(this, innerSessionResult.Session, _contentManager, name);
                    return new CreateSessionResult<T>(session as T);
                });
        }

        /// <inheritdoc />
        public async Task<GetStatsResult> GetStatsAsync(Context context)
        {
            var result = await _innerStore.GetStatsAsync(context);
            if (result.Succeeded)
            {
                var counters = result.CounterSet;
                if (_contentManager != null)
                {
                    counters.Merge(_contentManager.Counters.ToCounterSet(), "Vfs.");
                }

                return new GetStatsResult(counters);
            }
            else
            {
                return result;
            }
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await _innerStore.StartupAsync(context).ThrowIfFailure();

            // Create long-lived session to be used with overlay (ImplicitPin=None (i.e false) to avoid cache full errors)
            _vfsContentSession = _innerStore.CreateSession(context, "VFSInner", ImplicitPin.None).ThrowIfFailure().Session;
            await _vfsContentSession.StartupAsync(context).ThrowIfFailure();

            _contentManager = new VfsContentManager(_logger, Configuration, Tree, _vfsContentSession);
            Provider = new VfsProvider(_logger, Configuration, _contentManager, Tree);

            if (!Provider.StartVirtualization())
            {
                return new BoolResult("Unable to start virtualizing");
            }

            return await base.StartupCoreAsync(context);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            Provider?.StopVirtualization();

            // Close all sessions?
            var result = await base.ShutdownCoreAsync(context);

            result &= await _vfsContentSession.ShutdownAsync(context);

            result &= await _innerStore.ShutdownAsync(context);

            return result;
        }

        /// <inheritdoc />
        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions deleteOptions = null) => _innerStore.DeleteAsync(context, contentHash, deleteOptions);

        /// <inheritdoc />
        public void PostInitializationCompleted(Context context, BoolResult result) => _innerStore.PostInitializationCompleted(context, result);
    }
}
