// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Extension methods for <see cref="ReparsePointType"/>.
    /// </summary>
    public static class ReparsePointTypeExtensions
    {
        /// <summary>
        /// Checks whether the reparse point is actionable, i.e., a mount point or a symlink.
        /// </summary>
        public static bool IsActionable(this ReparsePointType reparsePointType)
        {
            return FileUtilities.IsReparsePointActionable(reparsePointType);
        }
    }
}
