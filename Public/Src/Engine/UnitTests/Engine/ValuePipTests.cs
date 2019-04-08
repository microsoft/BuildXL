// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using BuildXL.Engine;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.Engine;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.EngineTests
{
    [Trait("Category", "ValuePipTests")]
    public class ValuePipTests : BaseEngineTest
    {
        public ValuePipTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestValuePips()
        {
            var spec = @"
import {Artifact, Cmd, Tool, Transformer} from 'Sdk.Transformers';


@@public
export const myString1 = 'mini1-line1';
const myString2 = myString1;

const mini1 = Transformer.writeAllLines(
    p`obj/mini1.txt`,
    [
        'mini1-line1',
        myString2,
    ]);

export const mini2 = Transformer.copyFile(
    mini1,
    p`obj/mini2.txt`);

namespace Ns1 {
    @@public
    export const mini3 = Transformer.copyFile(
        mini2,
        p`obj/mini3.txt`);
}
";
            AddModule("MyModule", ("spec.dsc", spec), placeInRoot: true);

            Configuration.Engine.Phase = EnginePhases.Schedule;
            TestHooks.Scheduler = new BoxRef<Scheduler>();
            RunEngine();

            var graph = TestHooks.Scheduler.Value.PipGraph;

            var valuePips = graph.RetrievePipsOfType(PipType.Value).ToArray();
            Assert.Equal(3, valuePips.Length);

            var mini1 = valuePips[0] as ValuePip;
            Assert.NotNull(mini1);
            Assert.Equal("mini1", mini1.Symbol.ToString(Context.SymbolTable));
            Assert.Equal(1, graph.RetrievePipImmediateDependents(mini1).Count());
            Assert.Equal(1, graph.RetrievePipImmediateDependencies(mini1).Count());
            var mini1Spec = (graph.RetrievePipImmediateDependents(mini1).FirstOrDefault(pip => pip.PipType == PipType.SpecFile) as SpecFilePip).SpecFile.Path;
            var mini1Write = graph.RetrievePipImmediateDependencies(mini1).FirstOrDefault(pip => pip.PipType == PipType.WriteFile) as WriteFile;
            Assert.NotNull(mini1Write);

            var mini2 = valuePips[1] as ValuePip;
            Assert.NotNull(mini2);
            Assert.Equal("mini2", mini2.Symbol.ToString(Context.SymbolTable));
            Assert.Equal(1, graph.RetrievePipImmediateDependents(mini2).Count());
            Assert.Equal(1, graph.RetrievePipImmediateDependencies(mini2).Count());
            var mini2Spec = (graph.RetrievePipImmediateDependents(mini2).FirstOrDefault(pip => pip.PipType == PipType.SpecFile) as SpecFilePip).SpecFile.Path;
            var mini2Copy = graph.RetrievePipImmediateDependencies(mini2).FirstOrDefault(pip => pip.PipType == PipType.CopyFile) as CopyFile;
            Assert.NotNull(mini2Copy);

            var mini3 = valuePips[2] as ValuePip;
            Assert.NotNull(mini3);
            Assert.Equal("Ns1.mini3", mini3.Symbol.ToString(Context.SymbolTable));
            Assert.Equal(1, graph.RetrievePipImmediateDependents(mini3).Count());
            Assert.Equal(1, graph.RetrievePipImmediateDependencies(mini3).Count());
            var mini3Spec = (graph.RetrievePipImmediateDependents(mini3).FirstOrDefault(pip => pip.PipType == PipType.SpecFile) as SpecFilePip).SpecFile.Path;
            var mini3Copy = graph.RetrievePipImmediateDependencies(mini3).FirstOrDefault(pip => pip.PipType == PipType.CopyFile) as CopyFile;
            Assert.NotNull(mini3Copy);


            Assert.Equal(mini1Spec, mini2Spec);
            Assert.Equal(mini1Spec, mini3Spec);
        }
    }
}
