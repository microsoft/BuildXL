import {Cmd, Artifact, Transformer} from "Sdk.Transformers";
import {XCode} from "Sdk.MacOS";

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
        dependencies?: StaticDirectory[]
    }

    interface Result {
        outFiles: DerivedFile[],
        derivedDataOutDir: StaticDirectory
    }

    const sandboxSealDir = Transformer.sealSourceDirectory(
        d`${Context.getMount("Sandbox").path}`,
        Transformer.SealSourceDirectoryOption.allDirectories);

    const thirdPartySealDir = Transformer.sealSourceDirectory(
        d`../../../../third_party`,
        Transformer.SealSourceDirectoryOption.allDirectories);

    export function build(args: Args): Result {
        const conf = args.configuration || qualifier.configuration;
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

    const bundleInfoXCConfig = qualifier.configuration === "debug"
        ? f`BundleInfoDebug.xcconfig`
        : f`BundleInfo.xcconfig`;

    const isMacOs = Context.getCurrentHost().os === "macOS";
    const interopXcodeproj = Transformer.sealDirectory({
        root: d`Interop/Interop.xcodeproj`, 
        files: globR(d`Interop/Interop.xcodeproj`, "*")
    });
    const sandboxXcodeproj = Transformer.sealDirectory({
        root: d`Sandbox/Sandbox.xcodeproj`, 
        files: globR(d`Sandbox/Sandbox.xcodeproj`, "*")
    });

    const ariaPkg = importFrom("Aria.Cpp.SDK.osx-x64");
    const ariaXcconfig = Transformer.writeData({
        outputPath: p`${Context.getNewOutputDirectory("xcconfig")}/Aria.xcconfig`,
        contents: {
            separator: "\n",
            contents: [
                "GCC_PREPROCESSOR_DEFINITIONS = MICROSOFT_INTERNAL",
                { separator: "", contents: ["LIBRARY_SEARCH_PATHS = $(inherited) \"", ariaPkg.Contents.all.root, "/tools"]},
                { separator: "", contents: ["HEADER_SEARCH_PATHS = $(inherited) \"", ariaPkg.Contents.all.root, "/tools/include"]},
                "OTHER_LDFLAGS = $(inherited) -laria_osx_objc_cpp"
            ]
        }
    });

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
        outFiles: [ a`libBuildXLInterop.dylib` ],
        xcconfig: bundleInfoXCConfig
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
        outFiles: [ a`SandboxMonitor` ],
        xcconfig: bundleInfoXCConfig
    }).outFiles[0];

    interface KextFiles {
        plist: DerivedFile,
        sandbox: DerivedFile,
        license: DerivedFile,
        codeRes: DerivedFile,
        dSYMPlist: DerivedFile,
        dSYMDwarf: DerivedFile
    }

    function buildKext(bundleInfo: File): KextFiles {
        const result = build({
            project: sandboxXcodeproj,
            scheme: "BuildXLSandbox",
            xcconfig: bundleInfo || bundleInfoXCConfig,
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
        return {
            plist: result.outFiles[0],
            sandbox: result.outFiles[1],
            license: result.outFiles[2],
            codeRes: result.outFiles[3],
            dSYMPlist: qualifier.configuration === "release" ? result.outFiles[4] : undefined,
            dSYMDwarf: qualifier.configuration === "release" ? result.outFiles[5] : undefined,
        };
    }

    @@public
    export const kext = isMacOs && buildKext(bundleInfoXCConfig);
}
