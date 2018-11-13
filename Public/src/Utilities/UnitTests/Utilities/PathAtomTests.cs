// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using System.IO;

namespace Test.BuildXL.Utilities
{
    public sealed class PathAtomTests : XunitBuildXLTest
    {
        public PathAtomTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void IsInitialized()
        {
            PathAtom atom = default(PathAtom);
            XAssert.IsFalse(atom.IsValid);

            var st = new StringTable(0);
            atom = PathAtom.Create(st, "AAA");
            XAssert.AreEqual("AAA", atom.ToString(st));
            XAssert.IsTrue(atom.IsValid);
        }

        [Fact]
        public void TryCreate()
        {
            var st = new StringTable(0);

            PathAtom atom;
            XAssert.IsTrue(PathAtom.TryCreate(st, "AAA", out atom));
            XAssert.AreEqual("AAA", atom.ToString(st));
            XAssert.IsFalse(PathAtom.TryCreate(st, Path.DirectorySeparatorChar+"AAA", out atom));
            XAssert.IsFalse(PathAtom.TryCreate(st, Path.VolumeSeparatorChar+"AAA", out atom));
            XAssert.IsFalse(PathAtom.TryCreate(st, "AAA"+Path.DirectorySeparatorChar, out atom));
            XAssert.IsFalse(PathAtom.TryCreate(st, string.Empty, out atom));

            atom = PathAtom.Create(st, "BBB");
            XAssert.AreEqual("BBB", atom.ToString(st));
        }

        [Fact]
        public void IsValidPathChar()
        {
            XAssert.IsTrue(PathAtom.IsValidPathAtomChar('a'));
            XAssert.IsTrue(PathAtom.IsValidPathAtomChar('z'));
            XAssert.IsTrue(PathAtom.IsValidPathAtomChar('A'));
            XAssert.IsTrue(PathAtom.IsValidPathAtomChar('Z'));
            XAssert.IsTrue(PathAtom.IsValidPathAtomChar('0'));
            XAssert.IsTrue(PathAtom.IsValidPathAtomChar('9'));
            XAssert.IsTrue(PathAtom.IsValidPathAtomChar('.'));

            XAssert.IsFalse(PathAtom.IsValidPathAtomChar(Path.DirectorySeparatorChar));
            XAssert.IsFalse(PathAtom.IsValidPathAtomChar('/'));

            XAssert.AreEqual(OperatingSystemHelper.IsUnixOS, PathAtom.IsValidPathAtomChar(':'));
            XAssert.AreEqual(OperatingSystemHelper.IsUnixOS, PathAtom.IsValidPathAtomChar('?'));

        }

        [Fact]
        public void Equality()
        {
            var st = new StringTable(0);
            PathAtom a1 = PathAtom.Create(st, "AAA");
            PathAtom a2 = PathAtom.Create(st, "AAA");
            PathAtom a3 = PathAtom.Create(st, "BBB");

            XAssert.IsTrue(a1.Equals(a1));
            XAssert.IsTrue(a1.Equals(a2));
            XAssert.IsTrue(a2.Equals(a1));
            XAssert.IsFalse(a1.Equals(a3));
            XAssert.IsFalse(a2.Equals(a3));

            XAssert.IsTrue(a1.Equals((object)a1));
            XAssert.IsTrue(a1.Equals((object)a2));
            XAssert.IsTrue(a2.Equals((object)a1));
            XAssert.IsFalse(a1.Equals((object)a3));
            XAssert.IsFalse(a2.Equals((object)a3));
            XAssert.IsFalse(a2.Equals("XYZ"));

            XAssert.IsTrue(a1 == a2);
            XAssert.IsTrue(a2 == a1);
            XAssert.IsFalse(a1 == a3);
            XAssert.IsFalse(a2 == a3);

            XAssert.IsFalse(a1 != a2);
            XAssert.IsFalse(a2 != a1);
            XAssert.IsTrue(a1 != a3);
            XAssert.IsTrue(a2 != a3);

            int h1 = a1.GetHashCode();
            int h2 = a2.GetHashCode();
            XAssert.AreEqual(h1, h2);

            StringId id1 = a1.StringId;
            StringId id2 = a2.StringId;
            XAssert.AreEqual(id1, id2);
        }

        [Fact]
        public void RemoveExtension()
        {
            var st = new StringTable(0);

            // remove a single char extension
            PathAtom pa1 = PathAtom.Create(st, @"a.c");
            PathAtom pa2 = pa1.RemoveExtension(st);
            XAssert.AreEqual(@"a", pa2.ToString(st));

            // remove a multi char extension
            pa1 = PathAtom.Create(st, @"a.cpp");
            pa2 = pa1.RemoveExtension(st);
            XAssert.AreEqual(@"a", pa2.ToString(st));

            // remove nothing
            pa1 = PathAtom.Create(st, @"a");
            pa2 = pa1.RemoveExtension(st);
            XAssert.AreEqual(pa1, pa2);

            // remove a single char extension
            pa1 = PathAtom.Create(st, @"ab.c");
            pa2 = pa1.RemoveExtension(st);
            XAssert.AreEqual(@"ab", pa2.ToString(st));

            // remove a multi char extension
            pa1 = PathAtom.Create(st, @"ab.cpp");
            pa2 = pa1.RemoveExtension(st);
            XAssert.AreEqual(@"ab", pa2.ToString(st));

            // remove nothing
            pa1 = PathAtom.Create(st, @"ab");
            pa2 = pa1.RemoveExtension(st);
            XAssert.AreEqual(pa1, pa2);

            pa1 = PathAtom.Create(st, @"ab.xyz.c");
            pa2 = pa1.RemoveExtension(st);
            XAssert.AreEqual(@"ab.xyz", pa2.ToString(st));

            // remove a multi char extension
            pa1 = PathAtom.Create(st, @"ab.xyz.cpp");
            pa2 = pa1.RemoveExtension(st);
            XAssert.AreEqual(@"ab.xyz", pa2.ToString(st));

            pa1 = PathAtom.Create(st, @".cpp");
            pa2 = pa1.RemoveExtension(st);
            XAssert.AreEqual(pa1, pa2);
        }

        [Fact]
        public void ChangeExtension()
        {
            var st = new StringTable(0);

            // change a single char extension
            PathAtom pa1 = PathAtom.Create(st, @"a.c");
            PathAtom pa2 = pa1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"a.d", pa2.ToString(st));

            // change a multi char extension
            pa1 = PathAtom.Create(st, @"a.cpp");
            pa2 = pa1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"a.d", pa2.ToString(st));

            // change nothing
            pa1 = PathAtom.Create(st, @"a");
            pa2 = pa1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"a.d", pa2.ToString(st));

            // change a single char extension
            pa1 = PathAtom.Create(st, @"ab.c");
            pa2 = pa1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"ab.d", pa2.ToString(st));

            // change a multi char extension
            pa1 = PathAtom.Create(st, @"ab.cpp");
            pa2 = pa1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"ab.d", pa2.ToString(st));

            // change nothing
            pa1 = PathAtom.Create(st, @"ab");
            pa2 = pa1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"ab.d", pa2.ToString(st));

            // change a single char extension
            pa1 = PathAtom.Create(st, @"ab.xyz.c");
            pa2 = pa1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"ab.xyz.d", pa2.ToString(st));

            // change a multi char extension
            pa1 = PathAtom.Create(st, @"ab.xyz.cpp");
            pa2 = pa1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"ab.xyz.d", pa2.ToString(st));

            pa1 = PathAtom.Create(st, @".cpp");
            pa2 = pa1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@".d", pa2.ToString(st));

            pa1 = PathAtom.Create(st, @"a.cpp");
            pa2 = pa1.ChangeExtension(st, PathAtom.Invalid);
            XAssert.AreEqual(@"a", pa2.ToString(st));
        }

        [Fact]
        public void GetExtensionTests()
        {
            var st = new StringTable(0);

            // get a single char extension
            PathAtom e1 = PathAtom.Create(st, "a.c").GetExtension(st);
            XAssert.AreEqual(@".c", e1.ToString(st));

            // get a multi char extension
            e1 = PathAtom.Create(st, "a.cpp").GetExtension(st);
            XAssert.AreEqual(@".cpp", e1.ToString(st));

            // get nothing
            e1 = PathAtom.Create(st, "a").GetExtension(st);
            XAssert.IsFalse(e1.IsValid);

            // get a single char extension
            e1 = PathAtom.Create(st, "a.c.d").GetExtension(st);
            XAssert.AreEqual(@".d", e1.ToString(st));

            // get a multi char extension
            e1 = PathAtom.Create(st, ".cpp").GetExtension(st);
            XAssert.AreEqual(@".cpp", e1.ToString(st));
        }

        [Fact]
        public void ToStringTest()
        {
            var st = new StringTable(0);

            Assert.Throws<NotImplementedException>(() =>
                {
                    PathAtom a = PathAtom.Create(st, "AAA");
#pragma warning disable 618
                    a.ToString();
#pragma warning restore 618
                });
        }

        [Fact]
        public void Concat()
        {
            var st = new StringTable(0);

            PathAtom a1 = PathAtom.Create(st, "AAA");
            PathAtom a2 = PathAtom.Create(st, "BBB");
            PathAtom a3 = a1.Concat(st, a2);
            XAssert.AreEqual("AAABBB", a3.ToString(st));
        }

        [Fact]
        public void Validate()
        {
            XAssert.IsTrue(PathAtom.Validate((StringSegment)"AAA"));
            XAssert.IsFalse(PathAtom.Validate((StringSegment)("AAA"+Path.VolumeSeparatorChar)));
        }

        [Fact]
        public void CaseInsensitiveEquals()
        {
            var st = new StringTable(0);

            PathAtom a1 = PathAtom.Create(st, "A");
            PathAtom a2 = PathAtom.Create(st, "a");

            XAssert.IsFalse(a1.Equals(a2));
            XAssert.IsTrue(a1.CaseInsensitiveEquals(st, a2));
        }
    }
}
