import { Arguments, build, NativeExeImage } from "Sdk.Native.Tools.Exe";
import * as boost from "boost";
import { Binary, PlatformDependentQualifier, Templates } from "Sdk.Native.Shared";
import { ClWarningLevel } from "Sdk.Native.Tools.Cl";
import { Transformer } from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";

export declare const qualifier : PlatformDependentQualifier;

const boostCompileArgs = {
    innerTemplates: {
        clRunner: {
            warningLevel: ClWarningLevel.level1,
            // There are some harmless macro redefinitions when compiling tests
            disableSpecificWarnings: [4005]
        },
    },
    includes: [
        boost.Contents.all.ensureContents({subFolder: r`lib/native/include`})
    ]
};

/** Arguments for testing a C++ compilation*/
@@public
export interface TestArgs extends Arguments {
    runtimeContent: (File | StaticDirectory)[]
}

/**
 * Runs Boost tests (https://www.boost.org/doc/libs/1_35_0/libs/test/doc/components/utf/index.html) on the specified sources
 */
@@public
export function test(args: TestArgs): TransformerExecuteResult {

    // Compile the test
    const compiled = build(Object.merge<Arguments>(boostCompileArgs, args));
    
    // Build the deployment
    const deployment : Deployment.Definition = {
        contents: [
            compiled.binaryFile,
            compiled.debugFile,
            ...args.runtimeContent
        ]
    };

    const deployDir = Context.getNewOutputDirectory("test-deploy");

    const onDiskDeployment = Deployment.deployToDisk({
        definition: deployment,
        targetDirectory: deployDir ,
        primaryFile: compiled.binaryFile.name,
    });

    const tool : Transformer.ToolDefinition = {
        exe: onDiskDeployment.primaryFile,
        description: "Boost Test",
        prepareTempDirectory: true,
        dependsOnAppDataDirectory: true,
        dependsOnWindowsDirectories: true,
    };

    // Run the test
    return Transformer.execute({
        tool: tool,
        arguments: [],
        workingDirectory: deployDir,
        // Boost test does not produce any output, just add a dummy one
        outputs: [{existence: "optional", artifact: p`${Context.getNewOutputDirectory("boost-test")}/dummy`}],
        dependencies: [onDiskDeployment.contents, ...onDiskDeployment.targetOpaques]
    });
}