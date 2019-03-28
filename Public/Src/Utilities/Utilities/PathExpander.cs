// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Defines behavior for expanding paths
    /// </summary>
    public class PathExpander
    {
        /// <summary>
        /// The default expander which does no tokenization
        /// </summary>
        public static readonly PathExpander Default = new PathExpander();

        /// <summary>
        /// Gets the string representation of the given path.
        /// </summary>
        /// <param name="pathTable">the path table</param>
        /// <param name="path">the path</param>
        /// <returns>the string representation of the path</returns>
        public virtual string ExpandPath(PathTable pathTable, AbsolutePath path)
        {
            Contract.Requires(pathTable != null);
            return path.ToString(pathTable);
        }

        /// <summary>
        /// Attempts to translate the string into an absolute path which has any tokens detokenized based on the logic in the
        /// expander.
        /// </summary>
        /// <param name="pathTable">the path table</param>
        /// <param name="path">the string representation of the path</param>
        /// <param name="absolutePath">the absolute path</param>
        /// <returns>true if the absolute path was successfully detokenized and retrieved from the path table</returns>
        public virtual bool TryGetPath(PathTable pathTable, string path, out AbsolutePath absolutePath)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(path != null);
            return AbsolutePath.TryGet(pathTable, (StringSegment)path, out absolutePath);
        }

        /// <summary>
        /// Attempts to translate the string into an absolute path which has any tokens detokenized based on the logic in the
        /// expander.
        /// </summary>
        /// <param name="pathTable">the path table</param>
        /// <param name="path">the string representation of the path</param>
        /// <param name="absolutePath">the absolute path</param>
        /// <returns>true if the absolute path was successfully detokenized and retrieved or created from the path table</returns>
        public virtual bool TryCreatePath(PathTable pathTable, string path, out AbsolutePath absolutePath)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(path != null);
            return AbsolutePath.TryCreate(pathTable, (StringSegment)path, out absolutePath);
        }
    }
}
