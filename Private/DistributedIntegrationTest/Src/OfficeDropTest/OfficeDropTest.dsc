// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Drop from "OfficeDropSdk";
import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import {range, cmdExe} from "DistributedIntegrationTests.Utils";

const dropNameEnvVarName = "TF_ROLLING_DROPNAME";

const numFiles = 100;
const buildNumber = Environment.hasVariable(dropNameEnvVarName)
    ? Environment.getStringValue(dropNameEnvVarName)
    : undefined;

@@public
export const test = runTest(buildNumber ? `buildxl.rolling.officetest/${buildNumber}` : undefined);

function runTest(dropName: string) {
    if (dropName === undefined) {
        Debug.writeLine(`*** Skipping OfficeDropTest *** (because env var '${dropNameEnvVarName}' is not defined)`);
    }
    else
    {
        const enableDrop = Environment.getBooleanValue("OfficeDropTestEnableDrop");
        const outdir = Context.getNewOutputDirectory("officeDropTest");
        const batchSize = numFiles * 3 + 1;
        const configFile = Transformer.writeAllLines(outdir.combine("config.json"), [
            "{",
            `   "Name": "${dropName}",`,
            `   "EnableCloudBuildIntegration": true,`,
            `   "Service": "https://mseng.artifacts.visualstudio.com/DefaultCollection",`,
            `   "Verbose": true,`,
            `   "RetentionDays": 3,`,
            `   "BatchSize": ${batchSize},`,
            `   "MaxConcurrentClients": ${batchSize > 1 ? batchSize : 2},`,
            `   "NagleTimeMillis": 2000`,
            "}"
        ]);
        const dropData = enableDrop && Drop.startDrop(configFile);
        const dropFile = (f: File, tags?: string[]) => enableDrop ? Drop.dropFile(dropData, r`${f.name}`, f, tags) : {};
        const dropFiles = (fs: File[], tags?: string[]) => enableDrop ? Drop.dropFiles(dropData, fs.map(f => <[RelativePath, File]>[r`${f.name}`, f]), tags) : {};

        range(0, numFiles).map(idx => {
            const fileNameSuffix = `test-file-${idx}.txt`;

            // drop a file that is an output of a process pip that differs from build to build (hence, never deployed from cache)
            const file1 = writeToFileViaShellExecute(outdir, `build-specific-${fileNameSuffix}`, `[${dropName}] Hello World ${idx}!`);

            // drop a file that is an output of a process pip whose content stays the same from build to build (hence, can be deployed from cache)
            const file2 = writeToFileViaShellExecute(outdir, `const-${fileNameSuffix}`, `[[const]](${idx}) Hello World from BuildXL`);

            // drop a file that is an output of a copy pip, which copies a file that can be deployed from cache
            const file3 = Transformer.copyFile(file2, outdir.combine(`copy-of-${file2.name}`));

            dropFiles([file1, file2, file3, file1, file2, file3]);
        });

        // drop a source file tagged with 'exclude-drop-file', which is filtered out for testing purposes
        dropFile(f`module.config.dsc`, ["exclude-drop-file"]);

        // finally, drop a source file too
        dropFile(f`test-file-to-add-to-drop.txt`);
    }
}

function writeToFileViaShellExecute(outDir: Directory, fileName: string, content: string): DerivedFile {
    const outFile = outDir.combine(fileName);
    const result = Transformer.execute({
        tool: cmdExe,
        workingDirectory: outDir,
        arguments: [
           Cmd.argument("/d"), 
           Cmd.argument("/c"),
           Cmd.argument("echo"),
           Cmd.rawArgument(content),
           Cmd.rawArgument(" > "),
           Cmd.argument(Artifact.output(outFile))
        ]
    });
    return result.getOutputFile(outFile);
}
