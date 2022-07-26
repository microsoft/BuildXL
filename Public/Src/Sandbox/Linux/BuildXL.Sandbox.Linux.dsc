// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Cmd, Artifact, Transformer} from "Sdk.Transformers";

// NOTE: should have the same effect as building with `make` using the Makefile from this dir.

namespace Sandbox {
    export declare const qualifier : {
        configuration: "debug" | "release",
        targetRuntime: "linux-x64"
    };

    const isLinux = Context.getCurrentHost().os === "unix";

    const gccTool = compilerTool("gcc");
    const gxxTool = compilerTool("g++");
    const commonSrc = [
        ...glob(d`../MacOs/Interop/Sandbox/Data`, "*.cpp"),
        ...glob(d`../MacOs/Interop/Sandbox/Handlers`, "*.cpp"),
        f`../MacOs/Interop/Sandbox/Sandbox.cpp`,
        f`../MacOs/Sandbox/Src/FileAccessManifest/FileAccessManifestParser.cpp`,
        f`../MacOs/Sandbox/Src/Kauth/Checkers.cpp`,
        f`../MacOs/Sandbox/Src/Kauth/OpNames.cpp`,
        f`../Windows/DetoursServices/PolicyResult_common.cpp`,
        f`../Windows/DetoursServices/PolicySearch.cpp`,
        f`../Windows/DetoursServices/StringOperations.cpp`,
        f`../Windows/DetoursServices/FilesCheckedForAccess.cpp`
    ];
    const utilsSrc   = [ f`utils.c` ];
    const bxlEnvSrc  = [ f`bxl-env.c` ];
    const auditSrc   = [ f`bxl_observer.cpp`, f`audit.cpp` ];
    const detoursSrc = [ f`bxl_observer.cpp`, f`detours.cpp` ];
    const incDirs    = [
        d`./`,
        d`../MacOs/Interop/Sandbox`,
        d`../MacOs/Interop/Sandbox/Data`,
        d`../MacOs/Interop/Sandbox/Handlers`,
        d`../MacOs/Sandbox/Src`,
        d`../MacOs/Sandbox/Src/FileAccessManifest`,
        d`../MacOs/Sandbox/Src/Kauth`,
        d`../Windows/DetoursServices`
    ];
    const headers = incDirs.mapMany(d => ["*.h", "*.hpp"].mapMany(q => glob(d, q)));

    export const commonObj  = commonSrc.map(compile);
    export const utilsObj   = utilsSrc.map(compile);
    export const bxlEnvObj  = bxlEnvSrc.map(compile);
    export const auditObj   = auditSrc.map(compile);
    export const detoursObj = detoursSrc.map(f => _compile(f, [ "ENABLE_INTERPOSING" ]));

    @@public
    export const libBxlUtils = link(a`libBxlUtils.so`, gccTool, utilsObj, []);
    @@public
    export const bxlEnv      = link(a`bxl-env`, gccTool, bxlEnvObj, []);
    @@public
    export const libBxlAudit = link(a`libBxlAudit.so`, gxxTool, [...commonObj, ...utilsObj, ...auditObj], [ "dl" ]);
    @@public
    export const libDetours  = link(a`libDetours.so`, gxxTool, [...commonObj, ...utilsObj, ...detoursObj], [ "dl", "pthread" ]);

    function compile(srcFile: File) { return _compile(srcFile, []); }
    function _compile(srcFile: File, defines: string[]): DerivedFile {
        if (!isLinux) return undefined;

        const isDebug = qualifier.configuration === "debug";
        const isCpp = srcFile.extension === a`.cpp`;
        const compiler = isCpp ? gxxTool : gccTool;
        const outDir = Context.getNewOutputDirectory(compiler.exe.name);
        const objFile = p`${outDir}/${srcFile.name.changeExtension(".o")}`;
        const result = Transformer.execute({
            tool: compiler,
            workingDirectory: outDir,
            dependencies: headers,
            arguments: [
                Cmd.argument(Artifact.input(srcFile)),
                Cmd.option("-o", Artifact.output(objFile)),
                Cmd.argument("-c"),
                Cmd.argument("-fPIC"),
                Cmd.options("-I", incDirs.map(Artifact.none)),
                Cmd.options("-D", defines),
                Cmd.option("-D", isDebug ? "_DEBUG" : "_NDEBUG"),
                Cmd.option("-O", isDebug ? "g" : "3"),
                ...addIf(isDebug, Cmd.argument("-g")),
                ...addIf(isCpp, Cmd.argument("--std=c++17"))
            ]
        });
        return result.getOutputFile(objFile);
    }

    function link(name: PathAtom, compiler: Transformer.ToolDefinition, objs: DerivedFile[], libs: string[]): DerivedFile {
        if (!isLinux) return undefined;

        const isLib = name.extension === a`.so`;
        const outDir = Context.getNewOutputDirectory(compiler.exe.name);
        const outFile = p`${outDir}/${name}`;
        const result = Transformer.execute({
            tool: compiler,
            workingDirectory: outDir,
            arguments: [
                ...addIf(isLib, Cmd.argument("-shared")),
                Cmd.args(objs.map(Artifact.input)),
                Cmd.option("-o", Artifact.output(outFile)),
                Cmd.options("-l", libs)
            ]
        });
        return result.getOutputFile(outFile);
    }

    function compilerTool(compilerName: string) : Transformer.ToolDefinition {
        if (!isLinux) return undefined;

        return {
            exe: f`/usr/bin/${compilerName}`,
            dependsOnCurrentHostOSDirectories: true,
            prepareTempDirectory: true,
            untrackedDirectoryScopes: [ d`/lib` ],
            untrackedFiles: []
        };
    }
}
