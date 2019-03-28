// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Diagnostics {
    const propertyName = "[Sdk.Deployment]Diagnostics.enabled";

    /**
     * Provenance information.
     * This is a list of strings where each string is a friendly representation of 
     * a deployable unit, for example a managed nuget package, assembly etc.
     */
    @@public
    export type Provenance = string[];

    /**
     * Whether deployment should track diagnostic information or not.
     */
    @@public
    export const enabled : boolean = Environment.hasVariable(propertyName) && Environment.getBooleanValue(propertyName);

    @@public
    export const initialProvenance = enabled ? [] : undefined;

    /**
     * Adds a string to the provenance list
     */
    @@public
    export function chain(existinprovenance: Provenance, provenance: string) : Provenance {
        return enabled ? existinprovenance.push(provenance) : existinprovenance;
    }

 
    @@public
    export function reportDuplicateFileError(targetFile: RelativePath, sourceA: DeployedFileWithProvenance, sourceB: DeployedFileWithProvenance) : DeployedFileAction {
        let errorMessage = `Error trying to deploy both file '${sourceA.file.toDiagnosticString()}' and file '${sourceB.file.toDiagnosticString()}' to the same location: '${targetFile}'\n`;
        if (Diagnostics.enabled) {
            errorMessage += Diagnostics.printDeploymentProvenance(sourceA);
            errorMessage += Diagnostics.printDeploymentProvenance(sourceB);
        } else {
            errorMessage += `    To diagnose this error, please pass '/p:${propertyName}=true' to BuildXL`;
        }

        Contract.fail(errorMessage);
        return "takeA";
    }

    /**
     * Prints the provenance information for error messages
     */
    @@public
    export function printDeploymentProvenance(fileWithProvenance: DeployedFileWithProvenance) : string {
        let indent = 4;
        let builder = StringBuilder.create();

        builder.appendLine("");
        builder.appendLine("Reference chain for file:");

        builder.appendRepeat(" ", indent);
        builder.append(fileWithProvenance.file.toDiagnosticString());
        builder.appendLine(":");

        if (!fileWithProvenance.provenance || fileWithProvenance.provenance.length === 0) {
            // Nothing to print if there is no trace
            builder.appendLine("No information available");
        }

        for (let line of fileWithProvenance.provenance.reverse())
        {
            builder.appendRepeat(" ", indent);
            builder.appendLine(line);
            indent += 2;
        }

        return builder.toString();
    }
}
