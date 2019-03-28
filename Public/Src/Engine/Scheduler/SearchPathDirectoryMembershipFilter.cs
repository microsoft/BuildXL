// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Utilities;

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
        public SearchPathDirectoryMembershipFilter(PathTable pathTable, IEnumerable<StringId> accessedFileNamesWithoutExtension)
        {
            m_pathTable = pathTable;
            AccessedFileNamesWithoutExtension = new HashSet<StringId>(pathTable.StringTable.CaseInsensitiveEqualityComparer);

            foreach (var fileNameWithoutExtension in accessedFileNamesWithoutExtension)
            {
                AccessedFileNamesWithoutExtension.Add(fileNameWithoutExtension);
            }
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
