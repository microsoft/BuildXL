// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Shared from "Sdk.Managed.Shared";
import * as BinarySigner from "Sdk.Managed.Tools.BinarySigner";
import * as Managed from "Sdk.Managed";

namespace Signing {
    export declare const qualifier: {};

    /**
     * Create Esrp sign configuration using Environment variables
     */
    @@public
    export function createEsrpConfiguration() : EsrpSignConfiguration {
        return {
            signToolPath: p`${Environment.expandEnvironmentVariablesInString(Environment.getStringValue("SIGN_TOOL_PATH"))}`,
            signToolConfiguration: Environment.getPathValue("ESRP_SESSION_CONFIG"),
            signToolEsrpPolicy: Environment.getPathValue("ESRP_POLICY_CONFIG"),
            signToolAadAuth: p`${Context.getMount("SourceRoot").path}/Secrets/CodeSign/EsrpAuthentication.json`
        };
    }
    
    /** Build a signed assembly */
    @@public
    export function esrpSignAssembly(assemblyResult: Managed.Assembly) : Managed.Assembly {
        let signedRuntime = assemblyResult.runtime.override<Managed.Binary>({
            binary: esrpSignFile(assemblyResult.runtime.binary)
        });
        return assemblyResult.override<Managed.Assembly>({
            runtime : signedRuntime
        });
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

        let signArgs = createEsrpConfiguration().merge<BinarySigner.ESRPSignArguments>({file: file});
        return BinarySigner.signBinary(signArgs);
    }
}