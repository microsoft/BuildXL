// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions.Internal;

namespace BuildXL.Cache.Host.Service.Internal
{
    public class MultiplexedContentSession : MultiplexedReadOnlyContentSession, IContentSession, ITrustedContentSession
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="MultiplexedContentSession"/> class.
        /// </summary>
        public MultiplexedContentSession(Dictionary<string, IReadOnlyContentSession> cacheSessionsByRoot, string name, MultiplexedContentStore store)
            : base(cacheSessionsByRoot, name, store)
        {
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(
            Context context,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            var session = GetCache<IContentSession>(path);
            return session.PutFileAsync(context, hashType, path, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            var session = GetCache<IContentSession>(path);
            return session.PutFileAsync(context, contentHash, path, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(
            Context context,
            ContentHash contentHash,
            Stream stream,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            var session = GetCache<IContentSession>();
            return session.PutStreamAsync(context, contentHash, stream, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(
            Context context,
            HashType hashType,
            Stream stream,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            var session = GetCache<IContentSession>();
            return session.PutStreamAsync(context, hashType, stream, cts, urgencyHint);
        }

        /// <inheritdoc />
        public Task<PutResult> PutTrustedFileAsync(Context context, ContentHashWithSize contentHashWithSize, AbsolutePath path, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint)
        {
            var session = GetCache<ITrustedContentSession>(path);
            return session.PutTrustedFileAsync(context, contentHashWithSize, path, realizationMode, cts, urgencyHint);
        }

        /// <inheritdoc />
        public AbsolutePath TryGetWorkingDirectory(AbsolutePath pathHint)
        {
            var session = GetCache<IContentSession>(pathHint);
            return (session as ITrustedContentSession)?.TryGetWorkingDirectory(pathHint);
        }
    }
}
