// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using SpecialFolder = System.Environment.SpecialFolder;
using SpecialFolderOption = System.Environment.SpecialFolderOption;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Helper method for Environment.GetFolder functionality on .NET Core
    /// </summary>
    public static partial class SpecialFolderUtilities
    {
        // .NET Core doesn't currently have APIs to get the "special" folders (ie the ones defined in the Environment.SpecialFolder enum)
        //  The code here is mostly copied out of the .NET reference source for this functionality

        /// <summary>
        /// Gets the path to the system special folder that is identified by the specified enumeration.
        /// </summary>
        /// <param name="folder">An enumerated constant that identifies a system special folder.</param>
        /// <param name="option">Specifies options to use for accessing a special folder.</param>
        /// <returns>The path to the specified system special folder, if that folder physically exists
        /// on your computer; otherwise, an empty string ("")</returns>
        public static string GetFolderPath(SpecialFolder folder, SpecialFolderOption option = SpecialFolderOption.None)
        {
            return Environment.GetFolderPath(folder, option);
        }

        /// <summary>
        /// Gets the fully qualified path of the system directory.
        /// </summary>
        public static string SystemDirectory => Environment.SystemDirectory;
    }
}
