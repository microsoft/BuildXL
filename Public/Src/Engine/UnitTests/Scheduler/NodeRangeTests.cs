// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Scheduler.Graph;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    public class NodeRangeTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public NodeRangeTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void Equality()
        {
            Test.BuildXL.TestUtilities.Xunit.StructTester.TestEquality(
                baseValue: new NodeRange(new NodeId(123), new NodeId(456)),
                equalValue: new NodeRange(new NodeId(123), new NodeId(456)),
                notEqualValues: new[]
                                {
                                    new NodeRange(new NodeId(124), new NodeId(456)),
                                    new NodeRange(new NodeId(123), new NodeId(457)),
                                    NodeRange.Empty,
                                },
                eq: (a, b) => a == b,
                neq: (a, b) => a != b);
        }

        [Fact]
        public void Empty()
        {
            NodeRange range = NodeRange.Empty;
            XAssert.IsFalse(range.Contains(NodeId.Min));
            XAssert.IsFalse(range.Contains(new NodeId(123)));
            XAssert.IsFalse(range.Contains(NodeId.Max));

            XAssert.IsTrue(range.IsEmpty);
            XAssert.AreEqual(0, range.Size);
        }

        [Fact]
        public void Complete()
        {
            var range = new NodeRange(NodeId.Min, NodeId.Max);
            XAssert.IsTrue(range.Contains(NodeId.Min));
            XAssert.IsTrue(range.Contains(new NodeId(123)));
            XAssert.IsTrue(range.Contains(NodeId.Max));

            XAssert.IsFalse(range.IsEmpty);
            XAssert.AreEqual<uint>(NodeId.MaxValue - NodeId.MinValue + 1, (uint)range.Size);
        }

        [Fact]
        public void Singular()
        {
            var range = new NodeRange(new NodeId(123), new NodeId(123));
            XAssert.IsFalse(range.Contains(NodeId.Min));
            XAssert.IsFalse(range.Contains(new NodeId(122)));
            XAssert.IsTrue(range.Contains(new NodeId(123)));
            XAssert.IsFalse(range.Contains(new NodeId(124)));
            XAssert.IsFalse(range.Contains(NodeId.Max));

            XAssert.IsFalse(range.IsEmpty);
            XAssert.AreEqual<uint>(1, (uint)range.Size);
        }

        [Fact]
        public void LowerBound()
        {
            NodeRange range = NodeRange.CreateLowerBound(new NodeId(123));
            XAssert.IsFalse(range.Contains(NodeId.Min));
            XAssert.IsFalse(range.Contains(new NodeId(122)));
            XAssert.IsTrue(range.Contains(new NodeId(123)));
            XAssert.IsTrue(range.Contains(new NodeId(124)));
            XAssert.IsTrue(range.Contains(NodeId.Max));

            XAssert.IsFalse(range.IsEmpty);
            XAssert.AreEqual<uint>(NodeId.MaxValue - 123 + 1, (uint)range.Size);
        }

        [Fact]
        public void UpperBound()
        {
            NodeRange range = NodeRange.CreateUpperBound(new NodeId(123));
            XAssert.IsTrue(range.Contains(NodeId.Min));
            XAssert.IsTrue(range.Contains(new NodeId(122)));
            XAssert.IsTrue(range.Contains(new NodeId(123)));
            XAssert.IsFalse(range.Contains(new NodeId(124)));
            XAssert.IsFalse(range.Contains(NodeId.Max));

            XAssert.IsFalse(range.IsEmpty);
            XAssert.AreEqual<uint>(123 - NodeId.MinValue + 1, (uint)range.Size);
        }

        [Fact]
        public void CreatePossiblyEmpty()
        {
            XAssert.AreEqual(NodeRange.Empty, NodeRange.CreatePossiblyEmpty(new NodeId(456), new NodeId(123)));
            XAssert.AreEqual(new NodeRange(new NodeId(123), new NodeId(456)), NodeRange.CreatePossiblyEmpty(new NodeId(123), new NodeId(456)));
        }
    }
}
