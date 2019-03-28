// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
