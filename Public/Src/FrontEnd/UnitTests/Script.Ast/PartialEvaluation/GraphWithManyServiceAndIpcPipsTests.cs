// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.PartialEvaluation
{
    /// <summary>
    /// For a spec2spec dependency graph that looks like, for example:
    ///
    ///      0
    ///      |
    ///      1 
    ///      |
    ///      2
    /// this test class producess the following pip graph
    ///
    ///     Service0-Ipc0-Finalize0 Shutdown0
    ///               |
    ///     Service1-Ipc1-Finalize1 Shutdown1
    ///               |
    ///     Service2-Ipc2-Finalize2 Shutdown2
    ///     
    /// All IPC-to-IPC pip dependencies are realized via output files.
    /// </summary>
    [Trait("Category", "PartialEvaluation")]
    public sealed class GraphWithManyServiceAndIpcPipsTests : GraphBasedTestBase
    {
        public GraphWithManyServiceAndIpcPipsTests(ITestOutputHelper output) : base(output)
        {
        }

        /// <summary>
        /// Each generated spec looks something like:
        /// namespace NS{i} {
        ///     export const x{i} = runServiceAndIpcPip('p{i}', [ NS{j1}.x{j1}, ..., NS{jn}.x{jn} ]);
        /// }
        /// </summary>
        protected override string GenerateSpec(int specIndex, List<int> specDependencies)
        {
            var commaSeparatedImports = string.Join(", ", specDependencies.Select(specIdx => $"{GetSpecNamespace(specIdx)}.x{specIdx}"));
            return $@"namespace NS{specIndex} {{
    export const x{specIndex} = runServiceAndIpcPip('{GetProcTag(specIndex)}', [ {commaSeparatedImports} ]);
}}";
        }

        protected override void AssertPipGraphContent(PipGraph pipGraph, SimpleGraph file2file, StringTable stringTable)
        {
            AssertPipGraphCounts(pipGraph, new Dictionary<PipType, int>
            {
                [PipType.Process] = file2file.NodeCount * 3, // service pip, service shutdown pip, and service finalize pip in each file
                [PipType.Ipc] = file2file.NodeCount, // one IPC pip per file
            });
            var ipcPips = pipGraph.RetrievePipsOfType(PipType.Ipc).ToList();
            AssertEdges(pipGraph, file2file, ipcPips, stringTable);
            AssertMonikerConsistencyForIpcPip(ipcPips.Cast<IpcPip>(), pipGraph);
        }
    }
}
