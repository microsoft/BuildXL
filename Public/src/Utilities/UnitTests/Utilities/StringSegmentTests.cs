// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class StringSegmentTests : XunitBuildXLTest
    {
        public StringSegmentTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void General()
        {
            var seg = new StringSegment(string.Empty);
            XAssert.AreEqual(0, seg.Length);
            XAssert.IsTrue(seg.IndexOf("AB") < 0);

            seg = new StringSegment("A");
            XAssert.AreEqual(1, seg.Length);
            XAssert.AreEqual('A', seg[0]);
            XAssert.IsTrue(seg.IndexOf("AB") < 0);

            var stable = new StringSegment("AB");

            seg = new StringSegment("AB");
            XAssert.AreEqual(2, seg.Length);
            XAssert.AreEqual('A', seg[0]);
            XAssert.AreEqual('B', seg[1]);
            XAssert.AreEqual(stable, seg);
            XAssert.IsTrue(seg.IndexOf("AB") == 0);

            seg = new StringSegment("XABY", 1, 2);
            XAssert.AreEqual(2, seg.Length);
            XAssert.AreEqual('A', seg[0]);
            XAssert.AreEqual('B', seg[1]);
            XAssert.AreEqual(stable, seg);
            XAssert.IsTrue(seg.IndexOf("AB") == 0);

            seg = new StringSegment("ABY", 0, 2);
            XAssert.AreEqual(2, seg.Length);
            XAssert.AreEqual('A', seg[0]);
            XAssert.AreEqual('B', seg[1]);
            XAssert.AreEqual(stable, seg);
            XAssert.IsTrue(seg.IndexOf("AB") == 0);

            seg = new StringSegment("XAB", 1, 2);
            XAssert.AreEqual(2, seg.Length);
            XAssert.AreEqual('A', seg[0]);
            XAssert.AreEqual('B', seg[1]);
            XAssert.AreEqual(stable, seg);
            XAssert.IsTrue(seg.IndexOf("AB") == 0);
        }

        [Fact]
        public void DefaultIsNotEmpty()
        {
            StringSegment defaultSegment = default(StringSegment);
            StringSegment emptySegment = new StringSegment(string.Empty);

            XAssert.IsFalse(defaultSegment.Equals(emptySegment));
        }

        [Fact]
        public void Empty()
        {
            var seg = StringSegment.Empty;
            XAssert.AreEqual(0, seg.Length);
            XAssert.IsTrue(seg.IndexOf("AB") < 0);
            XAssert.AreEqual(seg, seg);

            XAssert.AreNotEqual(new StringSegment("ABC"), seg);
        }

        [Fact]
        public void Subsegment()
        {
            var seg = new StringSegment(string.Empty);
            var sub = seg.Subsegment(0, 0);
            XAssert.AreEqual(0, sub.Length);

            seg = new StringSegment("ABCDEF");
            sub = seg.Subsegment(0, 1);
            XAssert.AreEqual(1, sub.Length);
            XAssert.AreEqual('A', sub[0]);

            sub = seg.Subsegment(5, 1);
            XAssert.AreEqual(1, sub.Length);
            XAssert.AreEqual('F', sub[0]);

            sub = seg.Subsegment(2, 3);
            XAssert.AreEqual(3, sub.Length);
            XAssert.AreEqual('C', sub[0]);
            XAssert.AreEqual('D', sub[1]);
            XAssert.AreEqual('E', sub[2]);
        }

        [Fact]
        public void IsEqual()
        {
            var s1 = new StringSegment("ABCDEF");
            var s2 = new StringSegment("ABCDEF", 0, 6);
            XAssert.IsTrue(s1.Equals(s2));
            XAssert.IsTrue(s1 == s2);
            XAssert.IsFalse(s1 != s2);

            s2 = new StringSegment("XABCDEF", 1, 6);
            XAssert.IsTrue(s1.Equals(s2));
            XAssert.IsTrue(s1 == s2);
            XAssert.IsFalse(s1 != s2);

            s2 = new StringSegment("ABCDEF", 0, 5);
            XAssert.IsFalse(s1.Equals(s2));
            XAssert.IsFalse(s1 == s2);
            XAssert.IsTrue(s1 != s2);

            s2 = new StringSegment("GHIJKL", 0, 6);
            XAssert.IsFalse(s1.Equals(s2));
            XAssert.IsFalse(s1 == s2);
            XAssert.IsTrue(s1 != s2);
        }
    }
}
