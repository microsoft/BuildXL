// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed    from "Sdk.Managed";
import * as Ilc        from "Sdk.Managed.Tools.ILCompiler";
import * as Frameworks from "Sdk.Managed.Frameworks";
import * as Shared     from "Sdk.Managed.Shared";

export namespace CoreRT {
    export declare const qualifier: Shared.TargetFrameworks.All;

    @@public
    export interface NativeExecutableResult extends Shared.Assembly {
        getExecutable: () => File
    }

    @@public
    export function compileToNative(asm: Shared.Assembly): NativeExecutableResult {
        /** Compile to native object file */
        const referencesClosure = Managed.Helpers.computeTransitiveClosure(asm.references, /*compile*/ false);
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
}
