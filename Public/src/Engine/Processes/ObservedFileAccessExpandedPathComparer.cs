// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// Comparer for <see cref="ObservedFileAccess"/>es which orders the accesses by their expanded paths
    /// (using a <see cref="PathTable.ExpandedAbsolutePathComparer"/>).
    /// </summary>
    public sealed class ObservedFileAccessExpandedPathComparer : IComparer<ObservedFileAccess>
    {
        /// <nodoc />
        public readonly PathTable.ExpandedAbsolutePathComparer PathComparer;

        /// <nodoc />
        public ObservedFileAccessExpandedPathComparer(PathTable.ExpandedAbsolutePathComparer pathComparer)
        {
            Contract.Requires(pathComparer != null);
            PathComparer = pathComparer;
        }

        /// <nodoc />
        public int Compare(ObservedFileAccess x, ObservedFileAccess y)
        {
            return PathComparer.Compare(x.Path, y.Path);
        }
    }
}
