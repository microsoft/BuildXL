// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace ContentStoreTest.Grpc
{
    internal class TestTempFileDeletingContentSession : IContentSession, IHibernateContentSession
    {
        private readonly IAbsFileSystem _fileSystem;

        public string Name { get; }

        internal TestTempFileDeletingContentSession(string name, IAbsFileSystem fileSystem)
        {
            Name = name;
            _fileSystem = fileSystem;
        }

        public bool StartupCompleted { get; private set; }

        public bool StartupStarted { get; private set; }

        public Task<BoolResult> StartupAsync(Context context)
        {
            StartupStarted = true;
            StartupCompleted = true;
            return Task.FromResult(BoolResult.Success);
        }

        public void Dispose()
        {
        }

        public bool ShutdownCompleted { get; private set; }

        public bool ShutdownStarted { get; private set; }

        public Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;
            ShutdownCompleted = true;
            return Task.FromResult(BoolResult.Success);
        }

        public Task<PinResult> PinAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new NotImplementedException();
        }

        public Task<OpenStreamResult> OpenStreamAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new NotImplementedException();
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
            // This implementation lies about success without creating the requested files (to simulate the file being deleted due to some race;
            // this generally happens if the service shuts down before the caller can read the temporary file it produced).
            return Task.FromResult(new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy, 100));
        }

        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(
            Context context,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            // This implementation lies about success without creating the requested files (to simulate the file being deleted due to some race;
            // this generally happens if the service shuts down before the caller can read the temporary file it produced).
            return Task.FromResult(hashesWithPaths.Select(hashWithPath => new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy, 100)).AsIndexed().AsTasks());
        }

        public Task<PutResult> PutFileAsync(
            Context context, HashType hashType, AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            // This implementation deletes the file and then complains about its nonexistence (to simulate the file being deleted before the service can read it;
            // this generally happens if the service restarts before reading the temporary file during PutStream).
            _fileSystem.DeleteFile(path);
            return Task.FromResult(new PutResult(new ContentHash(hashType), "Source file not found."));
        }

        public Task<PutResult> PutFileAsync(
            Context context, ContentHash contentHash, AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            // This implementation deletes the file and then complains about its nonexistence (to simulate the file being deleted before the service can read it;
            // this generally happens if the service restarts before reading the temporary file during PutStream).
            _fileSystem.DeleteFile(path);
            return Task.FromResult(new PutResult(contentHash, "Source file not found."));
        }

        public Task<PutResult> PutStreamAsync(Context context, HashType hashType, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new NotImplementedException();
        }

        public Task<PutResult> PutStreamAsync(
            Context context, ContentHash contentHash, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<ContentHash> EnumeratePinnedContentHashes()
        {
            return new List<ContentHash>();
        }

        public Task PinBulkAsync(Context context, IEnumerable<ContentHash> contentHashes)
        {
            return Task.FromResult(0);
        }
    }
}
