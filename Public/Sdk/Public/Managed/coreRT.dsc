// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Clang      from "Sdk.Clang";
import * as Ilc        from "Sdk.Managed.Tools.ILCompiler";
import * as Frameworks from "Sdk.Managed.Frameworks";
import * as Shared     from "Sdk.Managed.Shared";

@@public
export function nativeExecutable(args: Arguments): DerivedFile {
    /** Override framework.applicationDeploymentStyle to make sure we don't use apphost */
    const exeArgs = args.override<Arguments>({
        framework: (args.framework || Frameworks.framework).override<Shared.Framework>({
            applicationDeploymentStyle: "frameworkDependent"
        })
    });

    /** Compile to MSIL */
    const exeResult = executable(exeArgs);

    /** Compile to native object file */
    const userIlcArgs = <Ilc.Arguments>((args.tools && args.tools.ilc) || {});
    const ilcArgs = Object.merge(userIlcArgs, <Ilc.Arguments>{
        out: `${args.assemblyName}.o`,
        inputs: [ 
            exeResult.runtime.binary
        ],
        dependencies: [
            exeResult.runtime.pdb
        ]
    });
    const ilcResult = Ilc.compile(ilcArgs);

    /** Link native */
    const linkArgs = <Clang.Arguments>{
        out: args.assemblyName,
        inputs: [
            ilcResult.binary,
            ...Ilc.linkTimeLibraries,
        ],
        emitDebugInformation: true,
        linkerArgs: [
            "-rpath",
            "'$ORIGIN'"
        ],
        frameworks: [
            "CoreFoundation",
            "Security",
            "GSS"
        ],
        libraries: [
            "stdc++",
            "dl",
            "m",
            "curl",
            "z",
            "icucore"
        ]
    };
    
    return Clang.compile(linkArgs);
}
