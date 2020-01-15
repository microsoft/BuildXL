// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Cache.ContentStore.Vfs.Provider
{
    /// <summary>
    /// Implements IComparer using <see cref="Microsoft.Windows.ProjFS.Utils.FileNameCompare(string, string)"/>.
    /// </summary>
    internal class ProjectedFileNameSorter : Comparer<VfsNode>
    {
        public static readonly ProjectedFileNameSorter Instance = new ProjectedFileNameSorter();

        public override int Compare(VfsNode x, VfsNode y) => Microsoft.Windows.ProjFS.Utils.FileNameCompare(x.Name, y.Name);
    }
}
