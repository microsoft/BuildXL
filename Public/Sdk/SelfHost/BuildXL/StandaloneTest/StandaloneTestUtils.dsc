// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

namespace StandaloneTestUtils {
    export declare const qualifier: {};

    const deploymentEnvironmentVariableName = "[Sdk.BuildXL]DeployStandaloneTest";
    
    @@public
    export const shouldDeployStandaloneTest: boolean = 
        Environment.hasVariable(deploymentEnvironmentVariableName)
        && (Environment.getBooleanValue(deploymentEnvironmentVariableName) === true);

    export function quoteString(str: string): string {
        return '"' + str.replace('"', '\\"') + '"';
    }

    export function renderFileLiteral(file: string): string {
        if (file === undefined) return "undefined";
        return "f`" + file + "`";
    }

    export function generateArrayProperty(propertyName: string, literals: string[], indent: string, func?: (s: string) => string): string {
        if (!literals) return "";
        
        func = func || (x => x);

        return [
            `${indent}${propertyName}: [`,
            ...literals.map(str => `${indent}${indent}${func(str)},`),
            `${indent}],`
        ].join("\n");
    }

    export function createModuleConfig(name: string, projectFiles?: string[]): string {
        return [
            'module({',
            `    name: "${name}",`,
            '    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,',
            generateArrayProperty("projects", projectFiles, "    ", renderFileLiteral),
            '});'
        ].join("\n");
    }

    export function writeFile(fileName: PathAtom, content: string): DerivedFile {
        const data: Transformer.Data = {contents: [content]};
        const destination = p`${Context.getNewOutputDirectory("BuildXLStandaloneTest")}/${fileName}`;
        return Transformer.writeData(destination, data);
    }
}