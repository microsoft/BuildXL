// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Linq;

namespace BuildXL.Utilities
{
    /// <summary>
    /// This class is used to generate paths that can be used in unit tests. 
    /// The paths generated are constructed to be appropriate for the operating system
    /// on which unit tests are being run.
    /// </summary>
    public static class PathGeneratorUtilities
    {
        /// <summary>
        /// Returns an absolute path, formatted appropriately for Windows or Unix
        /// </summary>
        /// <param name="driveLetter">If Windows, the absolute path starts at driveLetter,
        /// otherwise driveLetter is ignored and path starts with '/'. In Windows, if the driveLetter param is null, then it is assigned 'x' by default</param>
        /// <param name="dirList">An optional parameter for a sequence of directories in the path
        /// that follow the root, where dirList[i+1] is the child directory of dirList[i]</param>
        public static string GetAbsolutePath(string driveLetter, params string[] dirList)
        {          
            string path = string.Empty;
            if (!OperatingSystemHelper.IsUnixOS)
            {
                if(driveLetter == null)
                {
                    driveLetter = "x";
                }
                path = driveLetter + Path.VolumeSeparatorChar + Path.DirectorySeparatorChar + GetRelativePath(dirList);
            }
            else
            {
                path =  Path.VolumeSeparatorChar + GetRelativePath(dirList);
            }

            return path;
        }

        /// <summary>
        /// Returns an absolute path, formatted appropriately for Windows or Unix.
        /// Behaves just like GetAbsolutePath(driveLetter, dirList), except driveLetter = driveAndDirList[0]. 
        /// 
        /// Note: There is O(n) additional overhead in using this method, so please use GetAbsolutePath(driveLetter, dirList) when possible.
        /// 
        /// </summary>
        /// <param name="driveAndDirList">The 0th element's value should be the driveLetter. If Unix, the 0th element is ignored
        /// and the path starts with '/'. In Windows, if driveAndDirList[0] is null, then drive letter is assigned 'x' by default. The 
        /// rest of the elements are a sequence of directories in the path
        /// that follow the root, where driveAndDirList[i+1] is the child directory of driveAndDirList[i], and where i > 0.</param>
        public static string GetAbsolutePath(string[] driveAndDirList)
        {
            if (driveAndDirList.Length == 0)
            {
                return string.Empty;
            }

            return GetAbsolutePath(driveAndDirList[0], driveAndDirList.Skip(1).ToArray());
        }

        /// <summary>
        /// Returns a relative path, formatted appropriately for Windows or Unix
        /// </summary>
        /// <param name="dirList">An optional parameter for a sequence of directories in the path,
        /// where dirList[i+1] is the child directory of dirList[i]. If empty, then an empty string is returned.</param>
        public static string GetRelativePath(params string[] dirList)
        {
            return string.Join(Path.DirectorySeparatorChar.ToString(), dirList);
        }
    }
}
