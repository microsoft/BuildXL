// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Constants used by ContentStore.
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// Name of the Shared directory, where the cached content resides.
        /// </summary>
        public const string SharedDirectoryName = "Shared";

        /// <summary>
        ///     Marks session names that contain build ID.
        /// </summary>
        public const string BuildIdPrefix = "BID";

        /// <summary>
        /// Attempts to extract build guid encoded in a session name (i.e. SessionName={Name}{BuildIdPrefix}{BuildId}
        /// </summary>
        public static bool TryExtractBuildId(string sessionName, out string buildId)
        {
            if (sessionName?.Contains(BuildIdPrefix) == true)
            {
                var index = sessionName.IndexOf(BuildIdPrefix) + BuildIdPrefix.Length;
                buildId = sessionName.Substring(index);

                // Return true only if buildId is actually a guid.
                return Guid.TryParse(buildId, out _);
            }

            buildId = null;
            return false;
        }
    }
}
