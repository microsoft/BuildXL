// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;

namespace BuildXL.Cache.ContentStore.Sessions.Internal
{
    /// <summary>
    /// Content session with the capability of wrapping the stream it uses while hashing a file.
    /// </summary>
    public interface IDecoratedStreamContentSession : IContentSession
    {
        /// <summary>
        /// Put the given file allowing interception of hashing. The delegate function will be called to wrap the stream when reading in order to hash the file.
        /// </summary>
        Task<PutResult> PutFileAsync(Context context,
            AbsolutePath path,
            HashType hashType,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint,
            Func<Stream, Stream> wrapStream);

        /// <summary>
        /// Put the given file allowing interception of hashing. The delegate function will be called to wrap the stream when reading in order to hash the file.
        /// </summary>
        Task<PutResult> PutFileAsync(Context context,
            AbsolutePath path,
            ContentHash contentHash,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint,
            Func<Stream, Stream> wrapStream);
    }
}
