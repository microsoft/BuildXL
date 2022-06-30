// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Shared from "Sdk.Managed.Shared";
import * as BinarySigner from "Sdk.Managed.Tools.BinarySigner";
import * as Managed from "Sdk.Managed";

namespace Signing {
    export declare const qualifier: {};
    
    /** Build a signed assembly */
    @@public
    export function esrpSignAssembly(signArgs: BinarySigner.ESRPSignArguments, assemblyResult: Managed.Assembly) : Managed.Assembly {
        let signedRuntime = assemblyResult.runtime.override<Managed.Binary>({
            binary: esrpSignFile(signArgs, assemblyResult.runtime.binary)
        });
        return assemblyResult.override<Managed.Assembly>({
            runtime : signedRuntime
        });
    }

    /**
     * Request Binary signature for a given file via ESRPClient
     */
    @@public
    export function esrpSignFile(signArgs: BinarySigner.ESRPSignArguments, file: File) : File { 
        Contract.requires(
            file !== undefined,
            "BuildXLSdk.esrpSignFile file argument must not be undefined."
        );

        // A local esrpSignFileInfoTemplate can be introduced for specific applications of signing tool
        let signInfo = BinarySigner.esrpSignFileInfoTemplate(signArgs).override<BinarySigner.SignFileInfo>({file: file});
        return BinarySigner.signBinary(signArgs, signInfo);
    }
}