// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using CLAP;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

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
