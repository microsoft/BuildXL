// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Scheduler.Graph;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    public class RangedNodeSetTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public RangedNodeSetTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void Empty()
        {
            var rangedSet = new RangedNodeSet();

            XAssert.IsTrue(rangedSet.Range.IsEmpty);

            XAssert.IsFalse(rangedSet.Contains(NodeId.Min));
            XAssert.IsFalse(rangedSet.Contains(new NodeId(123)));
            XAssert.IsFalse(rangedSet.Contains(NodeId.Max));

            ExpectContentsFromEnumeration(rangedSet);
        }

        [Fact]
        public void Singular()
        {
            var rangedSet = new RangedNodeSet();
            rangedSet.SetSingular(new NodeId(123));

            XAssert.IsFalse(rangedSet.Range.IsEmpty);

            XAssert.IsFalse(rangedSet.Contains(NodeId.Min));
            XAssert.IsFalse(rangedSet.Contains(new NodeId(122)));
            XAssert.IsTrue(rangedSet.Contains(new NodeId(123)));
            XAssert.IsFalse(rangedSet.Contains(new NodeId(124)));
            XAssert.IsFalse(rangedSet.Contains(NodeId.Max));

            ExpectContentsFromEnumeration(rangedSet, new NodeId(123));
        }

        [Fact]
        public void NonemptyRangeButEmptySet()
        {
            var rangedSet = new RangedNodeSet();
            rangedSet.ClearAndSetRange(new NodeRange(new NodeId(123), new NodeId(223)));

            XAssert.IsFalse(rangedSet.Range.IsEmpty);
            XAssert.AreEqual(new NodeRange(new NodeId(123), new NodeId(223)), rangedSet.Range);

            XAssert.IsFalse(rangedSet.Contains(NodeId.Min));
            XAssert.IsFalse(rangedSet.Contains(new NodeId(122)));
            XAssert.IsFalse(rangedSet.Contains(new NodeId(123)));
            XAssert.IsFalse(rangedSet.Contains(new NodeId(124)));
            XAssert.IsFalse(rangedSet.Contains(new NodeId(223)));
            XAssert.IsFalse(rangedSet.Contains(NodeId.Max));

            ExpectContentsFromEnumeration(rangedSet);
        }

        [Fact]
        public void AddEndpoints()
        {
            var rangedSet = new RangedNodeSet();
            rangedSet.ClearAndSetRange(new NodeRange(new NodeId(123), new NodeId(223)));
            rangedSet.Add(new NodeId(123));
            rangedSet.Add(new NodeId(223));

            XAssert.IsFalse(rangedSet.Range.IsEmpty);
            XAssert.AreEqual(new NodeRange(new NodeId(123), new NodeId(223)), rangedSet.Range);

            XAssert.IsFalse(rangedSet.Contains(NodeId.Min));
            XAssert.IsFalse(rangedSet.Contains(new NodeId(122)));
            XAssert.IsTrue(rangedSet.Contains(new NodeId(123)));
            XAssert.IsFalse(rangedSet.Contains(new NodeId(124)));
            XAssert.IsTrue(rangedSet.Contains(new NodeId(223)));
            XAssert.IsFalse(rangedSet.Contains(new NodeId(224)));
            XAssert.IsFalse(rangedSet.Contains(NodeId.Max));

            ExpectContentsFromEnumeration(rangedSet, new NodeId(123), new NodeId(223));
        }

        [Fact]
        public void AddInterior()
        {
            var rangedSet = new RangedNodeSet();
            rangedSet.ClearAndSetRange(new NodeRange(new NodeId(123), new NodeId(223)));
            rangedSet.Add(new NodeId(200));
            rangedSet.Add(new NodeId(201));

            XAssert.IsFalse(rangedSet.Range.IsEmpty);
            XAssert.AreEqual(new NodeRange(new NodeId(123), new NodeId(223)), rangedSet.Range);

            XAssert.IsFalse(rangedSet.Contains(NodeId.Min));
            XAssert.IsFalse(rangedSet.Contains(new NodeId(122)));
            XAssert.IsFalse(rangedSet.Contains(new NodeId(123)));
            XAssert.IsFalse(rangedSet.Contains(new NodeId(124)));

            XAssert.IsTrue(rangedSet.Contains(new NodeId(200)));
            XAssert.IsTrue(rangedSet.Contains(new NodeId(201)));

            XAssert.IsFalse(rangedSet.Contains(new NodeId(223)));
            XAssert.IsFalse(rangedSet.Contains(new NodeId(224)));
            XAssert.IsFalse(rangedSet.Contains(NodeId.Max));

            ExpectContentsFromEnumeration(rangedSet, new NodeId(200), new NodeId(201));
        }

        [Fact]
        public void AddAndClear()
        {
            var rangedSet = new RangedNodeSet();
            rangedSet.ClearAndSetRange(new NodeRange(new NodeId(123), new NodeId(223)));
            rangedSet.Add(new NodeId(200));
            rangedSet.ClearAndSetRange(new NodeRange(new NodeId(123), new NodeId(223)));
            rangedSet.Add(new NodeId(201));

            XAssert.IsFalse(rangedSet.Range.IsEmpty);
            XAssert.AreEqual(new NodeRange(new NodeId(123), new NodeId(223)), rangedSet.Range);

            XAssert.IsFalse(rangedSet.Contains(NodeId.Min));
            XAssert.IsFalse(rangedSet.Contains(new NodeId(122)));
            XAssert.IsFalse(rangedSet.Contains(new NodeId(123)));
            XAssert.IsFalse(rangedSet.Contains(new NodeId(124)));

            XAssert.IsFalse(rangedSet.Contains(new NodeId(200)));
            XAssert.IsTrue(rangedSet.Contains(new NodeId(201)));

            XAssert.IsFalse(rangedSet.Contains(new NodeId(223)));
            XAssert.IsFalse(rangedSet.Contains(new NodeId(224)));
            XAssert.IsFalse(rangedSet.Contains(NodeId.Max));

            ExpectContentsFromEnumeration(rangedSet, new NodeId(201));
        }

        private void ExpectContentsFromEnumeration(RangedNodeSet set, params NodeId[] expected)
        {
            var expectedSet = new HashSet<NodeId>(expected);

            foreach (NodeId expectedNode in expectedSet)
            {
                XAssert.IsTrue(set.Contains(expectedNode), "Contains() didn't find an expected node");
            }

            foreach (NodeId actualNode in set)
            {
                bool found = expectedSet.Remove(actualNode);
                XAssert.IsTrue(found, "Enumeration returned an unexpected node");
            }

            XAssert.AreEqual(0, expectedSet.Count, "Enumeration didn't return one or more expected nodes (expectation set nonempty)");
        }
    }
}
