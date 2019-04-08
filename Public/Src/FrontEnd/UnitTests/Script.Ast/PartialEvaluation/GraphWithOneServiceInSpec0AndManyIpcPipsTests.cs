// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities;
using Xunit;
using Xunit.Abstractions;
using static Test.BuildXL.TestUtilities.SimpleGraph;

namespace Test.DScript.Ast.PartialEvaluation
{
    /// <summary>
    /// For a spec2spec dependency graph that looks like, for example:
    ///
    ///      0
    ///      |
    ///      1    5
    ///    / | \ /
    ///   2  3  4 
    ///   
    /// this test class producess the following pip graph (where 'S' stands for service pip, 
    /// 'I' for IPC pip, 'F' for finalization pip, and 'Shutdown0' is the shutdown pip of service 'S0')
    ///
    ///   S0-I0-F0 Shutdown0
    ///      |
    ///      I1   P5
    ///    / | \ /
    ///   P2 P3 P4
    /// </summary>
    [Trait("Category", "PartialEvaluation")]
    public class GraphWithOneServiceInSpec0AndManyIpcPipsTests : GraphBasedTestBase
    {
        protected virtual int ServiceSpecIndex { get; } = 0;

        public GraphWithOneServiceInSpec0AndManyIpcPipsTests(ITestOutputHelper output) : base(output)
        {
        }

        /// <summary>
        /// For 'spec{s}', the content is
        ///
        ///     export const service = startService('p{s}');
        ///     export const x{s} = ipcSend(service, 'p{s}', [ NS{j1}..x{j1}, ..., NS{jn}.x{jn} ]);
        ///     
        /// For all dependendents of 'spec{s}', the content looks something like
        ///
        ///     export const x{i} = ipcSend(
        ///         NS0.service, 
        ///         'p{i}', 
        ///         [ NS{j1}.x{j1}, ..., NS{jn}.x{jn} ]);
        ///         
        ///  For all other files, the content is
        ///
        ///     export const x{i} = runProcess('p{i}', [ NS{j1}.x{j1}, ..., NS{jn}.x{jn} ]);
        /// </summary>
        protected override string GenerateSpec(int specIndex, List<int> specDependencies)
        {
            var commaSeparatedImports = string.Join(", ", specDependencies.Select(specIdx => $"{GetSpecNamespace(specIdx)}.x{specIdx}"));

            if (specIndex == ServiceSpecIndex)
            {
                return string.Join(Environment.NewLine, new string[]
                {
                    $"namespace NS{specIndex} {{ export const service = startService('{GetProcTag(specIndex)}'); }}",

                    $"namespace NS{specIndex} {{ export const x{specIndex} = ipcSend(service, '{GetProcTag(specIndex)}', [ {commaSeparatedImports} ]); }}",
                });
            }

            if (specDependencies.Contains(ServiceSpecIndex))
            {
                return string.Join(Environment.NewLine, new string[] 
                                                        {
                                                            $"namespace NS{specIndex} {{ export const x{specIndex} = ipcSend(",
                                                            $"    {GetSpecNamespace(ServiceSpecIndex)}.service, ",
                                                            $"    '{GetProcTag(specIndex)}',",
                                                            $"    [ { commaSeparatedImports} ]);",
                                                            $"}}",
                                                        });
            }

            return $"namespace NS{specIndex} {{ export const x{specIndex} = runProcess('{GetProcTag(specIndex)}', [ {commaSeparatedImports} ]); }}";
        }

        protected override void AssertPipGraphContent(PipGraph pipGraph, SimpleGraph file2file, StringTable stringTable)
        {
            var serviceSpecDependents = Join(new[] { ServiceSpecIndex }, Inverse(file2file.Edges));
            var nonServiceSpecDependents = file2file.Nodes.Except(new[] { ServiceSpecIndex }).Except(serviceSpecDependents).ToList();

            AssertPipGraphCounts(pipGraph, new Dictionary<PipType, int>
            {
                [PipType.Process] = 3 + nonServiceSpecDependents.Count, // service pip, service shutdown pip, and service finalize pip in 'serviceSpec' non-dependent specs of 'serviceSpec'
                [PipType.Ipc] = 1 + serviceSpecDependents.Count, // one IPC pip in 'serviceSpec' and one in each of its dependents
            });
            var ipcPips = pipGraph.RetrievePipsOfType(PipType.Ipc).Cast<IpcPip>().ToList();
            var processAndIpcPips = pipGraph.RetrievePipsOfType(PipType.Process).Concat(ipcPips).ToList();
            AssertEdges(pipGraph, file2file, processAndIpcPips, stringTable);
            AssertMonikerConsistencyForIpcPip(ipcPips, pipGraph);
        }
    }
}
