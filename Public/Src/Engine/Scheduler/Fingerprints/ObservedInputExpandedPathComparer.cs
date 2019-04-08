// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Scheduler.Fingerprints
{
    /// <summary>
    /// Comparer for <see cref="ObservedInput" />s which orders the inputs by their expanded paths
    /// (using a <see cref="PathTable.ExpandedAbsolutePathComparer" />).
    /// </summary>
    public sealed class ObservedInputExpandedPathComparer : IComparer<ObservedInput>
    {
        /// <nodoc />
        public readonly PathTable.ExpandedAbsolutePathComparer PathComparer;

        /// <nodoc />
        public ObservedInputExpandedPathComparer(PathTable.ExpandedAbsolutePathComparer pathComparer)
        {
            Contract.Requires(pathComparer != null);
            PathComparer = pathComparer;
        }

        /// <inheritdoc />
        public int Compare(ObservedInput x, ObservedInput y)
        {
            return PathComparer.Compare(x.Path, y.Path);
        }
    }
}
