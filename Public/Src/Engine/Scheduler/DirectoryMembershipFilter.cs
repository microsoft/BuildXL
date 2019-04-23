// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Filters the members used for directory membership fingerprint computation.
    /// </summary>
    public class DirectoryMembershipFilter
    {
        /// <summary>
        /// Gets the default allow all filter
        /// </summary>
        public static readonly DirectoryMembershipFilter AllowAllFilter = new DirectoryMembershipFilter();

        /// <summary>
        /// Indicates whether the given path should be included in the directory membership computatation
        /// </summary>
        /// <returns>True if the member should be included. Otherwise, false.</returns>
        public bool Include(PathTable pathTable, AbsolutePath path)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(path.IsValid);

            var fileName = path.GetName(pathTable);
            var fileNameStr = fileName.ToString(pathTable.StringTable);
            return Include(fileName, fileNameStr);
        }

        /// <summary>
        /// Indicates whether the given path should be included in the directory membership computatation
        /// </summary>
        /// <returns>True if the member should be included. Otherwise, false.</returns>
        public virtual bool Include(PathAtom fileName, string fileNameStr)
        {
            Contract.Requires(fileName.IsValid, "FileName is invalid");
            Contract.Requires(fileNameStr != null, "fileNameStr is null");
            Contract.Requires(fileNameStr != string.Empty, "fileNameStr is empty");
            
            return true;
        }

        /// <summary>
        /// Merges two directory membership filter.
        /// </summary>
        public DirectoryMembershipFilter Union(DirectoryMembershipFilter other)
        {
            if (other == null)
            {
                return this;
            }

            return new UnionDirectoryMembershipFilter(this, other);
        }
    }
}
