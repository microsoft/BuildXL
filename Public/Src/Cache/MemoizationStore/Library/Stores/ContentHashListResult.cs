// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Stores
{
    /// <summary>
    /// Result for <see cref="MemoizationDatabase.GetContentHashListAsync"/>.
    /// </summary>
    public class ContentHashListResult
        : Result<(ContentHashListWithDeterminism contentHashListInfo, string replacementToken)>
    {
        /// <nodoc />
        public ContentHashListResult(ContentHashListWithDeterminism contentHashListInfo, string replacementToken, DateTime? lastContentPinnedTime = null)
            : base((contentHashListInfo, replacementToken))
        {
            LastContentPinnedTime = lastContentPinnedTime;
        }

        /// <nodoc />
        public ContentHashListResult(string errorMessage, string? diagnostics = null)
            : base(errorMessage, diagnostics)
        {
        }

        /// <nodoc />
        public ContentHashListResult(Exception exception, string? message = null)
            : base(exception, message)
        {
        }

        /// <nodoc />
        public ContentHashListResult(ResultBase other, string? message = null)
            : base(other, message)
        {
        }

        /// <summary>
        /// The source of <see cref="ContentHashList"/>.
        /// </summary>
        public ContentHashListSource Source { get; set; } = ContentHashListSource.Unknown;

        /// <summary>
        /// The time at which all the content of this content hash list was pinned
        /// </summary>
        /// <remarks>
        /// This is only set when the memoization database retrieving this last supports this, and consumers can use this
        /// to help feed a pin eliding heuristic
        /// </remarks>
        public DateTime? LastContentPinnedTime { get; set; }

        /// <nodoc />
        public void Deconstruct(
            out ContentHashListWithDeterminism contentHashListInfo,
            out string? replacementToken,
            out ContentHashListSource source)
        {
            source = Source;

            if (Succeeded)
            {
                contentHashListInfo = Value.contentHashListInfo;
                replacementToken = Value.replacementToken;
            }
            else
            {
                contentHashListInfo = default;
                replacementToken = default;
                source = default;
            }
        }
    }
}
