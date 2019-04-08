// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Xunit;
using Xunit.Abstractions;

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
    /// 'I' for IPC pip, 'F' for finalization pip, and 'Shutdown1' is the shutdown pip of service 'S1')
    ///
    ///      P0
    ///      |
    ///  S1-I1-F1 P5 Shutdown1
    ///    / | \ /
    ///   I2 I3 I4
    /// </summary>
    [Trait("Category", "PartialEvaluation")]
    public sealed class GraphWithOneServiceInSpec1AndManyIpcPipsTests : GraphWithOneServiceInSpec0AndManyIpcPipsTests
    {
        public GraphWithOneServiceInSpec1AndManyIpcPipsTests(ITestOutputHelper output) : base(output)
        {
        }

        protected override int ServiceSpecIndex { get; } = 1;
    }
}
