// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class FileArtifactTests : XunitBuildXLTest
    {
        public FileArtifactTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void FileArtifactEquality()
        {
            var pathTable = new PathTable();
            AbsolutePath file1 = AbsolutePath.Create(pathTable, A("t","file1.txt"));
            AbsolutePath file2 = AbsolutePath.Create(pathTable, A("t", "file2.txt"));

            StructTester.TestEquality(
                baseValue: FileArtifact.CreateSourceFile(file1),
                equalValue: FileArtifact.CreateSourceFile(file1),
                notEqualValues: new[]
                                {
                                    FileArtifact.CreateSourceFile(file2),
                                    FileArtifact.CreateSourceFile(file2).CreateNextWrittenVersion(),
                                    FileArtifact.CreateSourceFile(file1).CreateNextWrittenVersion(),
                                    FileArtifact.CreateSourceFile(file1).CreateNextWrittenVersion().CreateNextWrittenVersion()
                                },
                eq: (left, right) => left == right,
                neq: (left, right) => left != right,
                skipHashCodeForNotEqualValues: true);
        }

        [Fact]
        public void SourceOrOutput()
        {
            var pathTable = new PathTable();
            FileArtifact fa1 = FileArtifact.CreateSourceFile(AbsolutePath.Create(pathTable, A("C", "AAA", "CCC")));
            XAssert.IsTrue(fa1.IsSourceFile);
            XAssert.IsFalse(fa1.IsOutputFile);

            FileArtifact fa2 = fa1.CreateNextWrittenVersion();
            XAssert.IsFalse(fa2.IsSourceFile);
            XAssert.IsTrue(fa2.IsOutputFile);
        }

        [Fact]
        public void IsInitialized()
        {
            FileArtifact p = default(FileArtifact);
            XAssert.IsFalse(p.IsValid);

            var pt = new PathTable();
            p = FileArtifact.CreateSourceFile(AbsolutePath.Create(pt, A("C","AAA","CCC")));
            XAssert.AreEqual(A("C", "AAA", "CCC"), p.Path.ToString(pt));
            XAssert.IsTrue(p.IsValid);
        }

        [Fact]
        public void CreateFileArtifact()
        {
            var pt = new PathTable();
            AbsolutePath da1 = AbsolutePath.Create(pt, A("c"));
            RelativePath relPath = RelativePath.Create(pt.StringTable, R("a","b"));
            FileArtifact fa = FileArtifact.CreateSourceFile(da1.Combine(pt, relPath));
            XAssert.AreEqual(A("c","a","b"), fa.Path.ToString(pt));

            da1 = AbsolutePath.Create(pt, A("c"));
            PathAtom atom = PathAtom.Create(pt.StringTable, "a");
            fa = FileArtifact.CreateSourceFile(da1.Combine(pt, atom));
            XAssert.AreEqual(A("c","a"), fa.Path.ToString(pt));

            da1 = AbsolutePath.Create(pt, A("c"));
            atom = PathAtom.Create(pt.StringTable, "a");
            PathAtom atom2 = PathAtom.Create(pt.StringTable, "b");
            fa = FileArtifact.CreateSourceFile(da1.Combine(pt, atom, atom2));
            XAssert.AreEqual(A("c", "a", "b"), fa.Path.ToString(pt));

            da1 = AbsolutePath.Create(pt, A("c"));
            atom = PathAtom.Create(pt.StringTable, "a");
            atom2 = PathAtom.Create(pt.StringTable, "b");
            PathAtom atom3 = PathAtom.Create(pt.StringTable, "c");
            fa = FileArtifact.CreateSourceFile(da1.Combine(pt, atom, atom2, atom3));
            XAssert.AreEqual(A("c", "a", "b", "c"), fa.Path.ToString(pt));
        }

        [Fact]
        public void ToStringTest()
        {
            var pt = new PathTable();
            string f = FileArtifact.CreateSourceFile(AbsolutePath.Create(pt, A("F"))).ToString();
            XAssert.IsTrue(f.Contains("File"), f);
            XAssert.IsTrue(f.Contains("(source)"), f);

            string g = FileArtifact.CreateOutputFile(AbsolutePath.Create(pt, A("G"))).ToString();
            XAssert.IsTrue(g.Contains("File"), g);
            XAssert.IsTrue(g.Contains("(output)"), g);

            string h = FileArtifact.CreateOutputFile(AbsolutePath.Create(pt, A("H"))).CreateNextWrittenVersion().ToString();
            XAssert.IsTrue(h.Contains("File"), h);
            XAssert.IsTrue(h.Contains("(rewrite:2)"), h);

            string i = FileArtifact.Invalid.ToString();
            XAssert.IsTrue(i.Contains("Invalid"), i);
        }
    }
}
