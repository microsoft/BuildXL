// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import {cmdExe} from "DistributedIntegrationTests.Utils";

function main() {
    const inputfile = Environment.getFileValue("SOURCE_FILE");
    const tempOutDir = Context.getNewOutputDirectory("outputDirectory");
    const outfile1 = p`${tempOutDir}/outputfile1.txt`;
    const outfile2 = p`${tempOutDir}/outputfile2.txt`;
    const outfile3= p`${tempOutDir}/outputfile3.txt`;
    const verificationOutput = p`${tempOutDir}/verify.txt`;

    const outputDir = d`${Context.getMount("ChangeAffectedInputTest").path}/${Environment.getStringValue("OUTPUT_DIR_NAME")}`;
    const affectedInputForLastProcess = p`${outputDir}/${Environment.getStringValue("OUTPUT_FILE_NAME")}`;

    const changeAffectedInputListWrittenFile = f`./changeAffectedInputListWrittenFile.txt`;
    const expectedChangeAffectedInputListWrittenFile = Environment.getFileValue("EXPECTED_WRITTEN_FILE");

    let result = Transformer.execute({
        tool: cmdExe,
        description: "First process pip in ChangeAffectedInputTest",
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument(Artifact.input(f`./readFromAndWriteToFile.cmd`)),
            Cmd.argument(Artifact.input(inputfile)),
            Cmd.argument(Artifact.output(outfile1)),
        ],
        workingDirectory: d`.`
    });

    let outfile1FromResult = result.getOutputFile(outfile1);

    result = Transformer.execute({
        tool: cmdExe,
        description: "Second process pip in ChangeAffectedInputTest",
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument(Artifact.input(f`./readFromAndWriteToFile.cmd`)),
            Cmd.argument(Artifact.input(outfile1FromResult)),
            Cmd.argument(Artifact.output(outfile2)),
        ],
        workingDirectory: d`.`
    });

    let outfile2FromResult = result.getOutputFile(outfile2);

    let copiedFile = Transformer.copyFile(
                outfile2FromResult,
                affectedInputForLastProcess,
                [],
                "CopyFile pip in ChangeAffectedInputTest"
            );

    let sealedDir = Transformer.sealDirectory({
                root: outputDir,
                files: [copiedFile],
                scrub: true,
                description: "sealDirectory pip in ChangeAffectedInputTest",
            });

    Transformer.execute({
        tool: cmdExe,
        description: "Last process pip in ChangeAffectedInputTest",
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument(Artifact.input(f`./readFromAndWriteToFile.cmd`)),
            Cmd.argument(affectedInputForLastProcess),
            Cmd.argument(Artifact.output(outfile3)),
        ],
        dependencies: [sealedDir],
        changeAffectedInputListWrittenFile : changeAffectedInputListWrittenFile.path,
        workingDirectory: d`.`
    });

    Transformer.execute({
        tool: cmdExe,
        description: "Verification pip in ChangeAffectedInputTest",
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument(Artifact.input(f`../Utils/verifyOutput.cmd`)),
            Cmd.argument(Artifact.input(expectedChangeAffectedInputListWrittenFile)),
            Cmd.argument(Artifact.input(changeAffectedInputListWrittenFile)),
            Cmd.argument(Artifact.output(verificationOutput)),
        ],
        workingDirectory: d`.`
    });

    return 0;
}

@@public
export const go = main();