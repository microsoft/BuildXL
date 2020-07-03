// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Test.DScript.Ast;
using Xunit;
using Xunit.Abstractions;
using PipsOperations = global::BuildXL.Pips.Operations;

namespace Test.BuildXL.FrontEnd.Script.Interpretation.Transformers
{
    public class TagTests : DsTestWithCacheBase
    {
        public TagTests(ITestOutputHelper output) : base(output, usePassThroughFileSystem: true)
        {
        }

        [Fact]
        public void QualifierBasedTagsAreSet()
        {
            // Writes a file using a process pip with user defined tags 'test1' and 'test2'
            string spec = @"
import {Cmd, Transformer} from 'Sdk.Transformers';

export declare const qualifier: {platform: 'x86' | 'x64'; configuration: 'debug' | 'release'};

const outDir = Context.getNewOutputDirectory('tags');

const result = Transformer.execute({
    tool: {" +
        $"exe: f`{(OperatingSystemHelper.IsUnixOS ? "/bin/sh" : @"${Environment.getPathValue(""COMSPEC"")}")}`"
    + @"},
    workingDirectory: d`.`,
    arguments: [ Cmd.rawArgument('" + $"{(OperatingSystemHelper.IsUnixOS ? "-c echo test > obj/a.txt" : @"/d /c echo test > obj\a.txt")}" + @"'),
    ],
    outputs: [
        p`${outDir}/obj/a.txt`,
    ],
    tags: ['test1', 'test2']
});
";
            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory))
            {
                var appDeployment = CreateAppDeployment(tempFiles);
                
                // Configure the initial qualifier to be configuration=debug, platform=x64
                var config = Build()
                    .Configuration(@"
config({
    qualifiers: {
        defaultQualifier: {
            configuration: 'debug',
            platform: 'x64'
        }
    }
});")
                    .AddSpec(spec)
                    .PersistSpecsAndGetConfiguration();

                // Scheduling is enough, we just want to inspect the tags
                ((CommandLineConfiguration)config).Engine.Phase = global::BuildXL.Utilities.Configuration.EnginePhases.Schedule;

                var engineResult = CreateAndRunEngine(
                    config,
                    appDeployment,
                    testRootDirectory: null,
                    rememberAllChangedTrackedInputs: false,
                    engine: out var engine);

                XAssert.IsTrue(engineResult.IsSuccess);

                // The corresponding process pip should have both the user defined tags and the current qualifier tags
                var process = (PipsOperations.Process) engineResult.EngineState.PipGraph.RetrievePipsOfType(PipsOperations.PipType.Process).Single();
                XAssert.Contains(process.Tags.Select(tag => tag.ToString(StringTable)), "test1", "test2", "platform=x64", "configuration=debug");
            }
        }
    }
}
