// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Extension methods for <see cref="ReparsePointType"/>.
    /// </summary>
    public static class ReparsePointTypeExtensions
    {
        /// <summary>
        /// Checks whether the reparse point is actionable
        /// </summary>
        public static bool IsActionable(this ReparsePointType reparsePointType)
        {
            return FileUtilities.IsReparsePointActionable(reparsePointType);
        }
    }
}
