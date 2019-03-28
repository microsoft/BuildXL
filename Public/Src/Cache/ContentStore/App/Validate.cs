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
        ///     Validate verb.
        /// </summary>
        [Verb(Aliases = "vc", Description = "Validate for correctness in hashes, ACLs, and content directory.")]
        internal void Validate([Required, Description("Content cache root directory")] string root)
        {
            RunFileSystemContentStoreInternal(new AbsolutePath(root), (context, store) => store.Validate(context));
        }
    }
}
