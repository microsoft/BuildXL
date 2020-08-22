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
    public class ToolTests : DsTestWithCacheBase
    {
        public ToolTests(ITestOutputHelper output) : base(output, usePassThroughFileSystem: true)
        {
        }

        [Fact]
        public void TestToolTimeOuts()
        {
            string spec = @"
import {Artifact, Cmd, Transformer} from 'Sdk.Transformers';

const outDir = Context.getNewOutputDirectory('tool');

const outFile1 = p`${outDir}/file1.out`;
const outFile2 = p`${outDir}/file2.out`;

export const cmdTool: Transformer.ToolDefinition = Context.isWindowsOS()
    ? getCmdToolDefinition(56789, 5678)
    : getBashToolDefinition(56789, 5678);


export const nestedCmdTool: Transformer.ToolDefinition = Context.isWindowsOS()
    ? getCmdToolDefinition(123456, 12345)
    : getBashToolDefinition(123456, 12345);

export const arguments1: Argument[] = Context.isWindowsOS()
    ? getExecuteArgumentsForCmd(outFile1)
    : getExecuteArgumentsForBash(outFile1);

export const arguments2: Argument[] = Context.isWindowsOS()
    ? getExecuteArgumentsForCmd(outFile2)
    : getExecuteArgumentsForBash(outFile2);

function getCmdToolDefinition(timeout: number, warningTimeout: number): Transformer.ToolDefinition {
    return {
        exe: f`${Environment.getPathValue('ComSpec')}`,
        dependsOnWindowsDirectories: true,
        timeoutInMilliseconds: timeout,
        warningTimeoutInMilliseconds: warningTimeout,
    };
}

function getBashToolDefinition(timeout: number, warningTimeout: number): Transformer.ToolDefinition {
    return {
        exe: f`/bin/bash`,
        runtimeDependencies: [
            f`/bin/sh`
        ],
        untrackedDirectoryScopes: [
            d`/usr`,
            d`/private`,
            d`/dev`,
            d`/etc`,
            d`/Library`,
            d`/System/Library`,
            d`/AppleInternal`,
            d`/var`,
            d`/bin`
        ],
        timeoutInMilliseconds: timeout,
        warningTimeoutInMilliseconds: warningTimeout,
    };
}

function getExecuteArgumentsForCmd(outFile: Path): Argument[] {
    return [
        Cmd.argument(' / d'),
        Cmd.argument('/c'),
        Cmd.argument('echo'),
        Cmd.argument('echo Hello'),
        Cmd.rawArgument(' >> '),
        Cmd.argument(Artifact.output(outFile))
    ];
}

function getExecuteArgumentsForBash(outFile: Path): Argument[] {
    return [
        Cmd.argument('-c'),
        Cmd.argument('echo'),
        Cmd.argument('echo Hello'),
        Cmd.rawArgument(' >> '),
        Cmd.argument(Artifact.output(outFile))
    ];
}

export const pip1 = Transformer.execute({
    tool: cmdTool,
    arguments:arguments1,
    workingDirectory: d`.`,
});

export const pip2 = Transformer.execute({
    tool: cmdTool,
    arguments:arguments2,
    workingDirectory: d`.`,
});
";
            using (var tempFiles = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory))
            {
                var appDeployment = CreateAppDeployment(tempFiles);

                var config = Build()
                    .AddSpec(spec)
                    .PersistSpecsAndGetConfiguration();

                // Scheduling is enough, we just want to inspect the timeouts
                ((CommandLineConfiguration)config).Engine.Phase = global::BuildXL.Utilities.Configuration.EnginePhases.Schedule;

                var engineResult = CreateAndRunEngine(
                    config,
                    appDeployment,
                    testRootDirectory: null,
                    rememberAllChangedTrackedInputs: false,
                    engine: out var engine);

                XAssert.IsTrue(engineResult.IsSuccess);

                // The corresponding process pip should have the mainTool's timeouts
                var processes = engineResult.EngineState.PipGraph.RetrievePipsOfType(PipsOperations.PipType.Process);
                XAssert.AreEqual(2, processes.Count());
                var process0 = (PipsOperations.Process)processes.ElementAt(0);
                var process1 = (PipsOperations.Process)processes.ElementAt(1);

                XAssert.AreEqual(true, process0.Timeout.HasValue);
                XAssert.AreEqual(56789, process0.Timeout.Value.TotalMilliseconds);
                XAssert.AreEqual(true, process0.WarningTimeout.HasValue);
                XAssert.AreEqual(5678, process0.WarningTimeout.Value.TotalMilliseconds);

                // The second process't tool is from cache. Same timeout value.
                XAssert.AreEqual(true, process1.Timeout.HasValue);
                XAssert.AreEqual(56789, process1.Timeout.Value.TotalMilliseconds);
                XAssert.AreEqual(true, process1.WarningTimeout.HasValue);
                XAssert.AreEqual(5678, process1.WarningTimeout.Value.TotalMilliseconds);
            }
        }
    }
}
