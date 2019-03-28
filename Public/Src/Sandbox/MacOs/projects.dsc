import {Cmd, Artifact, Transformer} from "Sdk.Transformers";
import * as XCode from "Sdk.MacOS";

namespace Sandbox {
    export declare const qualifier : {
        configuration: "debug" | "release"
    };

    interface Args {
        project: StaticDirectory,
        scheme: string,
        outFiles?: PathFragment[],
        semaphores?: string[],
        xcconfig?: File,
        dependencies?: StaticDirectory[]
    }

    interface Result {
        outFiles: File[],
        derivedDataOutDir: StaticDirectory
    }

    const sandboxSealDir = Transformer.sealSourceDirectory(
        d`${Context.getMount("Sandbox").path}`,
        Transformer.SealSourceDirectoryOption.allDirectories);

    const thirdPartySealDir = Transformer.sealSourceDirectory(
        d`../../../../third_party`,
        Transformer.SealSourceDirectoryOption.allDirectories);

    export function build(args: Args): Result {
        const conf = qualifier.configuration;
        const outDir = Context.getNewOutputDirectory(args.scheme);
        const outFilePaths = (args.outFiles || []).map(a => p`${outDir}/Build/Products/${conf}/${a}`);
        const result = XCode.execute({
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
                sandboxSealDir,
                thirdPartySealDir
            ]
        });
        return {
            outFiles: outFilePaths.map(result.getOutputFile),
            derivedDataOutDir: result.getOutputDirectory(outDir)
        };
    }

    const isMacOs = Context.getCurrentHost().os === "macOS";
    const interopXcodeproj = Transformer.sealDirectory(d`Interop/Interop.xcodeproj`, globR(d`Interop/Interop.xcodeproj`, "*"));
    const sandboxXcodeproj = Transformer.sealDirectory(d`Sandbox/Sandbox.xcodeproj`, globR(d`Sandbox/Sandbox.xcodeproj`, "*"));

    const ariaPkg = importFrom("Aria.Cpp.SDK.osx-x64");
    const ariaXcconfig = Transformer.writeData(
        p`${Context.getNewOutputDirectory("xcconfig")}/Aria.xcconfig`,
        {
            separator: "\n",
            contents: [
                "GCC_PREPROCESSOR_DEFINITIONS = MICROSOFT_INTERNAL",
                { separator: "", contents: ["LIBRARY_SEARCH_PATHS = $(inherited) \"", ariaPkg.Contents.all.root, "/tools"]},
                { separator: "", contents: ["HEADER_SEARCH_PATHS = $(inherited) \"", ariaPkg.Contents.all.root, "/tools/include"]},
                "OTHER_LDFLAGS = $(inherited) -laria_osx_objc_cpp"
            ]
        }
    );

    @@public
    export const libAria = !BuildXLSdk.Flags.isMicrosoftInternal ? undefined : isMacOs && build({
        project: interopXcodeproj,
        scheme: "AriaLibrary",
        outFiles: [ a`libBuildXLAria.dylib`],
        semaphores: [ "BuildXL.Aria" ], // prevents running debug/release builds of Aria concurrently
        xcconfig: ariaXcconfig,
        dependencies: [ ariaPkg.Contents.all ]
    }).outFiles[0];

    @@public
    export const libInterop = isMacOs && build({
        project: interopXcodeproj,
        scheme: "InteropLibrary",
        outFiles: [ a`libBuildXLInterop.dylib` ]
    }).outFiles[0];

    @@public
    export const coreDumpTester = isMacOs && build({
        project: interopXcodeproj,
        scheme: "CoreDumpTester",
        outFiles: [ a`CoreDumpTester` ]
    }).outFiles[0];

    @@public
    export const monitor = isMacOs && build({
        project: sandboxXcodeproj,
        scheme: "SandboxMonitor",
        outFiles: [ a`SandboxMonitor` ]
    }).outFiles[0];

    const kext = isMacOs && build({
        project: sandboxXcodeproj,
        scheme: "BuildXLSandbox",
        outFiles: [
            r`BuildXLSandbox.kext/Contents/Info.plist`,
            r`BuildXLSandbox.kext/Contents/MacOS/BuildXLSandbox`,
            r`BuildXLSandbox.kext/Contents/Resources/LICENSE`,
            r`BuildXLSandbox.kext/Contents/_CodeSignature/CodeResources`,
            ...addIfLazy(qualifier.configuration === "release", () => [
                r`BuildXLSandbox.kext.dSYM/Contents/Info.plist`,
                r`BuildXLSandbox.kext.dSYM/Contents/Resources/DWARF/BuildXLSandbox`
            ])
        ]
    });

    @@public export const kextPlist   = kext && kext.outFiles[0];
    @@public export const kextSandbox = kext && kext.outFiles[1];
    @@public export const kextLicense = kext && kext.outFiles[2];
    @@public export const kextCodeRes = kext && kext.outFiles[3];

    @@public export const kextdSYMPlist = qualifier.configuration === "release" ? kext && kext.outFiles[4] : undefined;
    @@public export const kextdSYMDwarf = qualifier.configuration === "release" ? kext && kext.outFiles[5] : undefined;
}
