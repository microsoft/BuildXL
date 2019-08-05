// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Interfaces.Distributed
{
    /// <summary>
    /// Session that handles copies from remote locations
    /// </summary>
    internal interface IFileCopyingSession
    {
        /// <summary>
        /// Attempts to copy a content locally from a given remote location and put into session
        /// </summary>
        Task<PutResult> TryCopyAndPutAsync(
            Context operationContext,
            ContentHash hash,
            string machineLocation);
    }
}
