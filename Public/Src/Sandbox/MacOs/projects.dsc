import {Cmd, Artifact, Transformer} from "Sdk.Transformers";
import {Clang, Xcode} from "Sdk.MacOS";

namespace Sandbox {
    export declare const qualifier : {
        configuration: "debug" | "release"
    };

    interface Args {
        project: StaticDirectory,
        scheme: string,
        configuration?: string,
        outFiles?: PathFragment[],
        semaphores?: string[],
        xcconfig?: File,
        dependencies?: StaticDirectory[],
        overrideXcodeBuildPath?: File,
        headerSearchPaths?: Xcode.HeaderSearchPath[];
    }

    interface Result {
        outFiles: DerivedFile[],
        derivedDataOutDir: StaticDirectory
    }

    const sourceFileDependencies = (() =>
    {
        const sandboxDir = d`${Context.getMount("Sandbox").path}`;
        const thirdPartyDir = d`../../../../third_party`;

        const sourceSealOptions =  Transformer.SealSourceDirectoryOption.allDirectories;
        const globPattern = "*";

        return BuildXLSdk.Flags.isMicrosoftInternal ?
            [
                Transformer.sealSourceDirectory(sandboxDir, sourceSealOptions),
                Transformer.sealSourceDirectory(thirdPartyDir, sourceSealOptions)
            ] : [
                ...Transformer.sealDirectory(sandboxDir, globR(sandboxDir, globPattern)).contents,
                ...Transformer.sealDirectory(thirdPartyDir, globR(thirdPartyDir, globPattern)).contents
            ];
    })();

    export function build(args: Args): Result {
        const conf = args.configuration || qualifier.configuration;
        const outDir = Context.getNewOutputDirectory(args.scheme);
        const outFilePaths = (args.outFiles || []).map(a => p`${outDir}/Build/Products/${conf}/${a}`);
        const result = Xcode.execute({
            useModernBuildSystem: true,
            project: args.project,
            xcconfig: args.xcconfig,
            scheme: args.scheme,
            semaphores: args.semaphores,
            configuration: conf,
            derivedDataPath: outDir,
            declaredOutputs: [
                ...outFilePaths,
            ],
            dependencies: [
                ...(args.dependencies || []),
                ...sourceFileDependencies
            ],
            overrideXcodeBuildPath: args.overrideXcodeBuildPath,
            headerSearchPaths: args.headerSearchPaths
        });

        return {
            outFiles: outFilePaths.map(result.getOutputFile),
            derivedDataOutDir: result.getOutputDirectory(outDir)
        };
    }

    const bundleInfoXCConfig = qualifier.configuration === "debug"
        ? f`BundleInfoDebug.xcconfig`
        : f`BundleInfo.xcconfig`;

    const isMacOs = Context.getCurrentHost().os === "macOS";

    const interopXcodeproj = Transformer.sealDirectory({
        root: d`Interop/Interop.xcodeproj`,
        files: globR(d`Interop/Interop.xcodeproj`, "*")
    });
}
