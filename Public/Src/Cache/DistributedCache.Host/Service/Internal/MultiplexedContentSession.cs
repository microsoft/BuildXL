// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.Host.Service.Internal
{
    public class MultiplexedContentSession : MultiplexedReadOnlyContentSession, IContentSession, ILightweightPin
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="MultiplexedContentSession"/> class.
        /// </summary>
        public MultiplexedContentSession(ContentSessionTracer tracer, Dictionary<string, IReadOnlyContentSession> cacheSessionsByRoot, string name, string preferredCacheDrive)
                    : base(tracer, cacheSessionsByRoot, name, preferredCacheDrive)
        {
        }

        public Task<IEnumerable<Task<Indexed<PinResult>>>> LightweightPinAsync(OperationContext operationContext, IReadOnlyList<ContentHash> contentHashes, UrgencyHint urgencyHint)
        {
            var session = PreferredContentSession as ILightweightPin;
            if (PreferredContentSession is ILightweightPin lightweightPin)
            {
                return lightweightPin.LightweightPinAsync(operationContext, contentHashes, urgencyHint);
            }

            throw new InvalidOperationException($"Preferred content session does not have type {nameof(ILightweightPin)}. Found {PreferredContentSession.GetType()}");
        }

        public Task<PutResult> PutFileAsync(
            Context context,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            var session = GetCache(path) as IContentSession;
            return session.PutFileAsync(context, hashType, path, realizationMode, cts, urgencyHint);
        }

        public Task<PutResult> PutFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            var session = GetCache(path) as IContentSession;
            return session.PutFileAsync(context, contentHash, path, realizationMode, cts, urgencyHint);
        }

        public Task<PutResult> PutStreamAsync(
            Context context,
            ContentHash contentHash,
            Stream stream,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            var session = PreferredContentSession as IContentSession;
            return session.PutStreamAsync(context, contentHash, stream, cts, urgencyHint);
        }

        public Task<PutResult> PutStreamAsync(
            Context context,
            HashType hashType,
            Stream stream,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            var session = PreferredContentSession as IContentSession;
            return session.PutStreamAsync(context, hashType, stream, cts, urgencyHint);
        }
    }
}
