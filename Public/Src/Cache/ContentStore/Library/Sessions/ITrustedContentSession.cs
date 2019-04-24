// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Sessions.Internal
{
    /// <summary>
    /// Trusted content session which can accept a trusted hash for a file.
    /// </summary>
    public interface ITrustedContentSession : IContentSession
    {
        /// <summary>
        /// Put the given file without hashing.
        /// </summary>
        Task<PutResult> PutTrustedFileAsync(Context context,
            ContentHashWithSize contentHashWithSize,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint);
    }
}
