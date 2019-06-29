// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class AbsolutePathTests : XunitBuildXLTest
    {
        public AbsolutePathTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void IsInitialized()
        {
            AbsolutePath p = default(AbsolutePath);
            XAssert.IsFalse(p.IsValid);

            var pt = new PathTable();
            p = AbsolutePath.Create(pt, @"C:\AAA\CCC");
            XAssert.AreEqual(@"C:\AAA\CCC", p.ToString(pt));
            XAssert.IsTrue(p.IsValid);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TryCreate()
        {
            var pt = new PathTable();
            AbsolutePath p;
            XAssert.IsTrue(AbsolutePath.TryCreate(pt, @"C:\AAA\CCC", out p));
            XAssert.AreEqual(@"C:\AAA\CCC", p.ToString(pt));
            XAssert.IsTrue(AbsolutePath.TryCreate(pt, @"C:\..", out p));
            XAssert.AreEqual(@"C:\", p.ToString(pt));
            XAssert.IsTrue(AbsolutePath.TryCreate(pt, @"C:\..\..\..\..", out p));
            XAssert.AreEqual(@"C:\", p.ToString(pt));
            XAssert.IsTrue(AbsolutePath.TryCreate(pt, @"C:\a\..\b.txt", out p));
            XAssert.AreEqual(@"C:\b.txt", p.ToString(pt));
            XAssert.IsTrue(AbsolutePath.TryCreate(pt, @"C:\a\..\..\..\..\b.txt", out p));
            XAssert.AreEqual(@"C:\b.txt", p.ToString(pt));


            XAssert.IsFalse(AbsolutePath.TryCreate(pt, @"C\::AAA", out p));
            XAssert.IsFalse(AbsolutePath.TryCreate(pt, @"AAA:", out p));
            XAssert.IsFalse(AbsolutePath.TryCreate(pt, @":AAA", out p));
            XAssert.IsFalse(AbsolutePath.TryCreate(pt, @"..", out p));
            XAssert.IsFalse(AbsolutePath.TryCreate(pt, @".", out p));
            XAssert.IsFalse(AbsolutePath.TryCreate(pt, @"C:\:", out p));
            XAssert.IsFalse(AbsolutePath.TryCreate(pt, @"1:\", out p));

            p = AbsolutePath.Create(pt, @"C:/");
            XAssert.AreEqual(@"C:\", p.ToString(pt));

            p = AbsolutePath.Create(pt, @"C:\");
            XAssert.AreEqual(@"C:\", p.ToString(pt));

            p = AbsolutePath.Create(pt, @"C:\.");
            XAssert.AreEqual(@"C:\", p.ToString(pt));

            p = AbsolutePath.Create(pt, @"C:\BBB");
            XAssert.AreEqual(@"C:\BBB", p.ToString(pt));

            p = AbsolutePath.Create(pt, @"C:\BBB\.");
            XAssert.AreEqual(@"C:\BBB", p.ToString(pt));

            p = AbsolutePath.Create(pt, @"C:\BBB\..");
            XAssert.AreEqual(@"C:\", p.ToString(pt));

            p = AbsolutePath.Create(pt, @"C:\BBB\CCC\..");
            XAssert.AreEqual(@"C:\BBB", p.ToString(pt));

            // now test out UNC paths
            XAssert.IsTrue(AbsolutePath.TryCreate(pt, @"\\srv\AAA\CCC", out p));
            XAssert.AreEqual(@"\\srv\AAA\CCC", p.ToString(pt));

            XAssert.IsFalse(AbsolutePath.TryCreate(pt, @"\\srv\..", out p));
            XAssert.IsFalse(AbsolutePath.TryCreate(pt, @"\\srv\..\..", out p));
            XAssert.IsFalse(AbsolutePath.TryCreate(pt, @"\\srv\:", out p));

            p = AbsolutePath.Create(pt, @"\\srv\");
            XAssert.AreEqual(@"\\srv", p.ToString(pt));

            p = AbsolutePath.Create(pt, @"\\srv\.");
            XAssert.AreEqual(@"\\srv", p.ToString(pt));

            p = AbsolutePath.Create(pt, @"\\srv\BBB");
            XAssert.AreEqual(@"\\srv\BBB", p.ToString(pt));

            p = AbsolutePath.Create(pt, @"\\srv\BBB\.");
            XAssert.AreEqual(@"\\srv\BBB", p.ToString(pt));

            p = AbsolutePath.Create(pt, @"\\srv\BBB\..");
            XAssert.AreEqual(@"\\srv", p.ToString(pt));

            p = AbsolutePath.Create(pt, @"\\srv\BBB\CCC\..");
            XAssert.AreEqual(@"\\srv\BBB", p.ToString(pt));
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TryCreateWithDeviceOrNtPrefix()
        {            
            var pt = new PathTable();
            AbsolutePath p;
            XAssert.IsTrue(AbsolutePath.TryCreate(pt, @"\\?\C:\AAA\CCC", out p));
            XAssert.AreEqual(@"C:\AAA\CCC", p.ToString(pt));

            XAssert.IsTrue(AbsolutePath.TryCreate(pt, @"\\.\C:\AAA\CCC", out p));
            XAssert.AreEqual(@"C:\AAA\CCC", p.ToString(pt));

            // TODO: This is not faithful to Wndows behavior. Win32NT paths do not canonicalize .. or slashes.
            XAssert.IsTrue(AbsolutePath.TryCreate(pt, @"\\?\C:\AAA\..\CCC", out p));
            XAssert.AreEqual(@"C:\CCC", p.ToString(pt));
            XAssert.IsTrue(AbsolutePath.TryCreate(pt, @"\\.\C:\AAA\..\CCC", out p));
            XAssert.AreEqual(@"C:\CCC", p.ToString(pt));
            XAssert.IsTrue(AbsolutePath.TryCreate(pt, @"\\.\C:\A/B", out p));
            XAssert.AreEqual(@"C:\A\B", p.ToString(pt));

            int errorPosition;
            AbsolutePath.ParseResult parseResult = AbsolutePath.TryCreate(pt, @"\\?\C:", out p, out errorPosition);
            XAssert.AreEqual(AbsolutePath.ParseResult.DevicePathsNotSupported, parseResult);
            XAssert.AreEqual(0, errorPosition);

            parseResult = AbsolutePath.TryCreate(pt, @"\\.\nul", out p, out errorPosition);
            XAssert.AreEqual(AbsolutePath.ParseResult.DevicePathsNotSupported, parseResult);
            XAssert.AreEqual(0, errorPosition);

            parseResult = AbsolutePath.TryCreate(pt, @"\\.\pipe\abc", out p, out errorPosition);
            XAssert.AreEqual(AbsolutePath.ParseResult.DevicePathsNotSupported, parseResult);
            XAssert.AreEqual(0, errorPosition);

            parseResult = AbsolutePath.TryCreate(pt, @"\\?\C:\foo\:", out p, out errorPosition);
            XAssert.AreEqual(AbsolutePath.ParseResult.FailureDueToInvalidCharacter, parseResult);
            XAssert.AreEqual(11, errorPosition);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void Equality()
        {
            var pt = new PathTable();
            AbsolutePath a1 = AbsolutePath.Create(pt, @"c:\AAA\CCC");
            AbsolutePath a2 = AbsolutePath.Create(pt, @"c:\AAA\CCC");
            AbsolutePath a3 = AbsolutePath.Create(pt, @"c:\BBB\CCC");

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

            // Case insensitive comparisons
            AbsolutePath a3DifferentCase = AbsolutePath.Create(pt, @"C:\bbb\ccc");
            XAssert.IsTrue(a3.Equals(a3DifferentCase));
            XAssert.IsTrue(a3.Equals((object)a3DifferentCase));
            XAssert.IsTrue(a3 == a3DifferentCase);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void Combine()
        {
            var pt = new PathTable();
            AbsolutePath a1 = AbsolutePath.Create(pt, @"C:\");
            PathAtom p1 = PathAtom.Create(pt.StringTable, "A");
            AbsolutePath a2 = a1.Combine(pt, p1);
            XAssert.AreEqual(@"C:\A", a2.ToString(pt));

            a1 = AbsolutePath.Create(pt, @"C:\X");
            p1 = PathAtom.Create(pt.StringTable, "A");
            a2 = a1.Combine(pt, p1);
            XAssert.AreEqual(@"C:\X\A", a2.ToString(pt));

            a1 = AbsolutePath.Create(pt, @"C:\X");
            p1 = PathAtom.Create(pt.StringTable, "A");
            PathAtom p2 = PathAtom.Create(pt.StringTable, "B");
            a2 = a1.Combine(pt, p1, p2);
            XAssert.AreEqual(@"C:\X\A\B", a2.ToString(pt));

            a1 = AbsolutePath.Create(pt, @"C:\X");
            p1 = PathAtom.Create(pt.StringTable, "A");
            p2 = PathAtom.Create(pt.StringTable, "B");
            PathAtom p3 = PathAtom.Create(pt.StringTable, "C");
            a2 = a1.Combine(pt, p1, p2, p3);
            XAssert.AreEqual(@"C:\X\A\B\C", a2.ToString(pt));

            a1 = AbsolutePath.Create(pt, @"C:\");
            RelativePath rp = RelativePath.Create(pt.StringTable, @"A\B");
            a2 = a1.Combine(pt, rp);
            XAssert.AreEqual(@"C:\A\B", a2.ToString(pt));

            a1 = AbsolutePath.Create(pt, @"C:\X");
            rp = RelativePath.Create(pt.StringTable, @"A\B");
            a2 = a1.Combine(pt, rp);
            XAssert.AreEqual(@"C:\X\A\B", a2.ToString(pt));
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void Concat()
        {
            var pt = new PathTable();
            AbsolutePath a1 = AbsolutePath.Create(pt, @"C:\A");
            PathAtom p1 = PathAtom.Create(pt.StringTable, "B");
            AbsolutePath a2 = a1.Concat(pt, p1);
            XAssert.AreEqual(@"C:\AB", a2.ToString(pt));
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void RemoveExtension()
        {
            var pt = new PathTable();

            // remove a single char extension
            AbsolutePath ap1 = AbsolutePath.Create(pt, @"c:\a.c");
            AbsolutePath ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(@"c:\a", ap2.ToString(pt));

            // remove a multi char extension
            ap1 = AbsolutePath.Create(pt, @"c:\a.cpp");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(@"c:\a", ap2.ToString(pt));

            // remove nothing
            ap1 = AbsolutePath.Create(pt, @"c:\a");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(ap1, ap2);

            // remove a single char extension
            ap1 = AbsolutePath.Create(pt, @"c:\ab.c");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(@"c:\ab", ap2.ToString(pt));

            // remove a multi char extension
            ap1 = AbsolutePath.Create(pt, @"c:\ab.cpp");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(@"c:\ab", ap2.ToString(pt));

            // remove nothing
            ap1 = AbsolutePath.Create(pt, @"c:\ab");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(ap1, ap2);

            // remove a single char extension
            ap1 = AbsolutePath.Create(pt, @"c:\xyz\ab.c");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(@"c:\xyz\ab", ap2.ToString(pt));

            // remove a multi char extension
            ap1 = AbsolutePath.Create(pt, @"c:\xyz\ab.cpp");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(@"c:\xyz\ab", ap2.ToString(pt));

            // remove nothing
            ap1 = AbsolutePath.Create(pt, @"c:\xyz\ab");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(ap1, ap2);

            // remove a single char extension
            ap1 = AbsolutePath.Create(pt, @"c:\xyz\ab.xyz.c");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(@"c:\xyz\ab.xyz", ap2.ToString(pt));

            // remove a multi char extension
            ap1 = AbsolutePath.Create(pt, @"c:\xyz\ab.xyz.cpp");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(@"c:\xyz\ab.xyz", ap2.ToString(pt));

            ap1 = AbsolutePath.Create(pt, @"c:\xyz\.cpp");
            ap2 = ap1.RemoveExtension(pt);
            XAssert.AreEqual(ap1, ap2);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void ChangeExtension()
        {
            var pt = new PathTable();

            // change a single char extension
            AbsolutePath ap1 = AbsolutePath.Create(pt, @"c:\a.c");
            AbsolutePath ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"c:\a.d", ap2.ToString(pt));

            // change a multi char extension
            ap1 = AbsolutePath.Create(pt, @"c:\a.cpp");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"c:\a.d", ap2.ToString(pt));

            // change nothing
            ap1 = AbsolutePath.Create(pt, @"c:\a");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"c:\a.d", ap2.ToString(pt));

            // change a single char extension
            ap1 = AbsolutePath.Create(pt, @"c:\ab.c");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"c:\ab.d", ap2.ToString(pt));

            // change a multi char extension
            ap1 = AbsolutePath.Create(pt, @"c:\ab.cpp");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"c:\ab.d", ap2.ToString(pt));

            // change nothing
            ap1 = AbsolutePath.Create(pt, @"c:\ab");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"c:\ab.d", ap2.ToString(pt));

            // change a single char extension
            ap1 = AbsolutePath.Create(pt, @"c:\xyz\ab.c");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"c:\xyz\ab.d", ap2.ToString(pt));

            // change a multi char extension
            ap1 = AbsolutePath.Create(pt, @"c:\xyz\ab.cpp");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"c:\xyz\ab.d", ap2.ToString(pt));

            // change nothing
            ap1 = AbsolutePath.Create(pt, @"c:\xyz\ab");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"c:\xyz\ab.d", ap2.ToString(pt));

            // change a single char extension
            ap1 = AbsolutePath.Create(pt, @"c:\xyz\ab.xyz.c");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"c:\xyz\ab.xyz.d", ap2.ToString(pt));

            // change a multi char extension
            ap1 = AbsolutePath.Create(pt, @"c:\xyz\ab.xyz.cpp");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"c:\xyz\ab.xyz.d", ap2.ToString(pt));

            ap1 = AbsolutePath.Create(pt, @"c:\xyz\.cpp");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Create(pt.StringTable, ".d"));
            XAssert.AreEqual(@"c:\xyz\.d", ap2.ToString(pt));

            ap1 = AbsolutePath.Create(pt, @"c:\xyz\a.cpp");
            ap2 = ap1.ChangeExtension(pt, PathAtom.Invalid);
            XAssert.AreEqual(@"c:\xyz\a", ap2.ToString(pt));
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void GetExtension()
        {
            var pt = new PathTable();

            // get a single char extension
            PathAtom e1 = AbsolutePath.Create(pt, @"c:\a.c").GetExtension(pt);
            XAssert.AreEqual(@".c", e1.ToString(pt.StringTable));

            // get a multi char extension
            e1 = AbsolutePath.Create(pt, @"c:\a.cpp").GetExtension(pt);
            XAssert.AreEqual(@".cpp", e1.ToString(pt.StringTable));

            // get nothing
            e1 = AbsolutePath.Create(pt, @"c:\a").GetExtension(pt);
            XAssert.IsFalse(e1.IsValid);

            // get a single char extension
            e1 = AbsolutePath.Create(pt, @"c:\a.c.d").GetExtension(pt);
            XAssert.AreEqual(@".d", e1.ToString(pt.StringTable));

            // get a multi char extension
            e1 = AbsolutePath.Create(pt, @"c:\.cpp").GetExtension(pt);
            XAssert.AreEqual(@".cpp", e1.ToString(pt.StringTable));
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void GetName()
        {
            var pt = new PathTable();
            AbsolutePath da = AbsolutePath.Create(pt, @"c:\a");
            PathAtom atom = da.GetName(pt);
            XAssert.AreEqual(@"a", atom.ToString(pt.StringTable));
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void GetParent()
        {
            var pt = new PathTable();
            AbsolutePath da = AbsolutePath.Create(pt, @"c:\a");
            AbsolutePath parent = da.GetParent(pt);
            XAssert.AreEqual(@"c:\", parent.ToString(pt));

            da = AbsolutePath.Create(pt, @"c:\a\b");
            parent = da.GetParent(pt);
            XAssert.AreEqual(@"c:\a", parent.ToString(pt));
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void GetRoot()
        {
            var pt = new PathTable();
            AbsolutePath da = AbsolutePath.Create(pt, @"c:\a");
            AbsolutePath root = da.GetRoot(pt);
            XAssert.AreEqual(@"c:\", root.ToString(pt));

            da = AbsolutePath.Create(pt, @"c:\");
            root = da.GetRoot(pt);
            XAssert.AreEqual(@"c:\", root.ToString(pt));

            da = AbsolutePath.Create(pt, @"c:\a\b");
            root = da.GetRoot(pt);
            XAssert.AreEqual(@"c:\", root.ToString(pt));

            da = AbsolutePath.Create(pt, @"\\server\foo");
            root = da.GetRoot(pt);
            XAssert.AreEqual(@"\\server", root.ToString(pt));
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
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

            XAssert.IsFalse(AbsolutePath.IsValidAbsolutePathChar('?'));
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void RelocateForm1()
        {
            // replace the file extension
            var pt = new PathTable();
            {
                AbsolutePath d1 = AbsolutePath.Create(pt, @"c:\a\b");
                AbsolutePath d2 = AbsolutePath.Create(pt, @"c:\a\x");
                AbsolutePath f1 = AbsolutePath.Create(pt, @"C:\a\b\c\d.cpp");
                PathAtom ext = PathAtom.Create(pt.StringTable, ".obj");
                XAssert.IsTrue(f1.IsWithin(pt, d1));
                XAssert.IsFalse(f1.IsWithin(pt, d2));
                AbsolutePath f2 = f1.Relocate(pt, d1, d2, ext);
                XAssert.AreEqual(@"c:\a\x\c\d.obj", f2.ToString(pt));
            }

            // strip the extension instead of replacing it
            pt = new PathTable();
            {
                AbsolutePath d1 = AbsolutePath.Create(pt, @"c:\a\b");
                AbsolutePath d2 = AbsolutePath.Create(pt, @"c:\a\x");
                AbsolutePath f1 = AbsolutePath.Create(pt, @"C:\a\b\c\d.cpp");
                XAssert.IsTrue(f1.IsWithin(pt, d1));
                XAssert.IsFalse(f1.IsWithin(pt, d2));
                AbsolutePath f2 = f1.Relocate(pt, d1, d2, PathAtom.Invalid);
                XAssert.AreEqual(@"c:\a\x\c\d", f2.ToString(pt));
            }

            // leave the extension alone
            pt = new PathTable();
            {
                AbsolutePath d1 = AbsolutePath.Create(pt, @"c:\a\b");
                AbsolutePath d2 = AbsolutePath.Create(pt, @"c:\a\x");
                AbsolutePath f1 = AbsolutePath.Create(pt, @"C:\a\b\c\d.cpp");
                XAssert.IsTrue(f1.IsWithin(pt, d1));
                XAssert.IsFalse(f1.IsWithin(pt, d2));
                AbsolutePath f2 = f1.Relocate(pt, d1, d2);
                XAssert.AreEqual(@"c:\a\x\c\d.cpp", f2.ToString(pt));
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void RelocateForm3Subst()
        {
            // leave the extension alone
            var pt = new PathTable();
            {
                AbsolutePath f1 = AbsolutePath.Create(pt, @"B:\a\x\foo.cs");
                AbsolutePath src = AbsolutePath.Create(pt, @"B:\");
                AbsolutePath dest = AbsolutePath.Create(pt, @"d:\repos\BuildXL\");
                AbsolutePath f2 = f1.Relocate(pt, src, dest);
                XAssert.AreEqual(@"d:\repos\BuildXL\a\x\foo.cs", f2.ToString(pt));
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void RelocateForm2()
        {
            // replace the file extension
            var pt = new PathTable();
            {
                AbsolutePath d2 = AbsolutePath.Create(pt, @"c:\a\x");
                AbsolutePath f1 = AbsolutePath.Create(pt, @"C:\a\b\c\d.cpp");
                PathAtom ext = PathAtom.Create(pt.StringTable, ".obj");
                XAssert.IsFalse(f1.IsWithin(pt, d2));
                AbsolutePath f2 = f1.Relocate(pt, d2, ext);
                XAssert.AreEqual(@"c:\a\x\d.obj", f2.ToString(pt));
            }

            // strip the extension instead of replacing it
            pt = new PathTable();
            {
                AbsolutePath d2 = AbsolutePath.Create(pt, @"c:\a\x");
                AbsolutePath f1 = AbsolutePath.Create(pt, @"C:\a\b\c\d.cpp");
                XAssert.IsFalse(f1.IsWithin(pt, d2));
                AbsolutePath f2 = f1.Relocate(pt, d2, PathAtom.Invalid);
                XAssert.AreEqual(@"c:\a\x\d", f2.ToString(pt));
            }

            // leave the extension alone
            pt = new PathTable();
            {
                AbsolutePath d2 = AbsolutePath.Create(pt, @"c:\a\x");
                AbsolutePath f1 = AbsolutePath.Create(pt, @"C:\a\b\c\d.cpp");
                XAssert.IsFalse(f1.IsWithin(pt, d2));
                AbsolutePath f2 = f1.Relocate(pt, d2);
                XAssert.AreEqual(@"c:\a\x\d.cpp", f2.ToString(pt));
            }
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void ToStringTests()
        {
            var backSlashedPath = @"c:\temp\test\test1.txt";
            var fwdSlashedPath = @"c:/temp/test/test1.txt";

            var pt = new PathTable();
            var p = AbsolutePath.Create(pt, backSlashedPath);

            // For xplat we should #if and have two tests.
            XAssert.AreEqual(backSlashedPath, p.ToString(pt, PathFormat.HostOs));
            XAssert.AreEqual(backSlashedPath, p.ToString(pt, PathFormat.Windows));
            XAssert.AreEqual(fwdSlashedPath, p.ToString(pt, PathFormat.Unix));
            XAssert.AreEqual(fwdSlashedPath, p.ToString(pt, PathFormat.Script));
        }
    }
}
