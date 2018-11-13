// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class FullSymbolTests : XunitBuildXLTest
    {
        public FullSymbolTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void IsInitialized()
        {
            FullSymbol p = default(FullSymbol);
            XAssert.IsFalse(p.IsValid);

            var idt = new SymbolTable();
            p = FullSymbol.Create(idt, @"C.AAA.CCC");
            XAssert.AreEqual(@"C.AAA.CCC", p.ToString(idt));
            XAssert.IsTrue(p.IsValid);
        }

        [Fact]
        public void TryCreate()
        {
            var idt = new SymbolTable();
            FullSymbol p;
            int characterWithError;
            XAssert.AreEqual(FullSymbol.ParseResult.Success, FullSymbol.TryCreate(idt, @"C.AAA.CCC", out p, out characterWithError));
            XAssert.AreEqual(@"C.AAA.CCC", p.ToString(idt));

            XAssert.AreEqual(FullSymbol.ParseResult.FailureDueToInvalidCharacter, FullSymbol.TryCreate(idt, @"C.::AAA", out p, out characterWithError));
            XAssert.AreEqual(2, characterWithError);
            XAssert.AreEqual(FullSymbol.ParseResult.FailureDueToInvalidCharacter, FullSymbol.TryCreate(idt, @"AAA:", out p, out characterWithError));
            XAssert.AreEqual(3, characterWithError);
            XAssert.AreEqual(FullSymbol.ParseResult.FailureDueToInvalidCharacter, FullSymbol.TryCreate(idt, @":AAA", out p, out characterWithError));
            XAssert.AreEqual(0, characterWithError);
            XAssert.AreEqual(FullSymbol.ParseResult.LeadingOrTrailingDot, FullSymbol.TryCreate(idt, @"C...", out p, out characterWithError));
            XAssert.AreEqual(2, characterWithError);
            XAssert.AreEqual(FullSymbol.ParseResult.LeadingOrTrailingDot, FullSymbol.TryCreate(idt, @"C......", out p, out characterWithError));
            XAssert.AreEqual(2, characterWithError);
            XAssert.AreEqual(FullSymbol.ParseResult.LeadingOrTrailingDot, FullSymbol.TryCreate(idt, @"..", out p, out characterWithError));
            XAssert.AreEqual(0, characterWithError);
            XAssert.AreEqual(FullSymbol.ParseResult.LeadingOrTrailingDot, FullSymbol.TryCreate(idt, @".", out p, out characterWithError));
            XAssert.AreEqual(0, characterWithError);
            XAssert.AreEqual(FullSymbol.ParseResult.FailureDueToInvalidCharacter, FullSymbol.TryCreate(idt, @"C.:", out p, out characterWithError));
            XAssert.AreEqual(2, characterWithError);
            XAssert.AreEqual(FullSymbol.ParseResult.FailureDueToInvalidCharacter, FullSymbol.TryCreate(idt, @"1..", out p, out characterWithError));
            XAssert.AreEqual(1, characterWithError);
            XAssert.AreEqual(FullSymbol.ParseResult.FailureDueToInvalidCharacter, FullSymbol.TryCreate(idt, string.Empty, out p, out characterWithError));
            XAssert.AreEqual(0, characterWithError);

            p = FullSymbol.Create(idt, @"C");
            XAssert.AreEqual(@"C", p.ToString(idt));

            p = FullSymbol.Create(idt, @"C.BBB");
            XAssert.AreEqual(@"C.BBB", p.ToString(idt));
        }

        [Fact]
        public void Equality()
        {
            var idt = new SymbolTable();
            FullSymbol a1 = FullSymbol.Create(idt, @"c.AAA.CCC");
            FullSymbol a2 = FullSymbol.Create(idt, @"c.AAA.CCC");
            FullSymbol a3 = FullSymbol.Create(idt, @"c.BBB.CCC");

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
        }

        [Fact]
        public void Combine()
        {
            var idt = new SymbolTable();
            FullSymbol a1 = FullSymbol.Create(idt, @"C");
            SymbolAtom p1 = SymbolAtom.Create(idt.StringTable, "A");
            FullSymbol a2 = a1.Combine(idt, p1);
            XAssert.AreEqual(@"C.A", a2.ToString(idt));

            a1 = FullSymbol.Create(idt, @"C.X");
            p1 = SymbolAtom.Create(idt.StringTable, "A");
            a2 = a1.Combine(idt, p1);
            XAssert.AreEqual(@"C.X.A", a2.ToString(idt));

            a1 = FullSymbol.Create(idt, @"C.X");
            p1 = SymbolAtom.Create(idt.StringTable, "A");
            SymbolAtom p2 = SymbolAtom.Create(idt.StringTable, "B");
            a2 = a1.Combine(idt, p1, p2);
            XAssert.AreEqual(@"C.X.A.B", a2.ToString(idt));

            a1 = FullSymbol.Create(idt, @"C.X");
            p1 = SymbolAtom.Create(idt.StringTable, "A");
            p2 = SymbolAtom.Create(idt.StringTable, "B");
            SymbolAtom p3 = SymbolAtom.Create(idt.StringTable, "C");
            a2 = a1.Combine(idt, p1, p2, p3);
            XAssert.AreEqual(@"C.X.A.B.C", a2.ToString(idt));

            a1 = FullSymbol.Create(idt, @"C");
            PartialSymbol rp = PartialSymbol.Create(idt.StringTable, @"A.B");
            a2 = a1.Combine(idt, rp);
            XAssert.AreEqual(@"C.A.B", a2.ToString(idt));

            a1 = FullSymbol.Create(idt, @"C.X");
            rp = PartialSymbol.Create(idt.StringTable, @"A.B");
            a2 = a1.Combine(idt, rp);
            XAssert.AreEqual(@"C.X.A.B", a2.ToString(idt));
        }

        [Fact]
        public void Concat()
        {
            var idt = new SymbolTable();
            FullSymbol a1 = FullSymbol.Create(idt, @"C.A");
            SymbolAtom p1 = SymbolAtom.Create(idt.StringTable, "B");
            FullSymbol a2 = a1.Concat(idt, p1);
            XAssert.AreEqual(@"C.AB", a2.ToString(idt));
        }

        [Fact]
        public void GetName()
        {
            var idt = new SymbolTable();
            FullSymbol da = FullSymbol.Create(idt, @"c.a");
            SymbolAtom atom = da.GetName(idt);
            XAssert.AreEqual(@"a", atom.ToString(idt.StringTable));
        }

        [Fact]
        public void GetParent()
        {
            var idt = new SymbolTable();
            FullSymbol da = FullSymbol.Create(idt, @"c.a");
            FullSymbol parent = da.GetParent(idt);
            XAssert.AreEqual(@"c", parent.ToString(idt));

            da = FullSymbol.Create(idt, @"c.a.b");
            parent = da.GetParent(idt);
            XAssert.AreEqual(@"c.a", parent.ToString(idt));
        }

        [Fact]
        public void IsValidIdChar()
        {
            XAssert.IsTrue(FullSymbol.IsValidAbsoluteIdChar('a'));
            XAssert.IsTrue(FullSymbol.IsValidAbsoluteIdChar('z'));
            XAssert.IsTrue(FullSymbol.IsValidAbsoluteIdChar('A'));
            XAssert.IsTrue(FullSymbol.IsValidAbsoluteIdChar('Z'));
            XAssert.IsTrue(FullSymbol.IsValidAbsoluteIdChar('0'));
            XAssert.IsTrue(FullSymbol.IsValidAbsoluteIdChar('9'));

            XAssert.IsFalse(FullSymbol.IsValidAbsoluteIdChar('\\'));
            XAssert.IsFalse(FullSymbol.IsValidAbsoluteIdChar('/'));
            XAssert.IsFalse(FullSymbol.IsValidAbsoluteIdChar(':'));

            XAssert.IsFalse(FullSymbol.IsValidAbsoluteIdChar('.'));
            XAssert.IsFalse(FullSymbol.IsValidAbsoluteIdChar('?'));
        }

        [Fact]
        public void RelocateForm1()
        {
            var idt = new SymbolTable();
            FullSymbol d1 = FullSymbol.Create(idt, @"c.a.b");
            FullSymbol d2 = FullSymbol.Create(idt, @"c.a.x");
            FullSymbol f1 = FullSymbol.Create(idt, @"c.a.b.c.d.cpp");
            XAssert.IsTrue(f1.IsWithin(idt, d1));
            XAssert.IsFalse(f1.IsWithin(idt, d2));
            FullSymbol f2 = f1.Relocate(idt, d1, d2);
            XAssert.AreEqual(@"c.a.x.c.d.cpp", f2.ToString(idt));
        }

        [Fact]
        public void RelocateForm2()
        {
            var idt = new SymbolTable();
            FullSymbol d2 = FullSymbol.Create(idt, @"c.a.x");
            FullSymbol f1 = FullSymbol.Create(idt, @"C.a.b.c.d.cpp");
            XAssert.IsFalse(f1.IsWithin(idt, d2));
            FullSymbol f2 = f1.Relocate(idt, d2);
            XAssert.AreEqual(@"c.a.x.cpp", f2.ToString(idt));
        }
    }
}
