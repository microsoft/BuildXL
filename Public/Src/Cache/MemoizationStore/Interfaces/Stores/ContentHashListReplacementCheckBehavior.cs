// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Stores
{
    /// <summary>
    /// Determines behavior for checking content existence for determining whether to replace entry in <see cref="IMemoizationSession.AddOrGetContentHashListAsync"/>
    /// </summary>
    public enum ContentHashListReplacementCheckBehavior
    {
        /// <summary>
        /// Allow using metadata on CHL to assume content is available
        /// </summary>
        AllowPinElision,

        /// <summary>
        /// Always perform pin operation to check content existence
        /// </summary>
        PinAlways,

        /// <summary>
        /// Assume content exists so never pin
        /// </summary>
        ReplaceNever,

        /// <summary>
        /// Always assume content may be missing and replace the entry
        /// </summary>
        ReplaceAlways,
    }
}
