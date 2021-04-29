// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;

#nullable enable

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// Options to control the behavior of <see cref="FileSystemContentStoreInternal.PinAsync(Context, System.Collections.Generic.IReadOnlyList{BuildXL.Cache.ContentStore.Hashing.ContentHash}, PinContext, PinBulkOptions)"/>.
    /// </summary>
    public class PinBulkOptions
    {
        /// <nodoc />
        public static PinBulkOptions Default { get; } = new PinBulkOptions();

        /// <summary>
        /// If true, then <see cref="FileSystemContentStoreInternal.PinAsync(Context, ContentHash, PinContext)"/> is called to restore pinned content after reading hibernating sessions from disk.
        /// </summary>
        public bool RePinFromHibernation { get; set; }
    }
}
