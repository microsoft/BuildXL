// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class PartialSymbolTests : XunitBuildXLTest
    {
        public PartialSymbolTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void IsInitialized()
        {
            PartialSymbol p = default(PartialSymbol);
            XAssert.IsFalse(p.IsValid);

            var st = new StringTable(0);
            p = PartialSymbol.Create(st, @"AAA.CCC");
            XAssert.AreEqual(@"AAA.CCC", p.ToString(st));
            XAssert.IsTrue(p.IsValid);
            XAssert.IsFalse(p.IsEmpty);

            p = PartialSymbol.Create(st, string.Empty);
            XAssert.IsTrue(p.IsValid);
            XAssert.IsTrue(p.IsEmpty);
        }

        [Fact]
        public void TryCreate()
        {
            var st = new StringTable(0);

            PartialSymbol p;
            XAssert.IsTrue(PartialSymbol.TryCreate(st, @"AAA.CCC", out p));
            XAssert.AreEqual(@"AAA.CCC", p.ToString(st));

            XAssert.IsFalse(PartialSymbol.TryCreate(st, @"C.:AAA", out p));
            XAssert.IsFalse(PartialSymbol.TryCreate(st, @"AAA:", out p));
            XAssert.IsFalse(PartialSymbol.TryCreate(st, @":AAA", out p));
            XAssert.IsFalse(PartialSymbol.TryCreate(st, @"..", out p));
            XAssert.IsFalse(PartialSymbol.TryCreate(st, @".", out p));
            XAssert.IsFalse(PartialSymbol.TryCreate(st, @"B.", out p));
            XAssert.IsFalse(PartialSymbol.TryCreate(st, @"B..", out p));

            p = PartialSymbol.Create(st, string.Empty);
            XAssert.AreEqual(string.Empty, p.ToString(st));

            p = PartialSymbol.Create(st, "BBB");
            XAssert.AreEqual("BBB", p.ToString(st));

            SymbolAtom a1 = SymbolAtom.Create(st, "AAA");
            SymbolAtom a2 = SymbolAtom.Create(st, "BBB");
            SymbolAtom a3 = SymbolAtom.Create(st, "CCC");
            p = PartialSymbol.Create(a1, a2, a3);
            XAssert.AreEqual(@"AAA.BBB.CCC", p.ToString(st));
        }

        [Fact]
        public void IsValidIdChar()
        {
            XAssert.IsTrue(PartialSymbol.IsValidRelativeIdChar('a'));
            XAssert.IsTrue(PartialSymbol.IsValidRelativeIdChar('z'));
            XAssert.IsTrue(PartialSymbol.IsValidRelativeIdChar('A'));
            XAssert.IsTrue(PartialSymbol.IsValidRelativeIdChar('Z'));
            XAssert.IsTrue(PartialSymbol.IsValidRelativeIdChar('0'));
            XAssert.IsTrue(PartialSymbol.IsValidRelativeIdChar('9'));

            XAssert.IsFalse(PartialSymbol.IsValidRelativeIdChar('.'));
            XAssert.IsFalse(PartialSymbol.IsValidRelativeIdChar('\\'));
            XAssert.IsFalse(PartialSymbol.IsValidRelativeIdChar('/'));
            XAssert.IsFalse(PartialSymbol.IsValidRelativeIdChar(':'));
            XAssert.IsFalse(PartialSymbol.IsValidRelativeIdChar('?'));
        }

        [Fact]
        public void Equality()
        {
            var st = new StringTable(0);
            PartialSymbol a1 = PartialSymbol.Create(st, @"AAA.CCC");
            PartialSymbol a2 = PartialSymbol.Create(st, @"AAA.CCC");
            PartialSymbol a3 = PartialSymbol.Create(st, @"BBB.CCC");

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

            a1 = PartialSymbol.Create(st, string.Empty);
            XAssert.AreEqual(0, a1.GetHashCode());

            XAssert.IsFalse(a1.Equals(a2));
        }

        [Fact]
        public void Conversion()
        {
            var st = new StringTable(0);
            SymbolAtom a1 = SymbolAtom.Create(st, "AAA");
            PartialSymbol p1 = PartialSymbol.Create(a1);
            XAssert.AreEqual("AAA", p1.ToString(st));
        }

        [Fact]
        public void Combine()
        {
            var st = new StringTable(0);
            PartialSymbol p1 = PartialSymbol.Create(st, "AAA");
            SymbolAtom a1 = SymbolAtom.Create(st, "BBB");
            PartialSymbol p2 = p1.Combine(a1);
            XAssert.AreEqual(@"AAA.BBB", p2.ToString(st));

            p1 = PartialSymbol.Create(st, string.Empty);
            p2 = p1.Combine(a1);
            XAssert.AreEqual(@"BBB", p2.ToString(st));

            p1 = PartialSymbol.Create(st, "AAA");
            SymbolAtom a2 = SymbolAtom.Create(st, "CCC");
            p2 = p1.Combine(a1, a2);
            XAssert.AreEqual(@"AAA.BBB.CCC", p2.ToString(st));

            p1 = PartialSymbol.Create(st, "AAA");
            a2 = SymbolAtom.Create(st, "CCC");
            SymbolAtom a3 = SymbolAtom.Create(st, "DDD");
            p2 = p1.Combine(a1, a2, a3);
            XAssert.AreEqual(@"AAA.BBB.CCC.DDD", p2.ToString(st));

            PartialSymbol p3 = p1.Combine(p2);
            XAssert.AreEqual(@"AAA.AAA.BBB.CCC.DDD", p3.ToString(st));
        }

        [Fact]
        public void ToStringTest()
        {
            var st = new StringTable(0);

            Assert.Throws<NotImplementedException>(() =>
            {
                PartialSymbol rp = PartialSymbol.Create(st, @"AAA");
#pragma warning disable 618
                rp.ToString();
#pragma warning restore 618
            });
        }

        [Fact]
        public void GetName()
        {
            var st = new StringTable(0);

            PartialSymbol rp = PartialSymbol.Create(st, @"AAA");
            SymbolAtom atom = rp.GetName();
            XAssert.AreEqual(@"AAA", atom.ToString(st));

            rp = PartialSymbol.Create(st, @"AAA.BBB");
            atom = rp.GetName();
            XAssert.AreEqual(@"BBB", atom.ToString(st));
        }

        [Fact]
        public void GetParent()
        {
            var st = new StringTable(0);

            PartialSymbol rp = PartialSymbol.Create(st, @"AAA");
            PartialSymbol parent = rp.GetParent();
            XAssert.AreEqual(string.Empty, parent.ToString(st));

            rp = PartialSymbol.Create(st, @"AAA.BBB");
            parent = rp.GetParent();
            XAssert.AreEqual(@"AAA", parent.ToString(st));

            rp = PartialSymbol.Create(st, @"AAA.BBB.CCC");
            parent = rp.GetParent();
            XAssert.AreEqual(@"AAA.BBB", parent.ToString(st));
        }

        [Fact]
        public void Concat()
        {
            var st = new StringTable(0);

            PartialSymbol rp = PartialSymbol.Create(st, @"AAA.BBB");
            SymbolAtom p1 = SymbolAtom.Create(st, "XXX");
            PartialSymbol rp2 = rp.Concat(st, p1);
            XAssert.AreEqual(@"AAA.BBBXXX", rp2.ToString(st));
        }
    }
}
