// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Interop.MacOS;
using BuildXL.Native.IO;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using static BuildXL.Interop.MacOS.IO;

namespace Test.BuildXL.Utilities
{
    public class MacNativeIOTests
    {
        [TheoryIfSupported(requiresUnixBasedOperatingSystem: true)]
        [InlineData("/", true)]
        [InlineData("/bin", true)]
        [InlineData("/bin/ls", true)]
        [InlineData("/foo/bar/baz/bogus/does/not/exist", false)]
        public void TestStatFs(string path, bool pathExists)
        {
            var pathExistsAsFile = FileUtilities.FileExistsNoFollow(path);
            var pathExistsAsDir = FileUtilities.DirectoryExistsNoFollow(path);
            XAssert.AreEqual(
                pathExists, pathExistsAsFile || pathExistsAsDir,
                $"Expected exists: {pathExists}, Actual exists as file: {pathExistsAsFile}, Actual exists as dir: {pathExistsAsDir}");

            var buf = new StatFsBuffer();
            int error = IO.StatFs(path, ref buf);
            long freeSpaceBytes = IO.FreeSpaceLeftOnDeviceInBytes(path);
            if (pathExists)
            {
                XAssert.AreEqual(0, error, $"Expected statfs to return 0 on {path} which exists; instead.");
                XAssert.IsTrue(freeSpaceBytes > 0, $"Expected free space to be greater than 0B, instead it is {freeSpaceBytes}B.");
            }
            else
            {
                XAssert.AreEqual(-1, error, $"Expected statfs to return -1 on '{path}' which does not exist.");
                XAssert.AreEqual(-1, freeSpaceBytes, $"Expected free space to be -1 for path '{path}' that does not exist.");
            }
        }
    }
}
