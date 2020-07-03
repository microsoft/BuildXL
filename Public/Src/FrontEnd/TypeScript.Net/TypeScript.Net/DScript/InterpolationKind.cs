// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TypeScript.Net.DScript
{
    /// <summary>
    /// Types of well-known DScript interpolation kinds.
    /// </summary>
    public enum InterpolationKind
    {
        /// <summary>
        /// Unknown path interpolation function: <code>let x = foo`${var}`;</code>
        /// </summary>
        Unknown,

        /// <summary>
        /// String interpolation: <code>let x = `${var1}`;</code>
        /// </summary>
        StringInterpolation,

        /// <summary>
        /// Path interpolation: <code>let p = p`${someRoot}/foo.txt`;</code>
        /// </summary>
        PathInterpolation,

        /// <summary>
        /// File path interpolation: <code>let f = f`${someRoot}/foo.txt`;</code>
        /// </summary>
        FileInterpolation,

        /// <summary>
        /// Directory interpolation: <code>let d = d`${someRoot}/foo`;</code>
        /// </summary>
        DirectoryInterpolation,

        /// <summary>
        /// Path atom interpolation: <code>let a = a`${name}.${extension}`;</code>
        /// </summary>
        PathAtomInterpolation,

        /// <summary>
        /// Relative path interpolation: <code>let rp = r`${relative}/foo.txt`;</code>
        /// </summary>
        RelativePathInterpolation,
    }
}
