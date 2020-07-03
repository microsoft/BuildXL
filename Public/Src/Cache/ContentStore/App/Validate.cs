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
        ///     Validate verb.
        /// </summary>
        [Verb(Aliases = "vc", Description = "Validate for correctness in hashes, ACLs, and content directory.")]
        internal void Validate([Required, Description("Content cache root directory")] string root)
        {
            RunFileSystemContentStoreInternal(new AbsolutePath(root), (context, store) => store.Validate(context));
        }
    }
}
