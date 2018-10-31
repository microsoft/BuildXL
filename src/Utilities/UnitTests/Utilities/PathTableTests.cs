// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class PathTableTests : XunitBuildXLTest
    {
        public PathTableTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void Basic()
        {
            var pt = new PathTable();
            int c1 = pt.Count;
            XAssert.IsTrue(c1 > 0);

            AbsolutePath id = AbsolutePath.Create(pt, A("c", "a", "b", "c"));
            string str = id.ToString(pt);
            XAssert.AreEqual(A("c", "a", "b", "c"), str);

            int c2 = pt.Count;
            XAssert.IsTrue(c2 > c1);

            AbsolutePath.Create(pt, A("c", "a", "b", "c", "d"));
            AbsolutePath.Create(pt, A("c", "a", "b", "c"));

            int c3 = pt.Count;
            XAssert.IsTrue(c3 > c2);

            int size = pt.SizeInBytes;
            XAssert.IsTrue(size > 0);

            pt.Freeze();

            size = pt.SizeInBytes;
            XAssert.IsTrue(size > 0);

            int c4 = pt.Count;
            XAssert.AreEqual(c3, c4);
        }

        [Fact]
        public void ExpandName()
        {
            var pt = new PathTable();
            AbsolutePath id = AbsolutePath.Create(pt, A("c", "a", "b", "c"));
            XAssert.AreEqual("c", id.GetName(pt).ToString(pt.StringTable));

            id = id.GetParent(pt);
            XAssert.AreEqual("b", id.GetName(pt).ToString(pt.StringTable));

            id = id.GetParent(pt);
            XAssert.AreEqual("a", id.GetName(pt).ToString(pt.StringTable));

            id = id.GetParent(pt);
            XAssert.AreEqual(A("c", ""), id.ToString(pt));
        }

        [Fact]
        public void ManyPaths()
        {
            var pt = new PathTable();
            var sb = new StringBuilder();

            for (char ch1 = 'A'; ch1 <= 'Z'; ch1++)
            {
                for (char ch2 = 'a'; ch2 <= 'z'; ch2++)
                {
                    for (char ch3 = 'A'; ch3 <= 'Z'; ch3++)
                    {
                        sb.Length = 0;
                        sb.AppendFormat(A("C","{0}","{1}","{2}"), ch1, ch2, ch3);
                        AbsolutePath.Create(pt, sb.ToString());
                    }
                }
            }
        }

        [Fact]
        public void IsPathWithinTree()
        {
            var pt = new PathTable();
            AbsolutePath tree = AbsolutePath.Create(pt, A("c", ""));
            AbsolutePath path = AbsolutePath.Create(pt, A("c", "windows"));
            XAssert.IsTrue(path.IsWithin(pt, tree));
            XAssert.IsFalse(tree.IsWithin(pt, path));
            XAssert.IsTrue(tree.IsWithin(pt, tree));
            XAssert.IsTrue(path.IsWithin(pt, path));
        }

        [Fact]
        public void TryExpandPathRelativeToAnother()
        {
            var pt = new PathTable();
            AbsolutePath root = AbsolutePath.Create(pt, A("c", "dir", "root"));
            AbsolutePath immediateDescendant = AbsolutePath.Create(pt, A("c", "dir", "root", "file"));
            AbsolutePath furtherDescendant = AbsolutePath.Create(pt, A("c", "dir", "root", "moredir", "file2"));
            AbsolutePath sibling = AbsolutePath.Create(pt, A("c", "dir", "sibling"));

            RelativePath immediateDescendantPath;
            XAssert.IsTrue(root.TryGetRelative(pt, immediateDescendant, out immediateDescendantPath));
            XAssert.AreEqual("file", immediateDescendantPath.ToString(pt.StringTable));

            RelativePath furtherDescendantPath;
            XAssert.IsTrue(root.TryGetRelative(pt, furtherDescendant, out furtherDescendantPath));
            XAssert.AreEqual(R("moredir", "file2"), furtherDescendantPath.ToString(pt.StringTable));

            RelativePath siblingPath;
            XAssert.IsFalse(root.TryGetRelative(pt, sibling, out siblingPath));
            XAssert.AreEqual(RelativePath.Invalid, siblingPath);

            RelativePath emptyPath;
            XAssert.IsTrue(root.TryGetRelative(pt, root, out emptyPath));
            XAssert.AreEqual(string.Empty, emptyPath.ToString(pt.StringTable));
        }

        [Fact]
        public void GetParentDirectory()
        {
            var pt = new PathTable();
            AbsolutePath cid = AbsolutePath.Create(pt, A("c", "a", "b", "c"));
            AbsolutePath bid = cid.GetParent(pt);
            AbsolutePath aid = bid.GetParent(pt);
            AbsolutePath root = aid.GetParent(pt);
            AbsolutePath invalid = root.GetParent(pt);

            XAssert.AreEqual(A("c", "a", "b", "c"), cid.ToString(pt));
            XAssert.AreEqual(A("c", "a", "b"), bid.ToString(pt));
            XAssert.AreEqual(A("c", "a"), aid.ToString(pt));
            XAssert.AreEqual(A("c"), root.ToString(pt));
            XAssert.AreEqual(AbsolutePath.Invalid, invalid);
        }

        [Fact]
        public void TryGetValue()
        {
            var pt = new PathTable();
            AbsolutePath other;

            XAssert.IsFalse(AbsolutePath.TryGet(pt, (StringSegment)string.Empty, out other));

            XAssert.IsFalse(AbsolutePath.TryGet(pt, (StringSegment)A("c", "d"), out other));

            AbsolutePath id = AbsolutePath.Create(pt, A("c", "a", "b", "c"));
            XAssert.IsTrue(AbsolutePath.TryGet(pt, (StringSegment)A("c", "a", "b", "c"), out other));
            XAssert.AreEqual(id, other);

            XAssert.IsTrue(AbsolutePath.TryGet(pt, (StringSegment)A("c", "a", "b"), out other));

            XAssert.IsFalse(AbsolutePath.TryGet(pt, (StringSegment)A("c", "d"), out other));
        }

        [Fact]
        public void AddRelativeToDrive()
        {
            var pt = new PathTable();
            AbsolutePath drive = AbsolutePath.Create(pt, A("c"));
            AbsolutePath directory = drive.CreateRelative(pt, @"windows");

            AbsolutePath fullDirectory = AbsolutePath.Create(pt, A("c", "windows"));
            XAssert.AreEqual(directory, fullDirectory);
            XAssert.AreEqual(A("c", "windows"), directory.ToString(pt));
        }

        [SuppressMessage("Microsoft.Globalization", "CA1302:DoNotHardcodeLocaleSpecificStrings", MessageId = "system32")]
        [Fact]
        public void AddRelativeToDrivePlusDirectory()
        {
            var pt = new PathTable();
            AbsolutePath drive = AbsolutePath.Create(pt, A("c", "windows"));
            AbsolutePath directory = drive.CreateRelative(pt, @"system32");

            AbsolutePath fullDirectory = AbsolutePath.Create(pt, A("c","windows", "system32"));
            XAssert.AreEqual(directory, fullDirectory);
            XAssert.AreEqual(A("c", "windows", "system32"), directory.ToString(pt));
        }

        [Fact]
        public void AddAbsoluteToDrivePlusDirectory()
        {
            var pt = new PathTable();
            AbsolutePath drive = AbsolutePath.Create(pt, A("c", "windows", "system32"));
            AbsolutePath directory = drive.CreateRelative(pt, A("c", "windows"));

            AbsolutePath fullDirectory = AbsolutePath.Create(pt, A("c", "windows"));
            XAssert.AreEqual(directory, fullDirectory);
            XAssert.AreEqual(A("c", "windows"), directory.ToString(pt));
        }

        [Fact]
        public void RemoveExtension()
        {
            var pt = new PathTable();

            // remove a single char extension
            AbsolutePath id1 = AbsolutePath.Create(pt, A("c", "a.c"));
            AbsolutePath id2 = id1.RemoveExtension(pt);
            XAssert.AreEqual(A("c", "a"), id2.ToString(pt));

            // remove a multi char extension
            id1 = AbsolutePath.Create(pt, A("c", "a.cpp"));
            id2 = id1.RemoveExtension(pt);
            XAssert.AreEqual(A("c", "a"), id2.ToString(pt));

            // remove nothing
            id1 = AbsolutePath.Create(pt, A("c", "a"));
            id2 = id1.RemoveExtension(pt);
            XAssert.AreEqual(id1, id2);

            // remove a single char extension
            id1 = AbsolutePath.Create(pt, A("c", "ab.c"));
            id2 = id1.RemoveExtension(pt);
            XAssert.AreEqual(A("c", "ab"), id2.ToString(pt));

            // remove a multi char extension
            id1 = AbsolutePath.Create(pt, A("c", "ab.cpp"));
            id2 = id1.RemoveExtension(pt);
            XAssert.AreEqual(A("c", "ab"), id2.ToString(pt));

            // remove nothing
            id1 = AbsolutePath.Create(pt, A("c", "ab"));
            id2 = id1.RemoveExtension(pt);
            XAssert.AreEqual(id1, id2);

            // remove a single char extension
            id1 = AbsolutePath.Create(pt, A("c", "xyz", "ab.c"));
            id2 = id1.RemoveExtension(pt);
            XAssert.AreEqual(A("c", "xyz", "ab"), id2.ToString(pt));

            // remove a multi char extension
            id1 = AbsolutePath.Create(pt, A("c", "xyz", "ab.cpp"));
            id2 = id1.RemoveExtension(pt);
            XAssert.AreEqual(A("c", "xyz", "ab"), id2.ToString(pt));

            // remove nothing
            id1 = AbsolutePath.Create(pt, A("c", "xyz", "ab"));
            id2 = id1.RemoveExtension(pt);
            XAssert.AreEqual(id1, id2);

            // remove a single char extension
            id1 = AbsolutePath.Create(pt, A("c", "xyz", "ab.xyz.c"));
            id2 = id1.RemoveExtension(pt);
            XAssert.AreEqual(A("c", "xyz", "ab.xyz"), id2.ToString(pt));

            // remove a multi char extension
            id1 = AbsolutePath.Create(pt, A("c", "xyz", "ab.xyz.cpp"));
            id2 = id1.RemoveExtension(pt);
            XAssert.AreEqual(A("c", "xyz", "ab.xyz"), id2.ToString(pt));

            id1 = AbsolutePath.Create(pt, A("c", "xyz", ".cpp"));
            id2 = id1.RemoveExtension(pt);
            XAssert.AreEqual(id1, id2);
        }

        [Fact]
        public void ChangeExtension()
        {
            var pt = new PathTable();

            // change a single char extension
            AbsolutePath id1 = AbsolutePath.Create(pt, A("c", "a.c"));
            AbsolutePath id2 = id1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(A("c", "a.d"), id2.ToString(pt));

            // change a multi char extension
            id1 = AbsolutePath.Create(pt, A("c", "a.cpp"));
            id2 = id1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(A("c", "a.d"), id2.ToString(pt));

            // change nothing
            id1 = AbsolutePath.Create(pt, A("c", "a"));
            id2 = id1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(A("c", "a.d"), id2.ToString(pt));

            // change a single char extension
            id1 = AbsolutePath.Create(pt, A("c", "ab.c"));
            id2 = id1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(A("c", "ab.d"), id2.ToString(pt));

            // change a multi char extension
            id1 = AbsolutePath.Create(pt, A("c", "ab.cpp"));
            id2 = id1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(A("c", "ab.d"), id2.ToString(pt));

            // change nothing
            id1 = AbsolutePath.Create(pt, A("c", "ab"));
            id2 = id1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(A("c", "ab.d"), id2.ToString(pt));

            // change a single char extension
            id1 = AbsolutePath.Create(pt, A("c", "xyz", "ab.c"));
            id2 = id1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(A("c", "xyz", "ab.d"), id2.ToString(pt));

            // change a multi char extension
            id1 = AbsolutePath.Create(pt, A("c", "xyz", "ab.cpp"));
            id2 = id1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(A("c", "xyz", "ab.d"), id2.ToString(pt));

            // change nothing
            id1 = AbsolutePath.Create(pt, A("c", "xyz", "ab"));
            id2 = id1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(A("c", "xyz", "ab.d"), id2.ToString(pt));

            // change a single char extension
            id1 = AbsolutePath.Create(pt, A("c", "xyz", "ab.xyz.c"));
            id2 = id1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(A("c", "xyz", "ab.xyz.d"), id2.ToString(pt));

            // change a multi char extension
            id1 = AbsolutePath.Create(pt, A("c", "xyz", "ab.xyz.cpp"));
            id2 = id1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(A("c", "xyz", "ab.xyz.d"), id2.ToString(pt));

            id1 = AbsolutePath.Create(pt, A("c", "xyz", ".cpp"));
            id2 = id1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(A("c", "xyz", ".d"), id2.ToString(pt));
        }

        [Fact]
        public void RelocateSubtree()
        {
            var pt = new PathTable();
            AbsolutePath.Create(pt, A("c", "a"));
            AbsolutePath id2 = AbsolutePath.Create(pt, A("c", "a", "b"));
            AbsolutePath.Create(pt, A("c", "a", "b", "c"));
            AbsolutePath id4 = AbsolutePath.Create(pt, A("c", "a", "b", "c", "d.cpp"));
            AbsolutePath id5 = AbsolutePath.Create(pt, A("c", "a", "e"));
            AbsolutePath id6 = id4.Relocate(pt, id2, id5, PathAtom.Create(pt.StringTable, ".obj"));
            XAssert.AreEqual(A("c", "a", "e", "c", "d.obj"), id6.ToString(pt));

            AbsolutePath.Create(pt, A("c", "a"));
            id2 = AbsolutePath.Create(pt, A("c", "a", "b"));
            AbsolutePath.Create(pt, A("c", "a", "b", "c"));
            id4 = AbsolutePath.Create(pt, A("c", "a", "b", "c", "d"));
            id5 = AbsolutePath.Create(pt, A("c", "a", "e"));
            id6 = id4.Relocate(pt, id2, id5, PathAtom.Create(pt.StringTable, ".obj"));
            XAssert.AreEqual(A("c", "a", "e", "c", "d.obj"), id6.ToString(pt));
        }

        [Fact]
        public void IsValid()
        {
            AbsolutePath path = AbsolutePath.Invalid;
            XAssert.IsFalse(path.IsValid);
        }

        [Fact]
        public void AbsolutePathEquality()
        {
            StructTester.TestEquality(
                baseValue: new AbsolutePath(123),
                equalValue: new AbsolutePath(123),
                notEqualValues: new AbsolutePath[] { AbsolutePath.Invalid, new AbsolutePath(124) },
                eq: (a, b) => a == b,
                neq: (a, b) => a != b);
        }

        [Fact]
        public void CaseFolding()
        {
            var pt = new PathTable();

            // shouldn't be interference between different hierarchies and case should be preserved
            AbsolutePath id1 = AbsolutePath.Create(pt, A("c", "a", "b", "c"));
            AbsolutePath id3 = AbsolutePath.Create(pt, A("C", "A", "B", "C"));

            AbsolutePath id2 = AbsolutePath.Create(pt, A("c", "X", "A", "B", "C"));
            XAssert.AreEqual(A("c", "a", "b", "c"), id1.ToString(pt));
            XAssert.AreEqual(A("c", "X", "A", "B", "C"), id2.ToString(pt));

            // we expect to find an existing path when using different casing
            // AbsolutePath id3 = AbsolutePath.Create(pt, A("c","A","B","C"));
            XAssert.AreEqual(id1, id3);

            // and we expect for common paths to have "first one in wins" casing
            AbsolutePath id4 = AbsolutePath.Create(pt, A("C", "A", "B", "C", "D"));
            XAssert.AreEqual(A("c", "a", "b", "c", "D"), id4.ToString(pt));
        }

        /// <summary>
        /// Verify that empty PathTable can be serialized and deserialized
        /// </summary>
        [Fact]
        public async Task EmptySerialization()
        {
            var st = new StringTable();
            var pt = new PathTable(st);

            PathTable pt2;
            using (MemoryStream ms = new MemoryStream())
            {
                using (BuildXLWriter writer = new BuildXLWriter(true, ms, true, logStats: true))
                {
                    pt.Serialize(writer);
                }

                ms.Position = 0;

                using (BuildXLReader reader = new BuildXLReader(true, ms, true))
                {
                    pt2 = await PathTable.DeserializeAsync(reader, Task.FromResult(st));
                }
            }

            XAssert.AreEqual(pt.Count, pt2.Count);
        }

        [Fact]
        public async Task Serialization()
        {
            var st = new StringTable();
            var pt = new PathTable(st);

            string path1 = A("c", "a", "b", "c");
            var ap1 = AbsolutePath.Create(pt, path1);
            string path2 = A("c", "d", "c", "a");
            var ap2 = AbsolutePath.Create(pt, path2);

            string path3 = A("d","a","c","a");
            var ap3 = AbsolutePath.Create(pt, path3);

            string path3Caps = A("D","A","c","a");
            var ap3Caps = AbsolutePath.Create(pt, path3Caps);

            PathTable pt2;
            using (MemoryStream ms = new MemoryStream())
            {
                using (BuildXLWriter writer = new BuildXLWriter(true, ms, true, logStats: true))
                {
                    pt.Serialize(writer);
                }

                ms.Position = 0;

                using (BuildXLReader reader = new BuildXLReader(true, ms, true))
                {
                    pt2 = await PathTable.DeserializeAsync(reader, Task.FromResult(st));
                }
            }

            // Retrieve AbsolutePaths created with the original table from the deserialized one
            XAssert.AreEqual(path1, ap1.ToString(pt));
            XAssert.AreEqual(path1, ap1.ToString(pt2));
            XAssert.AreEqual(path2, ap2.ToString(pt2));
            XAssert.AreEqual(path3, ap3.ToString(pt2));

            // Recreate the paths and make sure they match the originals
            var ap1Recreated = AbsolutePath.Create(pt2, path1);
            var ap2Recreated = AbsolutePath.Create(pt2, path2);
            var ap3Recreated = AbsolutePath.Create(pt2, path3);
            var ap3CapsRecreated = AbsolutePath.Create(pt2, path3Caps);
            XAssert.AreEqual(ap1, ap1Recreated);
            XAssert.AreEqual(ap2, ap2Recreated);
            XAssert.AreEqual(ap3, ap3Recreated);
            XAssert.AreEqual(ap3, ap3CapsRecreated);

            // Make sure a new path can be added
            string path4 = A("c", "a", "s", "d");
            var ap4 = AbsolutePath.Create(pt2, path4);
            XAssert.AreEqual(path4, ap4.ToString(pt2));
        }

        /// <summary>
        /// Verify all paths prior to serialization are still present after deserialization.
        /// Namely, recreating the AbsolutePath from the string should yield the same AbsolutePath
        /// from the original path table
        /// TODO: This code currently does not actually reproduce the issue. Waiting for access to data
        /// from actual repro to flush out test to reproduce the issue.
        /// </summary>
        [Fact]
        public async Task Serialization_Bug695424()
        {
            var st = new StringTable();
            var pt = new PathTable(st);
            ConcurrentBigSet<AbsolutePath> paths = new ConcurrentBigSet<AbsolutePath>();
            List<string> pathStrings = new List<string>();

            int max = 32769;

            StringBuilder builder = new StringBuilder();
            builder.Append(A("c", "i"));
            var length = builder.Length;
            for (int i = 0; i < 100; i++)
            {
                builder.Length = length;
                builder.Append(i);
                builder.Append('\\');

                var jLength = builder.Length;
                for (int j = 0; j < 10; j++)
                {
                    builder.Length = jLength;
                    builder.Append('j');
                    builder.Append(j);
                    builder.Append('\\');

                    var kLenght = builder.Length;
                    for (int k = 0; k < 66; k++)
                    {
                        builder.Length = kLenght;
                        builder.Append('k');
                        builder.Append(k);
                        builder.Append('\\');
                        if (pt.Count < max)
                        {
                            paths.Add(AbsolutePath.Create(pt, builder.ToString()));
                        }
                        else
                        {
                            pathStrings.Add(builder.ToString());
                        }
                    }
                }
            }

            PathTable pt2;
            using (MemoryStream ms = new MemoryStream())
            {
                using (BuildXLWriter writer = new BuildXLWriter(true, ms, true, logStats: true))
                {
                    pt.Serialize(writer);
                }

                ms.Position = 0;

                using (BuildXLReader reader = new BuildXLReader(true, ms, true))
                {
                    pt2 = await PathTable.DeserializeAsync(reader, Task.FromResult(st));
                }
            }

            foreach (var pathString in pathStrings)
            {
                AbsolutePath.Create(pt2, pathString);
            }

            foreach (var path in paths.UnsafeGetList())
            {
                var pathString = path.ToString(pt).ToUpperInvariant();
                var path2 = AbsolutePath.Create(pt2, pathString);
                XAssert.AreEqual(path, path2);
            }
        }
    }
}
