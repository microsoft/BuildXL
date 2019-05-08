// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Clang      from "Sdk.Clang";
import * as Managed    from "Sdk.Managed";
import * as Ilc        from "Sdk.Managed.Tools.ILCompiler";
import * as Frameworks from "Sdk.Managed.Frameworks";
import * as Shared     from "Sdk.Managed.Shared";

function ilcCompile(framework: Shared.Framework, asm: Shared.Assembly) {
    const referencesClosure = Managed.Helpers.computeTransitiveReferenceClosure(framework, asm.references, false);

    return Ilc.compile({
        out: `${asm.name}.o`,
        inputs: [ 
            asm.runtime.binary
        ],
        references: [
            ...referencesClosure.map(r => r.binary)
        ],
        dependencies: [
            asm.runtime.pdb,
            ...referencesClosure.map(r => r.pdb)
        ]
    });
}

@@public
export function nativeExecutable(args: Arguments): Result {
    if (Context.getCurrentHost().os !== "macOS") {
        return executable(args);
    }

    /** Override framework.applicationDeploymentStyle to make sure we don't use apphost */
    args = args.override<Arguments>({
        framework: (args.framework || Frameworks.framework).override<Shared.Framework>({
            applicationDeploymentStyle: "frameworkDependent"
        })
    });

    /** Compile to MSIL */
    const exeResult = executable(args);

    const assemblyReferences = (args.references || [])
        .filter(r => Shared.isAssembly(r))
        .map(r => <Shared.Assembly>r);

    /** Compile to native object file */
    const ilcResult = ilcCompile(args.framework, exeResult);

    /** Link native */
    const linkArgs = <Clang.Arguments>{
        out: exeResult.name,
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
    
    const nativeBinary = Clang.compile(linkArgs);

    return Object.merge<Result>(exeResult, {
        runtime: { binary: nativeBinary },
    });
}
