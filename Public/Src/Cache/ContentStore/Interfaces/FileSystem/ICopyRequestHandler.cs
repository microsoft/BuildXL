// Copyright(c) Microsoft.All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

// ReSharper disable All
namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    /// Handles requests that machine to copy a file to itself.
    /// </summary>
    public interface ICopyRequestHandler
    {
        /// <summary>
        /// Requests the machine to copy a file to itself.
        /// </summary>
        /// <param name="context">The context of the operation</param>
        /// <param name="hash">The hash of the file to be copied.</param>
        Task<BoolResult> HandleCopyFileRequestAsync(Context context, ContentHash hash);
    }
}
