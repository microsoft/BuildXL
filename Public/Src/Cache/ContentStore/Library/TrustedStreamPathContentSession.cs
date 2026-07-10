// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Sessions.Internal;

namespace BuildXL.Cache.ContentStore
{
    /// <summary>
    /// A <see cref="StreamPathContentSession"/> that supports trusted content operations.
    /// </summary>
    public sealed class TrustedStreamPathContentSession : StreamPathContentSession, ITrustedContentSession
    {
        private ITrustedContentSession TrustedSessionForPath => (SessionForPath as ITrustedContentSession)!;

        /// <nodoc/>
        public TrustedStreamPathContentSession(
            string name,
            IContentSession sessionForStream,
            ITrustedContentSession sessionForPath) : base(name, sessionForStream, sessionForPath)
        {
        }

        /// <nodoc/>
        public Task<PutResult> PutTrustedFileAsync(Context context, ContentHashWithSize contentHashWithSize, AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint)
        {
            return TrustedSessionForPath.PutTrustedFileAsync(context, contentHashWithSize, path, realizationMode, cts, urgencyHint);
        }

        /// <nodoc/>
        public AbsolutePath? TryGetWorkingDirectory(AbsolutePath? pathHint)
        {
            return TrustedSessionForPath.TryGetWorkingDirectory(pathHint);
        }
    }
}
