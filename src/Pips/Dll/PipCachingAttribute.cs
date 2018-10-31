// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Pips
{
    /// <summary>
    /// Indicates the intended impact of some data on a pip's fingerprint.
    /// </summary>
    public enum FingerprintingRole : byte
    {
        /// <summary>
        /// Member is not used for fingerprinting.
        /// </summary>
        None,

        /// <summary>
        /// Semantic (static) value of this member is used for fingerprinting.
        /// If the member refers to a file artifact, the content hash of the file artifact is not relevant.
        /// </summary>
        Semantic,

        /// <summary>
        /// The dynamic content of this member is used for fingerprinting.
        /// This role is only relevant for file artifacts (or lists of file artifacts), in which case
        /// the pip fingerprint will account for that artifact's on-disk content.
        /// </summary>
        Content,
    }

    /// <summary>
    /// Indicates a pip member's participation in caching and fingerprinting.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class PipCachingAttribute : Attribute
    {
        /// <summary>
        /// Indicates if the value of this pip member contributes to the pip's fingerprint (cache key).
        /// </summary>
        public FingerprintingRole FingerprintingRole { get; set; }
    }
}
