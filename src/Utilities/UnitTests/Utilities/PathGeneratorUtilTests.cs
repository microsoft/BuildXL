// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using System.IO;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public class PathGeneratorUtilTests : XunitBuildXLTest
    {
        public PathGeneratorUtilTests(ITestOutputHelper output)
            : base(output) { }

        /// <summary>
        /// Tests absolute path generation of the PathGeneratorUtil class
        /// </summary>
        [Fact]
        public void AbsolutePathTests()
        {
            string drive = "x";
            string rootDir = "test";
            string subDir = "testSubDir";
            // absolute paths with two directories
            string path1 = A(drive, rootDir, subDir);
            // alternative method
            string[] path1Array = { drive, rootDir, subDir };
            string path1method2 = A(path1Array);
            // absolute paths with one directory
            string path2 = A(drive, rootDir);
            // alternative method
            string[] path2Array = { drive, rootDir };
            string path2method2 = A(path2Array);
            // absolute path with no directories
            string path3 = A(drive);
            // alternative method
            string[] path3Array = { drive };
            string path3method2 = A(path3Array);

            if (OperatingSystemHelper.IsUnixOS)
            {
                XAssert.AreEqual(path1, Path.VolumeSeparatorChar + rootDir + Path.DirectorySeparatorChar + subDir);
                XAssert.AreEqual(path2, Path.VolumeSeparatorChar + rootDir);
                XAssert.AreEqual(path3, Path.VolumeSeparatorChar+string.Empty);

                XAssert.AreEqual(path1method2, Path.VolumeSeparatorChar + rootDir + Path.DirectorySeparatorChar + subDir);
                XAssert.AreEqual(path2method2, Path.VolumeSeparatorChar + rootDir);
                XAssert.AreEqual(path3method2, Path.VolumeSeparatorChar + string.Empty);

                XAssert.AreEqual(path1method2, path1);
                XAssert.AreEqual(path2method2, path2);
                XAssert.AreEqual(path3method2, path3);
            }
            else
            {
                XAssert.AreEqual(path1, drive + Path.VolumeSeparatorChar + Path.DirectorySeparatorChar + rootDir + Path.DirectorySeparatorChar + subDir);
                XAssert.AreEqual(path2, drive + Path.VolumeSeparatorChar + Path.DirectorySeparatorChar + rootDir);
                XAssert.AreEqual(path3, drive + Path.VolumeSeparatorChar + Path.DirectorySeparatorChar);

                XAssert.AreEqual(path1method2, drive + Path.VolumeSeparatorChar + Path.DirectorySeparatorChar + rootDir + Path.DirectorySeparatorChar + subDir);
                XAssert.AreEqual(path2method2, drive + Path.VolumeSeparatorChar + Path.DirectorySeparatorChar + rootDir);
                XAssert.AreEqual(path3method2, drive + Path.VolumeSeparatorChar + Path.DirectorySeparatorChar);

                XAssert.AreEqual(path1method2, path1);
                XAssert.AreEqual(path2method2, path2);
                XAssert.AreEqual(path3method2, path3);

                // testing default drive for windows
                string pathNoDrive = A(null, rootDir, subDir);
                XAssert.AreEqual(pathNoDrive, drive + Path.VolumeSeparatorChar + Path.DirectorySeparatorChar + rootDir + Path.DirectorySeparatorChar + subDir);

                // testing default drive for windows - method 2
                string[] path3ArrayMethod2 = { null, rootDir, subDir };
                pathNoDrive = A(path3ArrayMethod2);
                XAssert.AreEqual(pathNoDrive, drive + Path.VolumeSeparatorChar + Path.DirectorySeparatorChar + rootDir + Path.DirectorySeparatorChar + subDir);
            }
        }

        /// <summary>
        /// Tests relative path generation of the PathGeneratorUtil class
        /// </summary>
        [Fact]
        public void RelativePathTests()
        {
            string rootDir = "test";
            string subDir = "testSubDir";
            string path1 = R(rootDir, subDir);
            XAssert.AreEqual(path1, rootDir + Path.DirectorySeparatorChar + subDir);

            string path2 = R(rootDir);
            XAssert.AreEqual(path2, rootDir);

            string path3 = R();
            XAssert.AreEqual(path3, string.Empty);
        }
    }
}
