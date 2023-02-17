// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Filters the directory members by using two filters 
    /// </summary>
    public class UnionDirectoryMembershipFilter : DirectoryMembershipFilter
    {
        private readonly DirectoryMembershipFilter m_filter1;
        private readonly DirectoryMembershipFilter m_filter2;

        internal UnionDirectoryMembershipFilter(DirectoryMembershipFilter filter1, DirectoryMembershipFilter filter2)
        {
            m_filter1 = filter1;
            m_filter2 = filter2;
        }

        /// <summary>
        /// Indicates whether the given path passes at least one of the filters.
        /// </summary>
        public override bool Include(PathAtom fileName, string fileNameStr)
        {
            return m_filter1.Include(fileName, fileNameStr) || m_filter2.Include(fileName, fileNameStr);
        }
    }
}
