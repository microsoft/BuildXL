// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Core;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Filters the members used for directory membership computation based on search path semantics
    /// </summary>
    public class SearchPathDirectoryMembershipFilter : DirectoryMembershipFilter
    {
        internal HashSet<StringId> AccessedFileNamesWithoutExtension { get; }

        private readonly PathTable m_pathTable;

        /// <summary>
        /// Creates a search path membership filter based on the given accessed file names without extensions
        /// </summary>
        /// <remarks>
        /// <paramref name="accessedFileNamesWithoutExtension"/> must use case-insensitive StringId comparer.
        /// </remarks>
        public SearchPathDirectoryMembershipFilter(PathTable pathTable, HashSet<StringId> accessedFileNamesWithoutExtension)
        {
            m_pathTable = pathTable;
            AccessedFileNamesWithoutExtension = accessedFileNamesWithoutExtension;
        }

        /// <summary>
        /// Indicates whether the given path should be included in the directory membership computation
        /// </summary>
        public bool Include(AbsolutePath path)
        {
            var fileName = path.GetName(m_pathTable);
            return Include(fileName, fileName.ToString(m_pathTable.StringTable));
        }

        /// <summary>
        /// Indicates whether the given path should be included in the directory membership computation
        /// </summary>
        /// <returns>true if the member should be included. Otherwise, false.</returns>
        public override bool Include(PathAtom fileName, string fileNameStr)
        {
            if (AccessedFileNamesWithoutExtension.Contains(fileName.StringId))
            {
                return true;
            }

            var fileNameWithoutExtension = fileName.RemoveExtension(m_pathTable.StringTable).StringId;
            return AccessedFileNamesWithoutExtension.Contains(fileNameWithoutExtension);
        }
    }
}
