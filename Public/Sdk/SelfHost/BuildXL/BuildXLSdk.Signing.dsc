// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Shared from "Sdk.Managed.Shared";
import * as BinarySigner from "BuildXL.Tools.BinarySigner";
import * as Managed from "Sdk.Managed";

namespace Signing {
    export declare const qualifier: {};
    
    /** Build a signed assembly */
    @@public
    export function esrpSignAssembly(assemblyResult: Managed.Assembly) : Managed.Assembly {
        if (!Environment.getFlag("ENABLE_ESRP")){
            return assemblyResult;
        }

        let modifiedAssembly = assemblyResult.runtime.override<Managed.Binary>({
            binary: esrpSignFile(assemblyResult.runtime.binary)
        });
        return assemblyResult.override<Managed.Assembly>({runtime : modifiedAssembly});
    }

    /**
     * Request Binary signature for a given file via ESRPClient
     */
    @@public
    export function esrpSignFile(file: File) : File { 
        Contract.requires(
            file !== undefined,
            "BuildXLSdk.esrpSignFile file argument must not be undefined."
        );

        if (!Environment.getFlag("ENABLE_ESRP")){
            return file;
        }

        // A local esrpSignFileInfoTemplate can be introduced for specific applications of signing tool
        let signInfo = BinarySigner.esrpSignFileInfoTemplate.override<BinarySigner.SignFileInfo>({file: file});
        return BinarySigner.signBinary(signInfo);
    }
}