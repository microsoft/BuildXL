// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class AbsolutePathTestsUnix : XunitBuildXLTest
    {
        public AbsolutePathTestsUnix(ITestOutputHelper output)
            : base(output)
        {
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void IsInitialized()
        {
            AbsolutePath p = default(AbsolutePath);
            XAssert.IsFalse(p.IsValid);

            var pt = new PathTable();
            p = AbsolutePath.Create(pt, @"/usr/local");
            XAssert.AreEqual(@"/usr/local", p.ToString(pt));
            XAssert.IsTrue(p.IsValid);
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void TryCreate()
        {
            var pt = new PathTable();
            AbsolutePath p;
            XAssert.IsTrue(AbsolutePath.TryCreate(pt, @"/usr/local", out p));
            XAssert.AreEqual(@"/usr/local", p.ToString(pt));


            XAssert.IsTrue(AbsolutePath.TryCreate(pt, @"/..", out p));
            XAssert.AreEqual(@"/", p.ToString(pt));
            XAssert.IsTrue(AbsolutePath.TryCreate(pt, @"/../..", out p));
            XAssert.AreEqual(@"/", p.ToString(pt));
            XAssert.IsTrue(AbsolutePath.TryCreate(pt, @"/usr/etc/aaa/../../../../../../", out p));
            XAssert.AreEqual(@"/", p.ToString(pt));
            XAssert.IsTrue(AbsolutePath.TryCreate(pt, @"/usr/a/../b.txt", out p));
            XAssert.AreEqual(@"/usr/b.txt", p.ToString(pt));
            XAssert.IsTrue(AbsolutePath.TryCreate(pt, @"/usr/a/../../../../b.txt", out p));
            XAssert.AreEqual(@"/b.txt", p.ToString(pt));

            XAssert.IsFalse(AbsolutePath.TryCreate(pt, @"..", out p));
            XAssert.IsFalse(AbsolutePath.TryCreate(pt, @".", out p));
            XAssert.IsFalse(AbsolutePath.TryCreate(pt, @"\", out p));
            XAssert.IsFalse(AbsolutePath.TryCreate(pt, @"\usr", out p));
            XAssert.IsFalse(AbsolutePath.TryCreate(pt, @"\..", out p));

            // Try creating a path that would be invalid on Windows
            XAssert.IsTrue(AbsolutePath.TryCreate(pt, "/Volumes/Source/sd/dev/file:", out p));

            p = AbsolutePath.Create(pt, @"//");
            XAssert.AreEqual(@"/", p.ToString(pt));

            p = AbsolutePath.Create(pt, @"/");
            XAssert.AreEqual(@"/", p.ToString(pt));

            p = AbsolutePath.Create(pt, @"/.");
            XAssert.AreEqual(@"/", p.ToString(pt));

            p = AbsolutePath.Create(pt, @"/usr");
            XAssert.AreEqual(@"/usr", p.ToString(pt));

            p = AbsolutePath.Create(pt, @"/usr/.");
            XAssert.AreEqual(@"/usr", p.ToString(pt));

            p = AbsolutePath.Create(pt, @"/usr/..");
            XAssert.AreEqual(@"/", p.ToString(pt));

            p = AbsolutePath.Create(pt, @"/usr/local/..");
            XAssert.AreEqual(@"/usr", p.ToString(pt));
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void Equality()
        {
            var pt = new PathTable();
            AbsolutePath a1 = AbsolutePath.Create(pt, @"/usr/local");
            AbsolutePath a2 = AbsolutePath.Create(pt, @"/usr/local");
            AbsolutePath a3 = AbsolutePath.Create(pt, @"/usr/local/bin");

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

            // TODO: Case insensitive comparisons, currently we do not differentiate between casing!
            AbsolutePath a3DifferentCase = AbsolutePath.Create(pt, @"/usr/local/Bin");
            XAssert.IsTrue(a3.Equals(a3DifferentCase));
            XAssert.IsTrue(a3.Equals((object) a3DifferentCase));
            XAssert.IsTrue(a3 == a3DifferentCase);
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void Combine()
        {
            var pt = new PathTable();
            AbsolutePath a1 = AbsolutePath.Create(pt, @"/");
            PathAtom p1 = PathAtom.Create(pt.StringTable, "home");
            AbsolutePath a2 = a1.Combine(pt, p1);
            XAssert.AreEqual(@"/home", a2.ToString(pt));

            a1 = AbsolutePath.Create(pt, @"/home");
            p1 = PathAtom.Create(pt.StringTable, "root");
            a2 = a1.Combine(pt, p1);
            XAssert.AreEqual(@"/home/root", a2.ToString(pt));

            a1 = AbsolutePath.Create(pt, @"/home");
            p1 = PathAtom.Create(pt.StringTable, "root");
            PathAtom p2 = PathAtom.Create(pt.StringTable, "documents");
            a2 = a1.Combine(pt, p1, p2);
            XAssert.AreEqual(@"/home/root/documents", a2.ToString(pt));

            a1 = AbsolutePath.Create(pt, @"/home");
            p1 = PathAtom.Create(pt.StringTable, "root");
            p2 = PathAtom.Create(pt.StringTable, "documents");
            PathAtom p3 = PathAtom.Create(pt.StringTable, "config");
            a2 = a1.Combine(pt, p1, p2, p3);
            XAssert.AreEqual(@"/home/root/documents/config", a2.ToString(pt));

            a1 = AbsolutePath.Create(pt, @"/");
            RelativePath rp = RelativePath.Create(pt.StringTable, @"home/root");
            a2 = a1.Combine(pt, rp);
            XAssert.AreEqual(@"/home/root", a2.ToString(pt));

            a1 = AbsolutePath.Create(pt, @"/home");
            rp = RelativePath.Create(pt.StringTable, @"root/documents");
            a2 = a1.Combine(pt, rp);
            XAssert.AreEqual(@"/home/root/documents", a2.ToString(pt));
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void Concat()
        {
            var pt = new PathTable();
            AbsolutePath a1 = AbsolutePath.Create(pt, @"/ho");
            PathAtom p1 = PathAtom.Create(pt.StringTable, "me");
            AbsolutePath a2 = a1.Concat(pt, p1);
            XAssert.AreEqual(@"/home", a2.ToString(pt));
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void RemoveExtension()
        {
            var pt = new PathTable();

            // remove a single char extension
            AbsolutePath ap1 = AbsolutePath.Create(pt, @"/a.c");
            AbsolutePath ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(@"/a", ap2.ToString(pt));

            // remove a multi char extension
            ap1 = AbsolutePath.Create(pt, @"/a.cpp");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(@"/a", ap2.ToString(pt));

            // remove nothing
            ap1 = AbsolutePath.Create(pt, @"/a");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(ap1, ap2);

            // remove a single char extension
            ap1 = AbsolutePath.Create(pt, @"/ab.c");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(@"/ab", ap2.ToString(pt));

            // remove a multi char extension
            ap1 = AbsolutePath.Create(pt, @"/ab.cpp");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(@"/ab", ap2.ToString(pt));

            // remove nothing
            ap1 = AbsolutePath.Create(pt, @"/ab");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(ap1, ap2);

            // remove a single char extension
            ap1 = AbsolutePath.Create(pt, @"/xyz/ab.c");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(@"/xyz/ab", ap2.ToString(pt));

            // remove a multi char extension
            ap1 = AbsolutePath.Create(pt, @"/xyz/ab.cpp");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(@"/xyz/ab", ap2.ToString(pt));

            // remove nothing
            ap1 = AbsolutePath.Create(pt, @"/xyz/ab");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(ap1, ap2);

            // remove a single char extension
            ap1 = AbsolutePath.Create(pt, @"/xyz/ab.xyz.c");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(@"/xyz/ab.xyz", ap2.ToString(pt));

            // remove a multi char extension
            ap1 = AbsolutePath.Create(pt, @"/xyz/ab.xyz.cpp");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(@"/xyz/ab.xyz", ap2.ToString(pt));

            ap1 = AbsolutePath.Create(pt, @"/xyz/.cpp");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(ap1, ap2);
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void ChangeExtension()
        {
            var pt = new PathTable();

            // change a single char extension
            AbsolutePath ap1 = AbsolutePath.Create(pt, @"/a.c");
            AbsolutePath ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"/a.d", ap2.ToString(pt));

            // change a multi char extension
            ap1 = AbsolutePath.Create(pt, @"/a.cpp");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"/a.d", ap2.ToString(pt));

            // change nothing
            ap1 = AbsolutePath.Create(pt, @"/a");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"/a.d", ap2.ToString(pt));

            // change a single char extension
            ap1 = AbsolutePath.Create(pt, @"/ab.c");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"/ab.d", ap2.ToString(pt));

            // change a multi char extension
            ap1 = AbsolutePath.Create(pt, @"/ab.cpp");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"/ab.d", ap2.ToString(pt));

            // change nothing
            ap1 = AbsolutePath.Create(pt, @"/ab");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"/ab.d", ap2.ToString(pt));

            // change a single char extension
            ap1 = AbsolutePath.Create(pt, @"/xyz/ab.c");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"/xyz/ab.d", ap2.ToString(pt));

            // change a multi char extension
            ap1 = AbsolutePath.Create(pt, @"/xyz/ab.cpp");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"/xyz/ab.d", ap2.ToString(pt));

            // change nothing
            ap1 = AbsolutePath.Create(pt, @"/xyz/ab");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"/xyz/ab.d", ap2.ToString(pt));

            // change a single char extension
            ap1 = AbsolutePath.Create(pt, @"/xyz/ab.xyz.c");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"/xyz/ab.xyz.d", ap2.ToString(pt));

            // change a multi char extension
            ap1 = AbsolutePath.Create(pt, @"/xyz/ab.xyz.cpp");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"/xyz/ab.xyz.d", ap2.ToString(pt));

            ap1 = AbsolutePath.Create(pt, @"/xyz/.cpp");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"/xyz/.d", ap2.ToString(pt));

            ap1 = AbsolutePath.Create(pt, @"/xyz/a.cpp");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Invalid);
            XAssert.AreEqual(@"/xyz/a", ap2.ToString(pt));
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void GetExtension()
        {
            var pt = new PathTable();

            // get a single char extension
            PathAtom e1 = AbsolutePath.Create(pt, @"/a.c").GetExtension(pt);
            XAssert.AreEqual(@".c", e1.ToString(pt.StringTable));

            // get a multi char extension
            e1 = AbsolutePath.Create(pt, @"/a.cpp").GetExtension(pt);
            XAssert.AreEqual(@".cpp", e1.ToString(pt.StringTable));

            // get nothing
            e1 = AbsolutePath.Create(pt, @"/a").GetExtension(pt);
            XAssert.IsFalse(e1.IsValid);

            // get a single char extension
            e1 = AbsolutePath.Create(pt, @"/a.c.d").GetExtension(pt);
            XAssert.AreEqual(@".d", e1.ToString(pt.StringTable));

            // get a multi char extension
            e1 = AbsolutePath.Create(pt, @"/.cpp").GetExtension(pt);
            XAssert.AreEqual(@".cpp", e1.ToString(pt.StringTable));
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void GetName()
        {
            var pt = new PathTable();
            AbsolutePath da = AbsolutePath.Create(pt, @"/a");
            PathAtom atom = da.GetName(pt);
            XAssert.AreEqual(@"a", atom.ToString(pt.StringTable));
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void GetParent()
        {
            var pt = new PathTable();
            AbsolutePath da = AbsolutePath.Create(pt, @"/a");
            AbsolutePath parent = da.GetParent(pt);
            XAssert.AreEqual(@"/", parent.ToString(pt));

            da = AbsolutePath.Create(pt, @"/a/b");
            parent = da.GetParent(pt);
            XAssert.AreEqual(@"/a", parent.ToString(pt));
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void GetRoot()
        {
            var pt = new PathTable();
            AbsolutePath da = AbsolutePath.Create(pt, @"/a");
            AbsolutePath root = da.GetRoot(pt);
            XAssert.AreEqual(@"/", root.ToString(pt));

            da = AbsolutePath.Create(pt, @"/");
            root = da.GetRoot(pt);
            XAssert.AreEqual(@"/", root.ToString(pt));

            da = AbsolutePath.Create(pt, @"/a/b");
            root = da.GetRoot(pt);
            XAssert.AreEqual(@"/", root.ToString(pt));
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void IsValidPathChar()
        {
            XAssert.IsTrue(AbsolutePath.IsValidAbsolutePathChar('a'));
            XAssert.IsTrue(AbsolutePath.IsValidAbsolutePathChar('z'));
            XAssert.IsTrue(AbsolutePath.IsValidAbsolutePathChar('A'));
            XAssert.IsTrue(AbsolutePath.IsValidAbsolutePathChar('Z'));
            XAssert.IsTrue(AbsolutePath.IsValidAbsolutePathChar('0'));
            XAssert.IsTrue(AbsolutePath.IsValidAbsolutePathChar('9'));
            XAssert.IsTrue(AbsolutePath.IsValidAbsolutePathChar('.'));
            XAssert.IsTrue(AbsolutePath.IsValidAbsolutePathChar('\\'));
            XAssert.IsTrue(AbsolutePath.IsValidAbsolutePathChar('/'));
            XAssert.IsTrue(AbsolutePath.IsValidAbsolutePathChar(':'));
            XAssert.IsTrue(AbsolutePath.IsValidAbsolutePathChar('?'));
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void RelocateForm1()
        {
            // replace the file extension
            var pt = new PathTable();
            {
                AbsolutePath d1 = AbsolutePath.Create(pt, @"/a/b");
                AbsolutePath d2 = AbsolutePath.Create(pt, @"/a/x");
                AbsolutePath f1 = AbsolutePath.Create(pt, @"/a/b/c/d.cpp");
                PathAtom ext = PathAtom.Create(pt.StringTable, ".obj");
                XAssert.IsTrue(f1.IsWithin(pt, d1));
                XAssert.IsFalse(f1.IsWithin(pt, d2));
                AbsolutePath f2 = f1.Relocate(pt, d1, d2, ext);
                XAssert.AreEqual(@"/a/x/c/d.obj", f2.ToString(pt));
            }

            // strip the extension instead of replacing it
            pt = new PathTable();
            {
                AbsolutePath d1 = AbsolutePath.Create(pt, @"/a/b");
                AbsolutePath d2 = AbsolutePath.Create(pt, @"/a/x");
                AbsolutePath f1 = AbsolutePath.Create(pt, @"/a/b/c/d.cpp");
                XAssert.IsTrue(f1.IsWithin(pt, d1));
                XAssert.IsFalse(f1.IsWithin(pt, d2));
                AbsolutePath f2 = f1.Relocate(pt, d1, d2, PathAtom.Invalid);
                XAssert.AreEqual(@"/a/x/c/d", f2.ToString(pt));
            }

            // leave the extension alone
            pt = new PathTable();
            {
                AbsolutePath d1 = AbsolutePath.Create(pt, @"/a/b");
                AbsolutePath d2 = AbsolutePath.Create(pt, @"/a/x");
                AbsolutePath f1 = AbsolutePath.Create(pt, @"/a/b/c/d.cpp");
                XAssert.IsTrue(f1.IsWithin(pt, d1));
                XAssert.IsFalse(f1.IsWithin(pt, d2));
                AbsolutePath f2 = f1.Relocate(pt, d1, d2);
                XAssert.AreEqual(@"/a/x/c/d.cpp", f2.ToString(pt));
            }
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void RelocateForm2()
        {
            // replace the file extension
            var pt = new PathTable();
            {
                AbsolutePath d2 = AbsolutePath.Create(pt, @"/a/x");
                AbsolutePath f1 = AbsolutePath.Create(pt, @"/a/b/c/d.cpp");
                PathAtom ext = PathAtom.Create(pt.StringTable, ".obj");
                XAssert.IsFalse(f1.IsWithin(pt, d2));
                AbsolutePath f2 = f1.Relocate(pt, d2, ext);
                XAssert.AreEqual(@"/a/x/d.obj", f2.ToString(pt));
            }

            // strip the extension instead of replacing it
            pt = new PathTable();
            {
                AbsolutePath d2 = AbsolutePath.Create(pt, @"/a/x");
                AbsolutePath f1 = AbsolutePath.Create(pt, @"/a/b/c/d.cpp");
                XAssert.IsFalse(f1.IsWithin(pt, d2));
                AbsolutePath f2 = f1.Relocate(pt, d2, PathAtom.Invalid);
                XAssert.AreEqual(@"/a/x/d", f2.ToString(pt));
            }

            // leave the extension alone
            pt = new PathTable();
            {
                AbsolutePath d2 = AbsolutePath.Create(pt, @"/a/x");
                AbsolutePath f1 = AbsolutePath.Create(pt, @"/a/b/c/d.cpp");
                XAssert.IsFalse(f1.IsWithin(pt, d2));
                AbsolutePath f2 = f1.Relocate(pt, d2);
                XAssert.AreEqual(@"/a/x/d.cpp", f2.ToString(pt));
            }
        }
    }
}
