// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Cmd, Artifact, Transformer} from "Sdk.Transformers";
import * as Native from "Sdk.Linux.Native";

namespace Sandbox {
    export declare const qualifier : {
        configuration: "debug" | "release",
        targetRuntime: "linux-x64"
    };

    const commonSrc = [
        ...glob(d`../MacOs/Interop/Sandbox/Data`, "*.cpp"),
        ...glob(d`../MacOs/Interop/Sandbox/Handlers`, "*.cpp"),
        f`../MacOs/Interop/Sandbox/Sandbox.cpp`,
        f`../MacOs/Sandbox/Src/Kauth/Checkers.cpp`,
        f`../MacOs/Sandbox/Src/Kauth/OpNames.cpp`,
        f`../Windows/DetoursServices/PolicyResult_common.cpp`,
        f`../Windows/DetoursServices/PolicySearch.cpp`,
        f`../Windows/DetoursServices/StringOperations.cpp`,
        f`../Windows/DetoursServices/FilesCheckedForAccess.cpp`,
        f`../Common/FileAccessManifest.cpp`
    ];
    const utilsSrc   = [ f`utils.c` ];
    const bxlEnvSrc  = [ f`bxl-env.c` ];
    const auditSrc   = [ f`bxl_observer.cpp`, f`audit.cpp`, f`observer_utilities.cpp` ];
    const detoursSrc = [ f`bxl_observer.cpp`, f`detours.cpp`, f`PTraceSandbox.cpp`, f`observer_utilities.cpp` ];
    const ptraceRunnerSrc = [ f`ptracerunner.cpp`, f`bxl_observer.cpp`, f`PTraceSandbox.cpp`, f`observer_utilities.cpp` ];
    const incDirs    = [
        d`./`,
        d`../MacOs/Interop/Sandbox`,
        d`../MacOs/Interop/Sandbox/Data`,
        d`../MacOs/Interop/Sandbox/Handlers`,
        d`../MacOs/Sandbox/Src`,
        d`../MacOs/Sandbox/Src/FileAccessManifest`,
        d`../MacOs/Sandbox/Src/Kauth`,
        d`../Windows/DetoursServices`,
        d`../Common`
    ];
    const headers = incDirs.mapMany(d => ["*.h", "*.hpp"].mapMany(q => glob(d, q)));

    function compile(sourceFile: SourceFile) {
        return compileWithDefines(sourceFile, []);
    }

    function compileWithDefines(sourceFile: SourceFile, defines?: string[]): DerivedFile {
        const compilerArgs : Native.Linux.Compilers.CompilerArguments =  {
            defines: defines || [],
            headers: headers,
            includeDirectories: incDirs,
            sourceFile: sourceFile
        };

        return Native.Linux.Compilers.compile(compilerArgs);
    }

    export const commonObj  = commonSrc.map(compile);
    export const utilsObj   = utilsSrc.map(compile);
    export const bxlEnvObj  = bxlEnvSrc.map(compile);
    export const auditObj   = auditSrc.map(compile);
    export const detoursObj = detoursSrc.map(f => compileWithDefines(f, [ "ENABLE_INTERPOSING" ]));
    export const ptraceRunnerObj = ptraceRunnerSrc.map(f => compileWithDefines(f, [ "ENABLE_INTERPOSING" ]));

    const gccTool = Native.Linux.Compilers.gccTool;
    const gxxTool = Native.Linux.Compilers.gxxTool;

    @@public
    export const libBxlUtils = Native.Linux.Compilers.link({
        outputName: a`libBxlUtils.so`, 
        tool: gccTool, 
        objectFiles: utilsObj});

    @@public
    export const bxlEnv = Native.Linux.Compilers.link({
        outputName: a`bxl-env`, 
        tool: gccTool, 
        objectFiles: bxlEnvObj});

    @@public
    export const libBxlAudit = Native.Linux.Compilers.link({
        outputName: a`libBxlAudit.so`, 
        tool: gxxTool, 
        objectFiles: [...commonObj, ...utilsObj, ...auditObj], 
        libraries: [ "dl" ]});

    @@public
    export const libDetours = Native.Linux.Compilers.link({
        outputName: a`libDetours.so`, 
        tool: gxxTool, 
        objectFiles: [...commonObj, ...utilsObj, ...detoursObj], 
        libraries: [ "dl", "pthread" ]});

    @@public
    export const ptraceRunner = Native.Linux.Compilers.link({
        outputName: a`ptracerunner`, 
        tool: gxxTool, 
        objectFiles: [...commonObj, ...utilsObj, ...ptraceRunnerObj], 
        libraries: [ "dl", "pthread" ]});
}
