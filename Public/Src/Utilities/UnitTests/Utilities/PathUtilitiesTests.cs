// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    /// <summary>
    /// Tests for <see cref="PathUtilities" />
    /// </summary>
    public sealed class PathUtilitiesTests : XunitBuildXLTest
    {
        public PathUtilitiesTests(ITestOutputHelper output)
            : base(output) { }

        private static readonly string[] s_validPathAtoms =
        {
            "dir1",
            "dir 1",
            "dir1 ",
            " dir1 ",
            "_$@#",
            "dir1!"
        };

        private static readonly string[] s_invalidPathAtoms =
        {
            string.Empty,
            "foo/",
            "fo/o",
            "foo\x0",
            "foo\x1f"
        };

        [Fact]
        public void InvalidPathAtoms()
        {
            foreach (string invalidPathAtom in s_invalidPathAtoms)
            {
                XAssert.IsFalse(PathAtom.Validate((StringSegment)invalidPathAtom), "Case: {0}", invalidPathAtom);
            }

            if (!OperatingSystemHelper.IsUnixOS)
            {
                XAssert.IsFalse(PathAtom.Validate((StringSegment)"f*oo"), "Case: {0}", "f*oo");
                XAssert.IsFalse(PathAtom.Validate((StringSegment)"foo?"), "Case: {0}", "foo?");
                XAssert.IsFalse(PathAtom.Validate((StringSegment)"foo\\"), "Case: {0}", "foo\\");
                XAssert.IsFalse(PathAtom.Validate((StringSegment)"fo\\o"), "Case: {0}", "fo\\o");
            }
        }

        [Fact]
        public void ValidPathAtoms()
        {
            foreach (string validPathAtom in s_validPathAtoms)
            {
                XAssert.IsTrue(PathAtom.Validate((StringSegment)validPathAtom), "Case: {0}", validPathAtom);
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestBadPathNameFileExistence()
        {
            var result = FileUtilities.TryProbePathExistence(@"\\mscorlib.dll", followSymlink: false);
            XAssert.IsTrue(result.Succeeded);
            XAssert.AreEqual(PathExistence.Nonexistent, result.Result);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestBadPathNameGetFileAttributes()
        {
            uint attrs = FileSystemWin.GetFileAttributesW(@"\\mscorlib.dll");
            var hr = Marshal.GetLastWin32Error();
            XAssert.AreEqual(NativeIOConstants.InvalidFileAttributes, attrs);
            XAssert.AreEqual(NativeIOConstants.ErrorBadPathname, hr);
        }
    }
}
