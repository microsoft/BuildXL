// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

        #region "IsValidPathAtom"

        private static readonly string[] validPathAtoms =
        {
            "dir1",
            "dir 1",
            "dir1 ",
            " dir1 ",
            "_$@#",
            "dir1!"
        };

        private static readonly string[] invalidPathAtoms =
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
            foreach (string invalidPathAtom in invalidPathAtoms)
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
            foreach (string validPathAtom in validPathAtoms)
            {
                XAssert.IsTrue(PathAtom.Validate((StringSegment)validPathAtom), "Case: {0}", validPathAtom);
            }
        }

#endregion
    }
}
