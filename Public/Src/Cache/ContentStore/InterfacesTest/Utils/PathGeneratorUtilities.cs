// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Utils
{
    public static class PathGeneratorUtilities
    {
        /// <summary>
        /// Returns an absolute path, formatted appropriately for Windows or Unix
        /// Copied from Public\Src\Utilities\UnitTests\Test.BuildXL.TestUtilities.XUnit\PathGeneratorUtilities.cs to get around net451/net461 dependency conflicts.
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
                if (driveLetter == null)
                {
                    driveLetter = "x";
                }
                path = driveLetter + Path.VolumeSeparatorChar + Path.DirectorySeparatorChar + GetRelativePath(dirList);
            }
            else
            {
                path = Path.VolumeSeparatorChar + GetRelativePath(dirList);
            }

            return path;
        }

        /// <summary>
        /// Returns a relative path, formatted appropriately for Windows or Unix
        /// Copied from Public\Src\Utilities\UnitTests\Test.BuildXL.TestUtilities.XUnit\PathGeneratorUtilities.cs to get around net451/net461 dependency conflicts.
        /// </summary>
        /// <param name="dirList">An optional parameter for a sequence of directories in the path,
        /// where dirList[i+1] is the child directory of dirList[i]. If empty, then an empty string is returned.</param>
        public static string GetRelativePath(params string[] dirList)
        {
            string path = "";
            foreach (string dir in dirList)
            {
                if (path.Length == 0)
                {
                    path = dir;
                }
                else
                {
                    path += Path.DirectorySeparatorChar + dir;
                }
            }

            return path;
        }
    }
}
