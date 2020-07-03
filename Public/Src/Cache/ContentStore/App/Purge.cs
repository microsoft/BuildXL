// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using CLAP;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        ///     Purge verb.
        /// </summary>
        [Verb(Description = "Bring a cache into quota")]
        internal void Purge([Required, Description("Content cache root directory")] string root)
        {
            RunFileSystemContentStoreInternal(new AbsolutePath(root), async (context, store) =>
            {
                await store.SyncAsync(context).ConfigureAwait(false);
            });
        }
    }
}
