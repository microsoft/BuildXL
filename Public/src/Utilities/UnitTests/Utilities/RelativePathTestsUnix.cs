// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class RelativePathTestsUnix : XunitBuildXLTest
    {
        public RelativePathTestsUnix(ITestOutputHelper output) : base(output) { }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void IsInitialized()
        {
            RelativePath p = default(RelativePath);
            XAssert.IsFalse(p.IsValid);

            var st = new StringTable(0);
            p = RelativePath.Create(st, @"usr/src");
            XAssert.AreEqual(@"usr/src", p.ToString(st));
            XAssert.IsTrue(p.IsValid);
            XAssert.IsFalse(p.IsEmpty);

            p = RelativePath.Create(st, string.Empty);
            XAssert.IsTrue(p.IsValid);
            XAssert.IsTrue(p.IsEmpty);
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void TryCreate()
        {
            var st = new StringTable(0);

            RelativePath p;
            XAssert.IsTrue(RelativePath.TryCreate(st, @"usr/src", out p));
            XAssert.AreEqual(@"usr/src", p.ToString(st));

            XAssert.IsFalse(RelativePath.TryCreate(st, @"C\:AAA", out p));
            XAssert.IsFalse(RelativePath.TryCreate(st, @"AAA:", out p));
            XAssert.IsFalse(RelativePath.TryCreate(st, @":AAA", out p));
            XAssert.IsFalse(RelativePath.TryCreate(st, @"..", out p));

            p = RelativePath.Create(st, ".");
            XAssert.AreEqual(string.Empty, p.ToString(st));

            p = RelativePath.Create(st, "usr");
            XAssert.AreEqual("usr", p.ToString(st));

            p = RelativePath.Create(st, @"usr/.");
            XAssert.AreEqual("usr", p.ToString(st));

            p = RelativePath.Create(st, @"usr\..");
            XAssert.AreEqual(string.Empty, p.ToString(st));

            p = RelativePath.Create(st, @"usr/src/..");
            XAssert.AreEqual("usr", p.ToString(st));

            PathAtom a1 = PathAtom.Create(st, "usr");
            PathAtom a2 = PathAtom.Create(st, "src");
            PathAtom a3 = PathAtom.Create(st, "include");
            p = RelativePath.Create(a1, a2, a3);
            XAssert.AreEqual(@"usr/src/include", p.ToString(st));
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void IsValidPathChar()
        {
            XAssert.IsTrue(RelativePath.IsValidRelativePathChar('a'));
            XAssert.IsTrue(RelativePath.IsValidRelativePathChar('z'));
            XAssert.IsTrue(RelativePath.IsValidRelativePathChar('A'));
            XAssert.IsTrue(RelativePath.IsValidRelativePathChar('Z'));
            XAssert.IsTrue(RelativePath.IsValidRelativePathChar('0'));
            XAssert.IsTrue(RelativePath.IsValidRelativePathChar('9'));
            XAssert.IsTrue(RelativePath.IsValidRelativePathChar('.'));
            XAssert.IsTrue(RelativePath.IsValidRelativePathChar('\\'));
            XAssert.IsTrue(RelativePath.IsValidRelativePathChar('/'));

            XAssert.IsFalse(RelativePath.IsValidRelativePathChar(':'));
            XAssert.IsFalse(RelativePath.IsValidRelativePathChar('?'));
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void Equality()
        {
            var st = new StringTable(0);
            RelativePath a1 = RelativePath.Create(st, @"usr/src");
            RelativePath a2 = RelativePath.Create(st, @"usr/src");
            RelativePath a3 = RelativePath.Create(st, @"lib/src");

            XAssert.IsTrue(a1.Equals(a1));
            XAssert.IsTrue(a1.Equals(a2));
            XAssert.IsTrue(a2.Equals(a1));
            XAssert.IsFalse(a1.Equals(a3));
            XAssert.IsFalse(a2.Equals(a3));

            XAssert.IsTrue(a1.Equals((object) a1));
            XAssert.IsTrue(a1.Equals((object) a2));
            XAssert.IsTrue(a2.Equals((object) a1));
            XAssert.IsFalse(a1.Equals((object) a3));
            XAssert.IsFalse(a2.Equals((object) a3));
            XAssert.IsFalse(a2.Equals("home"));

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

            a1 = RelativePath.Create(st, string.Empty);
            XAssert.AreEqual(0, a1.GetHashCode());

            XAssert.IsFalse(a1.Equals(a2));
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void CaseInsensitiveEquals()
        {
            var st = new StringTable(0);
            RelativePath a1 = RelativePath.Create(st, @"usr/SRC");
            RelativePath a2 = RelativePath.Create(st, @"usr/src");

            XAssert.IsTrue(a1.CaseInsensitiveEquals(st, a2));
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void Conversion()
        {
            var st = new StringTable(0);
            PathAtom a1 = PathAtom.Create(st, "usr");
            RelativePath p1 = RelativePath.Create(a1);
            XAssert.AreEqual("usr", p1.ToString(st));
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void Combine()
        {
            var st = new StringTable(0);
            RelativePath p1 = RelativePath.Create(st, "usr");
            PathAtom a1 = PathAtom.Create(st, "src");
            RelativePath p2 = p1.Combine(a1);
            XAssert.AreEqual(@"usr/src", p2.ToString(st));

            p1 = RelativePath.Create(st, string.Empty);
            p2 = p1.Combine(a1);
            XAssert.AreEqual(@"src", p2.ToString(st));

            p1 = RelativePath.Create(st, "usr");
            PathAtom a2 = PathAtom.Create(st, "include");
            p2 = p1.Combine(a1, a2);
            XAssert.AreEqual(@"usr/src/include", p2.ToString(st));

            p1 = RelativePath.Create(st, "usr");
            a2 = PathAtom.Create(st, "include");
            PathAtom a3 = PathAtom.Create(st, "x64");
            p2 = p1.Combine(a1, a2, a3);
            XAssert.AreEqual(@"usr/src/include/x64", p2.ToString(st));

            RelativePath p3 = p1.Combine(p2);
            XAssert.AreEqual(@"usr/usr/src/include/x64", p3.ToString(st));
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void RemoveExtension()
        {
            var st = new StringTable(0);

            // remove a single char extension
            RelativePath rp1 = RelativePath.Create(st, @"a.c");
            RelativePath rp2 = rp1.RemoveExtension(st);
            XAssert.AreEqual(@"a", rp2.ToString(st));

            // remove a multi char extension
            rp1 = RelativePath.Create(st, @"a.cpp");
            rp2 = rp1.RemoveExtension(st);
            XAssert.AreEqual(@"a", rp2.ToString(st));

            // remove nothing
            rp1 = RelativePath.Create(st, @"a");
            rp2 = rp1.RemoveExtension(st);
            XAssert.AreEqual(rp1, rp2);

            // remove a single char extension
            rp1 = RelativePath.Create(st, @"ab.c");
            rp2 = rp1.RemoveExtension(st);
            XAssert.AreEqual(@"ab", rp2.ToString(st));

            // remove a multi char extension
            rp1 = RelativePath.Create(st, @"ab.cpp");
            rp2 = rp1.RemoveExtension(st);
            XAssert.AreEqual(@"ab", rp2.ToString(st));

            // remove nothing
            rp1 = RelativePath.Create(st, @"ab");
            rp2 = rp1.RemoveExtension(st);
            XAssert.AreEqual(rp1, rp2);

            rp1 = RelativePath.Create(st, @"ab.xyz.c");
            rp2 = rp1.RemoveExtension(st);
            XAssert.AreEqual(@"ab.xyz", rp2.ToString(st));

            // remove a multi char extension
            rp1 = RelativePath.Create(st, @"ab.xyz.cpp");
            rp2 = rp1.RemoveExtension(st);
            XAssert.AreEqual(@"ab.xyz", rp2.ToString(st));

            rp1 = RelativePath.Create(st, @".cpp");
            rp2 = rp1.RemoveExtension(st);
            XAssert.AreEqual(rp1, rp2);

            // remove a single char extension
            rp1 = RelativePath.Create(st, @"src/a.c");
            rp2 = rp1.RemoveExtension(st);
            XAssert.AreEqual(@"src/a", rp2.ToString(st));

            // remove a multi char extension
            rp1 = RelativePath.Create(st, @"src/a.cpp");
            rp2 = rp1.RemoveExtension(st);
            XAssert.AreEqual(@"src/a", rp2.ToString(st));

            // remove nothing
            rp1 = RelativePath.Create(st, @"src/a");
            rp2 = rp1.RemoveExtension(st);
            XAssert.AreEqual(rp1, rp2);

            // remove a single char extension
            rp1 = RelativePath.Create(st, @"src/ab.c");
            rp2 = rp1.RemoveExtension(st);
            XAssert.AreEqual(@"src/ab", rp2.ToString(st));

            // remove a multi char extension
            rp1 = RelativePath.Create(st, @"src/ab.cpp");
            rp2 = rp1.RemoveExtension(st);
            XAssert.AreEqual(@"src/ab", rp2.ToString(st));

            // remove nothing
            rp1 = RelativePath.Create(st, @"src/ab");
            rp2 = rp1.RemoveExtension(st);
            XAssert.AreEqual(rp1, rp2);

            rp1 = RelativePath.Create(st, @"src/ab.xyz.c");
            rp2 = rp1.RemoveExtension(st);
            XAssert.AreEqual(@"src/ab.xyz", rp2.ToString(st));

            // remove a multi char extension
            rp1 = RelativePath.Create(st, @"src/ab.xyz.cpp");
            rp2 = rp1.RemoveExtension(st);
            XAssert.AreEqual(@"src/ab.xyz", rp2.ToString(st));

            rp1 = RelativePath.Create(st, @"src/.cpp");
            rp2 = rp1.RemoveExtension(st);
            XAssert.AreEqual(rp1, rp2);

            rp1 = RelativePath.Create(st, string.Empty);
            rp2 = rp1.RemoveExtension(st);
            XAssert.AreEqual(rp1, rp2);
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void ChangeExtension()
        {
            var st = new StringTable(0);

            // change a single char extension
            RelativePath rp1 = RelativePath.Create(st, @"a.c");
            RelativePath rp2 = rp1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"a.d", rp2.ToString(st));

            // change a multi char extension
            rp1 = RelativePath.Create(st, @"a.cpp");
            rp2 = rp1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"a.d", rp2.ToString(st));

            // change nothing
            rp1 = RelativePath.Create(st, @"a");
            rp2 = rp1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"a.d", rp2.ToString(st));

            // change a single char extension
            rp1 = RelativePath.Create(st, @"ab.c");
            rp2 = rp1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"ab.d", rp2.ToString(st));

            // change a multi char extension
            rp1 = RelativePath.Create(st, @"ab.cpp");
            rp2 = rp1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"ab.d", rp2.ToString(st));

            // change nothing
            rp1 = RelativePath.Create(st, @"ab");
            rp2 = rp1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"ab.d", rp2.ToString(st));

            // change a single char extension
            rp1 = RelativePath.Create(st, @"ab.xyz.c");
            rp2 = rp1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"ab.xyz.d", rp2.ToString(st));

            // change a multi char extension
            rp1 = RelativePath.Create(st, @"ab.xyz.cpp");
            rp2 = rp1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"ab.xyz.d", rp2.ToString(st));

            rp1 = RelativePath.Create(st, @".cpp");
            rp2 = rp1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@".d", rp2.ToString(st));

            // change a single char extension
            rp1 = RelativePath.Create(st, @"src/a.c");
            rp2 = rp1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"src/a.d", rp2.ToString(st));

            // change a multi char extension
            rp1 = RelativePath.Create(st, @"src/a.cpp");
            rp2 = rp1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"src/a.d", rp2.ToString(st));

            // change nothing
            rp1 = RelativePath.Create(st, @"src/a");
            rp2 = rp1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"src/a.d", rp2.ToString(st));

            // change a single char extension
            rp1 = RelativePath.Create(st, @"src/ab.c");
            rp2 = rp1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"src/ab.d", rp2.ToString(st));

            // change a multi char extension
            rp1 = RelativePath.Create(st, @"src/ab.cpp");
            rp2 = rp1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"src/ab.d", rp2.ToString(st));

            // change nothing
            rp1 = RelativePath.Create(st, @"src/ab");
            rp2 = rp1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"src/ab.d", rp2.ToString(st));

            // change a single char extension
            rp1 = RelativePath.Create(st, @"src/ab.xyz.c");
            rp2 = rp1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"src/ab.xyz.d", rp2.ToString(st));

            // change a multi char extension
            rp1 = RelativePath.Create(st, @"src/ab.xyz.cpp");
            rp2 = rp1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"src/ab.xyz.d", rp2.ToString(st));

            rp1 = RelativePath.Create(st, @"src/.cpp");
            rp2 = rp1.ChangeExtension(st, PathAtom.Create(st, ".d"));
            XAssert.AreEqual(@"src/.d", rp2.ToString(st));

            // nop change
            rp1 = RelativePath.Create(st, @"src/ab.xyz.cpp");
            rp2 = rp1.ChangeExtension(st, PathAtom.Create(st, ".cpp"));
            XAssert.AreEqual(rp1, rp2);

            rp1 = RelativePath.Create(st, @"src/ab.xyz.cpp");
            rp2 = rp1.ChangeExtension(st, PathAtom.Invalid);
            XAssert.AreEqual(@"src/ab.xyz", rp2.ToString(st));
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void GetExtensionTests()
        {
            var st = new StringTable(0);

            // get a single char extension
            PathAtom e1 = RelativePath.Create(st, "a.c").GetExtension(st);
            XAssert.AreEqual(@".c", e1.ToString(st));

            // get a multi char extension
            e1 = RelativePath.Create(st, "a.cpp").GetExtension(st);
            XAssert.AreEqual(@".cpp", e1.ToString(st));

            // get nothing
            e1 = RelativePath.Create(st, "a").GetExtension(st);
            XAssert.IsFalse(e1.IsValid);

            // get a single char extension
            e1 = RelativePath.Create(st, "a.c.d").GetExtension(st);
            XAssert.AreEqual(@".d", e1.ToString(st));

            // get a multi char extension
            e1 = RelativePath.Create(st, ".cpp").GetExtension(st);
            XAssert.AreEqual(@".cpp", e1.ToString(st));
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void ToStringTest()
        {
            var st = new StringTable(0);
            Assert.Throws<NotImplementedException>(() =>
            {
                RelativePath rp = RelativePath.Create(st, @"usr");
#pragma warning disable 618
                rp.ToString();
#pragma warning restore 618
            });
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void GetName()
        {
            var st = new StringTable(0);

            RelativePath rp = RelativePath.Create(st, @"usr");
            PathAtom atom = rp.GetName();
            XAssert.AreEqual(@"usr", atom.ToString(st));

            rp = RelativePath.Create(st, @"usr/src");
            atom = rp.GetName();
            XAssert.AreEqual(@"src", atom.ToString(st));
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void GetParent()
        {
            var st = new StringTable(0);

            RelativePath rp = RelativePath.Create(st, @"usr");
            RelativePath parent = rp.GetParent();
            XAssert.AreEqual(string.Empty, parent.ToString(st));

            rp = RelativePath.Create(st, @"usr/src");
            parent = rp.GetParent();
            XAssert.AreEqual(@"usr", parent.ToString(st));

            rp = RelativePath.Create(st, @"usr/src/include");
            parent = rp.GetParent();
            XAssert.AreEqual(@"usr/src", parent.ToString(st));
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void GetRelativeRoot()
        {
            var st = new StringTable(0);

            RelativePath rp = RelativePath.Create(st, @"usr");
            RelativePath root = rp.GetRelativeRoot();
            XAssert.AreEqual(rp.ToString(st), root.ToString(st));

            rp = RelativePath.Create(st, @"usr/src");
            root = rp.GetRelativeRoot();
            XAssert.AreEqual(@"usr", root.ToString(st));

            rp = RelativePath.Create(st, @"usr/src/include");
            root = rp.GetRelativeRoot();
            XAssert.AreEqual(@"usr", root.ToString(st));
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void Concat()
        {
            var st = new StringTable(0);

            RelativePath rp = RelativePath.Create(st, @"usr/lib");
            PathAtom p1 = PathAtom.Create(st, "s");
            RelativePath rp2 = rp.Concat(st, p1);
            XAssert.AreEqual(@"usr/libs", rp2.ToString(st));
        }
    }
}
