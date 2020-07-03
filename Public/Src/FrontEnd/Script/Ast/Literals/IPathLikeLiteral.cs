// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Literals
{
    /// <summary>
    /// Path, relative paths and path atoms implements this one.
    /// </summary>
    public interface IPathLikeLiteral
    {
        /// <summary>
        /// String representation of a path-like literal
        /// </summary>
        string ToDisplayString(PathTable table, AbsolutePath currentFolder);
    }
}
