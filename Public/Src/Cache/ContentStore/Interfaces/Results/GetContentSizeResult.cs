// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// Information returned by GetContentSize.
    /// </summary>
    public class GetContentSizeResult
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="GetContentSizeResult"/> class.
        /// </summary>
        public GetContentSizeResult(long size, bool wasPinned)
        {
            Size = size;
            WasPinned = wasPinned;
        }

        /// <summary>
        ///     Size of content in bytes or negative if content does not exist.
        /// </summary>
        public readonly long Size;

        /// <summary>
        ///     True if the content was already pinned in the store; false if it was not.
        /// </summary>
        public readonly bool WasPinned;

        /// <summary>
        ///     Gets a value indicating whether content is present.
        /// </summary>
        public bool Exists => Size >= 0;
    }
}
