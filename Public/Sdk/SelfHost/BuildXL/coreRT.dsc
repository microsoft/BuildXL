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

interface NativeExecutableResult extends Shared.Assembly {
    getExecutable: () => File
}

@@public
export function nativeExecutable(args: Arguments): NativeExecutableResult {
    if (Context.getCurrentHost().os !== "macOS") {
        const asm = executable(args);
        return asm.override<NativeExecutableResult>({
            getExecutable: () => asm.runtime.binary
        });
    }

    /** Override framework.applicationDeploymentStyle to make sure we don't use apphost */
    args = args.override<Arguments>({
        framework: (args.framework || Frameworks.framework).override<Shared.Framework>({
            applicationDeploymentStyle: "frameworkDependent"
        })
    });

    /** Compile to MSIL */
    const asm = executable(args);

    /** Compile to native object file */
    const referencesClosure = Managed.Helpers.computeTransitiveReferenceClosure(args.framework, asm.references, false);
    const ilcResult = Ilc.compile({
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

    /** Link native executable */
    const nativeExecutable = Clang.compile(<Clang.Arguments>{
        out: asm.name,
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
    });

    /** Set the nativeExecutable property in the produced assembly */
    return asm.override<NativeExecutableResult>({
        nativeExecutable: nativeExecutable,
        getExecutable: () => nativeExecutable
    });
}
