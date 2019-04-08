using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// ContentStore that is empty and does not accept content. Useful when we want to disable content; specifically, to bypass talking to VSTS/CASaaS when unnecessary.
    /// </summary>
    public class ReadOnlyEmptyContentStore : StartupShutdownBase, IContentStore
    {
        private readonly ContentStoreTracer _tracer = new ContentStoreTracer(nameof(ReadOnlyEmptyContentStore));

        /// <inheritdoc />
        protected override Tracer Tracer => _tracer;

        /// <inheritdoc />
        public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin) => new CreateSessionResult<IReadOnlyContentSession>(new ReadOnlyEmptyContentSession(name));

        /// <inheritdoc />
        public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin) => new CreateSessionResult<IContentSession>(new ReadOnlyEmptyContentSession(name));

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context) => Task.FromResult(new GetStatsResult(_tracer.GetCounters()));
    }

    /// <summary>
    /// ContentSession is empty and does not accept content. Useful when we want to disable content; specifically, to bypass talking to VSTS/CASaaS when unnecessary.
    /// </summary>
    public class ReadOnlyEmptyContentSession : StartupShutdownBase, IContentSession
    {
        /// <nodoc />
        public ReadOnlyEmptyContentSession(string name)
        {
            Name = name;
        }

        private const string ErrorMessage = "Unsupported operation.";

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(ReadOnlyEmptyContentSession));

        /// <inheritdoc />
        public string Name { get; private set; }

        /// <inheritdoc />
        public Task<OpenStreamResult> OpenStreamAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
            => Task.FromResult(new OpenStreamResult(null)); // Null stream signals failue.

        /// <inheritdoc />
        public Task<PinResult> PinAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
            => Task.FromResult(new PinResult(PinResult.ResultCode.ContentNotFound));

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
            => Task.FromResult(contentHashes.Select((hash, i) => Task.FromResult(new PinResult(PinResult.ContentNotFound).WithIndex(i))));

        /// <inheritdoc />
        public Task<PlaceFileResult> PlaceFileAsync(Context context, ContentHash contentHash, AbsolutePath path, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
            => Task.FromResult(new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound));

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(Context context, IReadOnlyList<ContentHashWithPath> hashesWithPaths, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
            => Task.FromResult(hashesWithPaths.Select((hash, i) => Task.FromResult(new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound).WithIndex(i))));

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(Context context, HashType hashType, AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
            => Task.FromResult(new PutResult(new BoolResult(ErrorMessage)));

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(Context context, ContentHash contentHash, AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
            => Task.FromResult(new PutResult(contentHash, ErrorMessage));

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(Context context, HashType hashType, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
            => Task.FromResult(new PutResult(new BoolResult(ErrorMessage)));

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(Context context, ContentHash contentHash, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
            => Task.FromResult(new PutResult(contentHash, ErrorMessage));
    }
}
