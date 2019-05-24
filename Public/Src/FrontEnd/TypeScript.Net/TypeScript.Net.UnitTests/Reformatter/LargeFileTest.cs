// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.UnitTests.Utils;
using Xunit;

namespace Test.DScript.Reformatter
{
    public sealed class LargeFileTest
    {
        [Fact]
        public void SampleTransformer()
        {
            string code = @"{
    const x = Transformer.execute({
        tool: {exe: f`./src/tool/tool.exe`},
        tags: ['tagA', 'tagB'],
        arguments: [],
        workingDirectory: d`./Out/working`,
        dependencies: [
            f`./src/tool/tool.exe`,
            f`./src/tool/helper.dll`,
            f`./src/tool/nested.exe`,
            f`./src/input/file.txt`,
            f`./src/stdIn.txt`,
            d`./src/seal2`,
            d`./src/seal1`,
        ],
        implicitOutputs: [
            f`./Out/outputFile.txt`,
            f`./Out/outputFile1.txt`,
            f`./Out/outputFile.txt`,
            f`./Out/outputFile.txt`,
            f`./Out/stdOut.txt`,
            f`./Out/stdErr.txt`,
            d`./Out/dynamicSealedDirectory`,
        ],
        optionalImplicitOutputs: [
            f`./Out/optionalImplicitOutput1.txt`,
            f`./Out/optionalImplicitOutput2.txt`,
            f`./Out/optionalImplicitOutput3.txt`,
            f`./Out/optionalImplicitOutput4.txt`,
            f`./Out/optionalImplicitOutput5.txt`,
            f`./Out/optionalImplicitOutput6.txt`,
        ],
        consoleInput: f`./src/stdIn.txt`,
        consoleOutput: p`./Out/stdOut.txt`,
        consoleError: p`./Out/stdErr.txt`,
        environmentVariables: [
            {name: 'env1', value: []},
            {name: 'env2', value: []},
            {name: 'env3', value: []},
            {name: 'env4', value: []},
            {name: 'TEMP', value: []},
            {name: 'TMP', value: []},
        ],
        warningRegex: '^\\s*((((((\\d+>)?[a-zA-Z]?:[^:]*)|([^:]*))):)|())(()|([^:]*? ))warning( \\s*([^: ]*))?\\s*:.*$',
        acquireSemaphores: [
            {
                name: 'semaphore1',
                incrementBy: 2,
                limit: 2,
            },
            {
                name: 'semaphore2',
                incrementBy: 1,
                limit: 1,
            },
            {
                name: 'mutex1',
                incrementBy: 1,
                limit: 1,
            },
            {
                name: 'mutex2',
                incrementBy: 1,
                limit: 1,
            },
        ],
        successExitCodes: [0, 1, 2],
        tempDirectory: d`./Out/Temp`,
        additionalTempDirectories: [d`./Out/extraTemp1`, d`./Out/extraTemp2`],
        unsafe: {
            untrackedPaths: [
                p`./src/tool/untrackedFile1.txt`,
                p`./src/tool/untrackedFile2.txt`,
                p`./src/tool/untrackedDirectory`,
                p`./src/untrackedPathOnPip`,
            ],
            untrackedScopes: [
                p`C:/WINDOWS`,
                p`C:/Users/userName/AppData/Local/Microsoft/Windows/INetCache`,
                p`C:/Users/userName/AppData/Local/Microsoft/Windows/History`,
                p`C:/Users/userName/AppData/Roaming`,
                p`C:/Users/userName/AppData/Local`,
                p`./src/tool/untrackedDirectoryScope`,
                p`./Out/dynamicSealedDirectory`,
                p`./Out/Temp`,
                p`./Out/extraTemp1`,
                p`./Out/extraTemp2`,
                p`./src/untrackedScopeOnPip`,
            ],
            hasUntrackedChildProcesses: true,
            allowPreservedOutputs: true,
        },
        keepOutputsWritable: true,
    });
}";

            var node = ParsingHelper.ParseFirstStatementFrom<IBlock>(code);
            var actual = node.GetFormattedText();
            Assert.Equal(code, actual);
        }
    }
}
