// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import {cmdExe} from "DistributedIntegrationTests.Utils";

function main() {
    // BuildXL invocation specifies inputfile.txt as source change.
    const inputFile = f`./inputfile.txt`;

    const outputDir1 = Context.getNewOutputDirectory("outputDirectory1");
    const outputFile1 = p`${outputDir1}/outputfile1.txt`;
    
    // inputfile.txt -> Process1 -> outputDirectory1/outputfile1.txt
    let result = Transformer.execute({
        tool: cmdExe,
        description: "First process pip in ChangeAffectedInputTest",
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument(Artifact.input(f`./readFromAndWriteToFile.cmd`)),
            Cmd.argument(Artifact.input(inputFile)),
            Cmd.argument(Artifact.output(outputFile1)),
        ],
        workingDirectory: d`.`
    });

    const outputFile2 = p`${outputDir1}/outputfile2.txt`;

    // outputDirectory1/outputfile1.txt -> Process2 -> outputDirectory1/outputfile2.txt
    result = Transformer.execute({
        tool: cmdExe,
        description: "Second process pip in ChangeAffectedInputTest",
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument(Artifact.input(f`./readFromAndWriteToFile.cmd`)),
            Cmd.argument(Artifact.input(result.getOutputFile(outputFile1))),
            Cmd.argument(Artifact.output(outputFile2)),
        ],
        workingDirectory: d`.`
    });

    const outputDir2 = Context.getNewOutputDirectory("outputDirectory2");
    
    // outputDirectory1/outputfile2.txt -> CopyFile -> outputDirectory2/copied_outputfile2.txt
    const copiedOutputFile2 = Transformer.copyFile(
        result.getOutputFile(outputFile2),
        p`${outputDir2}/copied_outputfile2.txt`,
        [],
        "CopyFile pip in ChangeAffectedInputTest");

    // sealedDir = Seal(outputDirectory2, members: [outputDirectory2/copied_outputfile2.txt])
    let sealedDir = Transformer.sealDirectory({
        root: outputDir2,
        files: [copiedOutputFile2],
        scrub: true,
        description: "sealDirectory pip in ChangeAffectedInputTest" 
    });

    const changeAffectedInputListWrittenFile = p`${outputDir1}/changed_affected_file.txt`;
    const outputFile3 = p`${outputDir1}/outputfile3.txt`;
    
    // SealedDir -> Process3 (last) -> outputDirectory1/outputfile3.txt
    // Prior to Process3 execution, BuildXL writes outputDirectory1/changed_affected_file.txt containing inputs that are affected by source change, i.e., inputfile.txt.
    // The file outputDirectory1/changed_affected_file.txt should contain the path outputDirectory2/copied_outputfile2.txt, due to Process3's dependency on the sealed directory.
    // Process3 reads outputDirectory1/changed_affected_file.txt and write its content to outputDirectory1/outputfile3.txt.
    result = Transformer.execute({
        tool: cmdExe,
        description: "Last process pip in ChangeAffectedInputTest",
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument(Artifact.input(f`./readFromAndWriteToFile.cmd`)),
            Cmd.argument(Artifact.none(changeAffectedInputListWrittenFile)),
            Cmd.argument(Artifact.output(outputFile3)),
        ],
        dependencies: [sealedDir],
        changeAffectedInputListWrittenFile : changeAffectedInputListWrittenFile,
        workingDirectory: d`.`
    });

    // Expected change affected input for Process3 should contain outputDirectory2/copied_outputfile2.txt.
    // The expected change is written to outputDirectory1/expected_changed_affected_file.txt.
    const expectedChangeAffectedInputListWrittenFile = Transformer.writeFile(p`${outputDir1}/expected_changed_affected_file.txt`, copiedOutputFile2.path);

    const verificationOutput = p`${outputDir1}/verify.txt`;

    // Verify that outputDirectory1/outputfile3.txt and outputDirectory1/expected_changed_affected_file.txt have the same content.
    Transformer.execute({
        tool: cmdExe,
        description: "Verification pip in ChangeAffectedInputTest",
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument(Artifact.input(f`../Utils/verifyOutput.cmd`)),
            Cmd.argument(Artifact.input(expectedChangeAffectedInputListWrittenFile)),
            Cmd.argument(Artifact.input(result.getOutputFile(outputFile3))),
            Cmd.argument(Artifact.output(verificationOutput)),
        ],
        workingDirectory: d`.`
    });

    return 0;
}

@@public
export const go = main();