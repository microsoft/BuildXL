// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    internal class TestContentStore : IContentStore
    {
        private IContentSession _contentSession;

        public bool StartupCompleted => throw new NotImplementedException();

        public bool StartupStarted => throw new NotImplementedException();

        public bool ShutdownCompleted => throw new NotImplementedException();

        public bool ShutdownStarted => throw new NotImplementedException();

        public TestContentStore(IContentSession testContentSession)
        {
            _contentSession = testContentSession;
        }

        public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            return new CreateSessionResult<IReadOnlyContentSession>(_contentSession);
        }

        public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return new CreateSessionResult<IContentSession>(_contentSession);
        }

        public void Dispose()
        {
        }

        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return Task.FromResult(new GetStatsResult(new CounterSet()));
        }

        public Task<BoolResult> ShutdownAsync(Context context)
        {
            return Task.FromResult(BoolResult.Success);
        }

        public Task<BoolResult> StartupAsync(Context context)
        {
            return Task.FromResult(BoolResult.Success);
        }
    }

    internal class TestContentSession : IContentSession
    {
        public HashSet<ContentHash> Pinned = new HashSet<ContentHash>();
        public HashSet<ContentHash> OpenStreamed = new HashSet<ContentHash>();
        public HashSet<Tuple<ContentHash, AbsolutePath, FileAccessMode, FileReplacementMode, FileRealizationMode>> FilePlacedParams = new HashSet<Tuple<ContentHash, AbsolutePath, FileAccessMode, FileReplacementMode, FileRealizationMode>>();
        public HashSet<Tuple<HashType, AbsolutePath, FileRealizationMode>> PutFileHashTypeParams = new HashSet<Tuple<HashType, AbsolutePath, FileRealizationMode>>();
        public HashSet<Tuple<ContentHash, AbsolutePath, FileRealizationMode>> PutFileHashParams = new HashSet<Tuple<ContentHash, AbsolutePath, FileRealizationMode>>();
        public HashSet<HashType> PutStreamHashTypeParams = new HashSet<HashType>();
        public HashSet<ContentHash> PutStreamHashParams = new HashSet<ContentHash>();

        public string Name => throw new NotImplementedException();

        public bool StartupCompleted => throw new NotImplementedException();

        public bool StartupStarted => throw new NotImplementedException();

        public bool ShutdownCompleted => throw new NotImplementedException();

        public bool ShutdownStarted => throw new NotImplementedException();

        public void Dispose()
        {
        }

        public Task<OpenStreamResult> OpenStreamAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            OpenStreamed.Add(contentHash);
            return Task.FromResult(new OpenStreamResult(new MemoryStream()));
        }

        public Task<PinResult> PinAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            Pinned.Add(contentHash);
            return Task.FromResult(PinResult.Success);
        }

        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new NotImplementedException();
        }

        public Task<PlaceFileResult> PlaceFileAsync(Context context, ContentHash contentHash, AbsolutePath path, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            FilePlacedParams.Add(new Tuple<ContentHash, AbsolutePath, FileAccessMode, FileReplacementMode, FileRealizationMode>(contentHash, path, accessMode, replacementMode, realizationMode));
            return Task.FromResult(new PlaceFileResult(PinResult.Success));
        }

        public Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(Context context, IReadOnlyList<ContentHashWithPath> hashesWithPaths, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new NotImplementedException();
        }

        public Task<PutResult> PutFileAsync(Context context, HashType hashType, AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            PutFileHashTypeParams.Add(new Tuple<HashType, AbsolutePath, FileRealizationMode>(hashType, path, realizationMode));
            return Task.FromResult(new PutResult(ContentHash.Random(hashType), 200));
        }

        public Task<PutResult> PutFileAsync(Context context, ContentHash contentHash, AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            PutFileHashParams.Add(new Tuple<ContentHash, AbsolutePath, FileRealizationMode>(contentHash, path, realizationMode));
            return Task.FromResult(new PutResult(contentHash, 200));
        }

        public Task<PutResult> PutStreamAsync(Context context, HashType hashType, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            PutStreamHashTypeParams.Add(hashType);
            return Task.FromResult(new PutResult(ContentHash.Random(hashType), 200));
        }

        public Task<PutResult> PutStreamAsync(Context context, ContentHash contentHash, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            PutStreamHashParams.Add(contentHash);
            return Task.FromResult(new PutResult(contentHash, 200));
        }

        public Task<BoolResult> ShutdownAsync(Context context)
        {
            return Task.FromResult(BoolResult.Success);
        }

        public Task<BoolResult> StartupAsync(Context context)
        {
            return Task.FromResult(BoolResult.Success);
        }
    }

    internal class TestMemoizationStore : IMemoizationStore
    {
        private IMemoizationSession _memoizationSession;

        public bool StartupCompleted => throw new NotImplementedException();

        public bool StartupStarted => throw new NotImplementedException();

        public bool ShutdownCompleted => throw new NotImplementedException();

        public bool ShutdownStarted => throw new NotImplementedException();

        public TestMemoizationStore(IMemoizationSession iMemoizationSession)
        {
            _memoizationSession = iMemoizationSession;
        }

        public CreateSessionResult<IReadOnlyMemoizationSession> CreateReadOnlySession(Context context, string name)
        {
            return new CreateSessionResult<IReadOnlyMemoizationSession>(_memoizationSession);
        }

        public CreateSessionResult<IMemoizationSession> CreateSession(Context context, string name)
        {
            return new CreateSessionResult<IMemoizationSession>(_memoizationSession);
        }

        public CreateSessionResult<IMemoizationSession> CreateSession(Context context, string name, IContentSession contentSession)
        {
            return new CreateSessionResult<IMemoizationSession>(_memoizationSession);
        }

        public void Dispose()
        {
        }

        public Async::System.Collections.Generic.IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context)
        {
            throw new NotImplementedException();
        }

        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return Task.FromResult(new GetStatsResult(new CounterSet()));
        }

        public Task<BoolResult> ShutdownAsync(Context context)
        {
            return Task.FromResult(BoolResult.Success);
        }

        public Task<BoolResult> StartupAsync(Context context)
        {
            return Task.FromResult(BoolResult.Success);
        }
    }

    internal class TestMemoizationSession : IMemoizationSession, IReadOnlyMemoizationSessionWithLevelSelectors
    {
        public HashSet<Fingerprint> GetSelectorsParams = new HashSet<Fingerprint>();
        public HashSet<StrongFingerprint> GetContentHashListAsyncParams = new HashSet<StrongFingerprint>();
        public HashSet<Tuple<StrongFingerprint, ContentHashListWithDeterminism>> AddOrGetContentHashListAsyncParams = new HashSet<Tuple<StrongFingerprint, ContentHashListWithDeterminism>>();
        public HashSet<IEnumerable<Task<StrongFingerprint>>> IncorporateStringFingerprintsAsyncParams = new HashSet<IEnumerable<Task<StrongFingerprint>>>();

        public string Name => throw new NotImplementedException();

        public bool StartupCompleted => throw new NotImplementedException();

        public bool StartupStarted => throw new NotImplementedException();

        public bool ShutdownCompleted => throw new NotImplementedException();

        public bool ShutdownStarted => throw new NotImplementedException();

        public Task<AddOrGetContentHashListResult> AddOrGetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, ContentHashListWithDeterminism contentHashListWithDeterminism, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            AddOrGetContentHashListAsyncParams.Add(new Tuple<StrongFingerprint, ContentHashListWithDeterminism>(strongFingerprint, contentHashListWithDeterminism));
            return Task.FromResult(new AddOrGetContentHashListResult(contentHashListWithDeterminism));
        }

        public void Dispose()
        {
        }

        public Task<GetContentHashListResult> GetContentHashListAsync(Context context, StrongFingerprint strongFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            GetContentHashListAsyncParams.Add(strongFingerprint);
            return Task.FromResult(new GetContentHashListResult(new ContentHashListWithDeterminism()));
        }

        /// <inheritdoc />
        public Async::System.Collections.Generic.IAsyncEnumerable<GetSelectorResult> GetSelectors(Context context, Fingerprint weakFingerprint, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            return this.GetSelectorsAsAsyncEnumerable(context, weakFingerprint, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<Result<LevelSelectors>> GetLevelSelectorsAsync(Context context, Fingerprint weakFingerprint, CancellationToken cts, int level)
        {
            GetSelectorsParams.Add(weakFingerprint);
            return Task.FromResult(Result.Success(new LevelSelectors(new Selector[0], hasMore: false)));
        }

        public Task<BoolResult> IncorporateStrongFingerprintsAsync(Context context, IEnumerable<Task<StrongFingerprint>> strongFingerprints, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            IncorporateStringFingerprintsAsyncParams.Add(strongFingerprints);
            return Task.FromResult(BoolResult.Success);
        }

        public Task<BoolResult> ShutdownAsync(Context context)
        {
            return Task.FromResult(BoolResult.Success);
        }

        public Task<BoolResult> StartupAsync(Context context)
        {
            return Task.FromResult(BoolResult.Success);
        }
    }
}
