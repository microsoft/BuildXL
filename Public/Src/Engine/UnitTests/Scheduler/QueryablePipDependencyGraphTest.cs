// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    public class QueryablePipDependencyGraphTest : XunitBuildXLTest
    {
        private BuildXLContext Context { get; }

        public QueryablePipDependencyGraphTest(ITestOutputHelper output) : base(output)
        {
            Context = BuildXLContext.CreateInstanceForTesting();
        }

        private const string GraphWithCycles = @"
p1 -> p2, p3
p2 -> p3, p4
p3 -> p1, p3
p5 -> p6
p7
";

        [Theory]
        [InlineData(GraphWithCycles, "p1", "p1", true)]
        [InlineData(GraphWithCycles, "p1", "p2", true)]
        [InlineData(GraphWithCycles, "p1", "p3", true)]
        [InlineData(GraphWithCycles, "p1", "p4", true)]
        [InlineData(GraphWithCycles, "p1", "p5", false)]
        [InlineData(GraphWithCycles, "p1", "p6", false)]
        [InlineData(GraphWithCycles, "p2", "p1", true)]
        [InlineData(GraphWithCycles, "p2", "p4", true)]
        [InlineData(GraphWithCycles, "p3", "p4", true)]
        [InlineData(GraphWithCycles, "p3", "p5", false)]
        [InlineData(GraphWithCycles, "p3", "p6", false)]
        [InlineData(GraphWithCycles, "p4", "p1", false)]
        [InlineData(GraphWithCycles, "p5", "p1", false)]
        [InlineData(GraphWithCycles, "p5", "p6", true)]
        [InlineData(GraphWithCycles, "p5", "p7", false)]
        [InlineData(GraphWithCycles, "p7", "p1", false)]
        public void IsReachableTest(string graphSpec, string from, string to, bool isReachable)
        {
            var graph = new QueryablePipDependencyGraph(Context);
            var pips = AddPips(graph, GraphWithCycles);
            XAssert.AreEqual(isReachable, graph.IsReachableFrom(from: pips[from].PipId, to: pips[to].PipId));
        }

        private Dictionary<string, global::BuildXL.Pips.Operations.Process> AddPips(QueryablePipDependencyGraph graph, string graphSpec)
        {
            var edges = GraphWithCycles
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(parseLine)
                .ToArray();
            var nodes = edges.SelectMany(edge => new[] { edge.from }.Concat(edge.to)).ToHashSet();
            var pips = nodes
                .Select(n => (name: n, proc: graph.AddProcess(ToAbsolutePath(X($"/x/out/{n}.out")))))
                .ToDictionary(kvp => kvp.name, kvp => kvp.proc);
            foreach (var edge in edges)
            {
                foreach (var to in edge.to)
                {
                    graph.AddDataflowDependency(from: pips[edge.from].PipId, to: pips[to].PipId);
                }
            }

            return pips;

            (string from, string[] to) parseLine(string line)
            {
                var splits = line.Split(new[] { "->" }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
                XAssert.IsTrue(splits.Length <= 2);
                var lhs = splits[0];
                var rhs = splits.Length > 1 ? splits[1] : string.Empty;
                return (from: lhs, to: rhs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray());
            }
        }

        private AbsolutePath ToAbsolutePath(string path) => AbsolutePath.Create(Context.PathTable, path);
    }
}
