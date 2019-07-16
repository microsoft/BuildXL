// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using Microsoft.Windows.ProjFS;

namespace BuildXL.Cache.ContentStore.Vfs.Provider
{
    using Utils = Microsoft.Windows.ProjFS.Utils;

    /// <summary>
    /// Implements IComparer using <see cref="Microsoft.Windows.ProjFS.Utils.FileNameCompare(string, string)"/>.
    /// </summary>
    internal class ProjectedFileNameSorter : Comparer<VfsNode>
    {
        public static readonly ProjectedFileNameSorter Instance = new ProjectedFileNameSorter();

        public override int Compare(VfsNode x, VfsNode y) => Utils.FileNameCompare(x.Name, y.Name);
    }
}
