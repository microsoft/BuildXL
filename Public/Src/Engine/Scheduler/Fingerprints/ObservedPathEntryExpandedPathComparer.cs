// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Core;

namespace BuildXL.Scheduler.Fingerprints
{
    /// <summary>
    /// Comparer for <see cref="ObservedPathEntry" />s which orders the inputs by their expanded paths
    /// (using a <see cref="PathTable.ExpandedAbsolutePathComparer" />).
    /// </summary>
    public sealed class ObservedPathEntryExpandedPathComparer : IComparer<ObservedPathEntry>
    {
        /// <nodoc />
        public readonly PathTable.ExpandedAbsolutePathComparer PathComparer;

        /// <nodoc />
        public ObservedPathEntryExpandedPathComparer(PathTable.ExpandedAbsolutePathComparer pathComparer)
        {
            Contract.Requires(pathComparer != null);
            PathComparer = pathComparer;
        }

        /// <inheritdoc />
        public int Compare(ObservedPathEntry x, ObservedPathEntry y)
        {
            return PathComparer.Compare(x.Path, y.Path);
        }
    }
}
