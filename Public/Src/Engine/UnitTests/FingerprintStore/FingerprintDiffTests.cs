// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Scheduler.Tracing.FingerprintDiff;

namespace Test.BuildXL.FingerprintStore
{
    public class FingerprintDiffTests : XunitBuildXLTest
    {
        public FingerprintDiffTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void ExtractObservedInputsDiff()
        {
            var commonData = new[]
            {
                new ObservedInputData(A("X", "Y", "f"), "flagF", "*.cs", "F", "XYf123"),
                new ObservedInputData(A("X", "Y", "g"), "flagG", "*.cc", "G", "XYg123"),
            };

            var oldData = new Dictionary<string, ObservedInputData>(commonData.ToDictionary(k => k.Path))
            {
                [A("X", "Y", "a")] = new ObservedInputData(A("X", "Y", "a"), "flagA", "*.cs", "A", "XYa123"),
                [A("X", "Y", "b")] = new ObservedInputData(A("X", "Y", "b"), "flagB", "*.cb", "B", "XYb123"),
                [A("X", "Y", "c")] = new ObservedInputData(A("X", "Y", "c"), "flagC", "*.cc", "C", "XYc123"),
                [A("X", "Y", "d")] = new ObservedInputData(A("X", "Y", "d"), "flagD", "*.cd", "D", "XYd123"),
            };

            var newData = new Dictionary<string, ObservedInputData>(commonData.ToDictionary(k => k.Path))
            {
                [A("X", "Y", "p")] = new ObservedInputData(A("X", "Y", "p"), "flagP", "*.cp", "P", "XYp123"),
                [A("X", "Y", "q")] = new ObservedInputData(A("X", "Y", "q"), "flagQ", "*.cq", "Q", "XYq123"),
                [A("X", "Y", "c")] = new ObservedInputData(A("X", "Y", "c"), "flagC1", "*.cc", "C", "XYc123"),
                [A("X", "Y", "d")] = new ObservedInputData(A("X", "Y", "d"), "flagD", "*.cd", "D", "XYd124"),
            };

            bool hasDiff = ExtractUnorderedMapDiff(
                oldData,
                newData,
                (o, n) => o.Equals(n),
                out var added,
                out var removed,
                out var changed);

            XAssert.IsTrue(hasDiff);
            XAssert.AreEqual(2, added.Count);
            XAssert.AreEqual(2, removed.Count);
            XAssert.AreEqual(2, changed.Count);

            XAssert.Contains(added, A("X", "Y", "p"), A("X", "Y", "q"));
            XAssert.Contains(removed, A("X", "Y", "a"), A("X", "Y", "b"));
            XAssert.Contains(changed, A("X", "Y", "c"), A("X", "Y", "d"));
        }

        [Fact]
        public void ExtractFilesDiff()
        {
            var oldData = new[] { A("X", "Y", "a"), A("X", "Y", "b"), A("X", "Y", "c"), A("X", "Y", "d") };
            var newData = new[] { A("X", "Y", "a"), A("X", "Y", "b"), A("X", "Y", "f"), A("X", "Y", "g") };

            bool hasDiff = ExtractUnorderedListDiff(oldData, newData, out var added, out var removed);

            XAssert.IsTrue(hasDiff);
            XAssert.AreEqual(2, added.Count);
            XAssert.AreEqual(2, removed.Count);

            XAssert.Contains(added, A("X", "Y", "f"), A("X", "Y", "g"));
            XAssert.Contains(removed, A("X", "Y", "c"), A("X", "Y", "d"));
        }
    }
}
