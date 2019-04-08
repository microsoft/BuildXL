// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// Information about changed path.
    /// </summary>
    public readonly struct ChangedPathInfo
    {
        /// <summary>
        /// Changed path.
        /// </summary>
        public readonly string Path;

        /// <summary>
        /// Kinds of changes to the path.
        /// </summary>
        public readonly PathChanges PathChanges;

        /// <summary>
        /// Creates an instance of <see cref="ChangedPathInfo"/>.
        /// </summary>
        public ChangedPathInfo(string path, PathChanges pathChanges)
        {
            Path = path;
            PathChanges = pathChanges;
        }
    }
}
