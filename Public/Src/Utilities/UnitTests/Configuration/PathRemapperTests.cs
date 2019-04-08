// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

namespace Test.BuildXL.Utilities
{
    /// <summary>
    /// Tests <see cref="PathRemapper"/>
    /// </summary>
    public class PathRemapperTests
    {
        [Fact]
        public void AbsolutePathRemap()
        {
            PathTable pt1;
            PathTable pt2;
            SetupPathTablesForTest(out pt1, out pt2);

            TestAbsolutePathEquality(pt1, pt2, A("x","a","b","c"));
            TestAbsolutePathEquality(pt1, pt2, A("d","z","y","x"));
            TestAbsolutePathEquality(pt1, pt2, A("x","a","b","d"));
            TestAbsolutePathEquality(pt1, pt2, A("c","z","y","x"));
        }

        [Fact]
        public void RelativePathRemap()
        {
            PathTable pt1;
            PathTable pt2;
            SetupPathTablesForTest(out pt1, out pt2);

            TestRelativePathEquality(pt1, pt2, "singleComponent");
            TestRelativePathEquality(pt1, pt2, R("a","b","c"));
            TestRelativePathEquality(pt1, pt2, R("x","y","z"));
            TestRelativePathEquality(pt1, pt2, R("y","X"));
            TestRelativePathEquality(pt1, pt2, R("foo","bar","test","file.txt"));
        }

        private static void TestRelativePathEquality(PathTable pt1, PathTable pt2, string relativePath)
        {
            var remapper = new PathRemapper(pt1, pt2);
            var r1 = RelativePath.Create(pt1.StringTable, relativePath);

            var r1Remap = remapper.Remap(r1);
            var r2 = RelativePath.Create(pt2.StringTable, relativePath);

            XAssert.AreEqual(r1.ToString(pt1.StringTable), r2.ToString(pt2.StringTable));
            XAssert.AreEqual(r1Remap, r2);
        }

        private static void TestAbsolutePathEquality(PathTable pt1, PathTable pt2, string absolutePath)
        {
            var remapper = new PathRemapper(pt1, pt2);
            var r1 = AbsolutePath.Create(pt1, absolutePath);

            var r1Remap = remapper.Remap(r1);
            var r2 = AbsolutePath.Create(pt2, absolutePath);

            XAssert.AreEqual(r1.ToString(pt1), r2.ToString(pt2));
            XAssert.AreEqual(r1Remap, r2);
        }

        private static void SetupPathTablesForTest(out PathTable pt1, out PathTable pt2)
        {
            // Create path tables with unique entries to start.
            pt1 = new PathTable();
            AbsolutePath.Create(pt1, A("x","a","b","c"));

            pt2 = new PathTable();
            AbsolutePath.Create(pt2, A("d","z","y","x"));
        }

        [Fact]
        public void TestAbsolutePathModuloCustomRemap()
        {
            PathTable pt1;
            PathTable pt2;
            SetupPathTablesForTest(out pt1, out pt2);

            TestAbsolutePathModuloCustomRemap(
                pt1,
                pt2,
                p => p.Replace(A("x"), A("y")),
                A("x","a","b","c"),
                A("y","a","b","c"));
        }

        [Fact]
        public void TestRelativePathModuloCustomRemap()
        {
            PathTable pt1;
            PathTable pt2;
            SetupPathTablesForTest(out pt1, out pt2);

            TestRelativePathModuloCustomRemap(
                pt1,
                pt2,
                a => a + "_x",
                R("a","b","c"),
                R("a_x","b_x","c_x"));
        }

        private static void TestAbsolutePathModuloCustomRemap(
            PathTable pt1,
            PathTable pt2,
            Func<string, string> customRemapper,
            string absolutePath,
            string expectedAbsolutePath)
        {
            var remapper = new PathRemapper(pt1, pt2, pathStringRemapper: customRemapper);
            var r1 = AbsolutePath.Create(pt1, absolutePath);

            var r1Remap = remapper.Remap(r1);
            var r2 = AbsolutePath.Create(pt2, expectedAbsolutePath);

            XAssert.AreEqual(r2, r1Remap, $"Expected: {r2.ToString(pt2)}, Actual: {r1Remap.ToString(pt2)}");
        }

        private static void TestRelativePathModuloCustomRemap(
            PathTable pt1,
            PathTable pt2,
            Func<string, string> customRemapper,
            string relativePath,
            string expectedRelativePath)
        {
            var remapper = new PathRemapper(pt1, pt2, pathAtomStringRemapper: customRemapper);
            var r1 = RelativePath.Create(pt1.StringTable, relativePath);

            var r1Remap = remapper.Remap(r1);
            var r2 = RelativePath.Create(pt2.StringTable, expectedRelativePath);

            XAssert.AreEqual(r2, r1Remap, $"Expected: {r2.ToString(pt2.StringTable)}, Actual: {r1Remap.ToString(pt2.StringTable)}");
        }
    }
}
