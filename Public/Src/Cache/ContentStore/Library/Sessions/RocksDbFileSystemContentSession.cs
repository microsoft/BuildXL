using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.ContentStore.Sessions
{
    public class RocksDbFileSystemContentSession : IContentSession
    {

        public RocksDbFileSystemContentSession()
        {
        }

        public string Name => throw new NotImplementedException();

        public bool StartupCompleted => throw new NotImplementedException();

        public bool StartupStarted => throw new NotImplementedException();

        public bool ShutdownCompleted => throw new NotImplementedException();

        public bool ShutdownStarted => throw new NotImplementedException();

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public Task<OpenStreamResult> OpenStreamAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new NotImplementedException();
        }

        public Task<PinResult> PinAsync(Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, PinOperationConfiguration config)
        {
            throw new NotImplementedException();
        }

        public Task<PlaceFileResult> PlaceFileAsync(Context context, ContentHash contentHash, AbsolutePath path, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(Context context, IReadOnlyList<ContentHashWithPath> hashesWithPaths, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new NotImplementedException();
        }

        public Task<PutResult> PutFileAsync(Context context, HashType hashType, AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new NotImplementedException();
        }

        // WIP: Need to implement GetFilePath()
        public Task<PutResult> PutFileAsync(Context context, ContentHash contentHash, AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new NotImplementedException();
        }

        public Task<PutResult> PutStreamAsync(Context context, HashType hashType, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new NotImplementedException();
        }

        public Task<PutResult> PutStreamAsync(Context context, ContentHash contentHash, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new NotImplementedException();
        }

        public Task<BoolResult> ShutdownAsync(Context context)
        {
            throw new NotImplementedException();
        }

        public Task<BoolResult> StartupAsync(Context context)
        {
            throw new NotImplementedException();
        }
    }
}
