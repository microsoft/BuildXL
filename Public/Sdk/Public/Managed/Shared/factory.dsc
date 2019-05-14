// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

namespace Factory {

    // Factory functions are qualifier agnostic
    export declare const qualifier : {};

    @@public
    export function createBinaryFromFiles(binary: File, pdb?: File, documentation?: File) : Binary {
        return {
            binary: binary,
            pdb: pdb,
            documentation: documentation,
            deploy: Deployment.flattenBinary,
        };
    }

    @@public
    export function createBinary(contents: StaticDirectory, binaryLocation: RelativePath | File) : Binary {
        const binaryPath = typeof binaryLocation === "File"
            ? (<File>binaryLocation).path
            : p`${contents.root}/${binaryLocation}`;
        const pdbPath = binaryPath.changeExtension(".pdb");
        const xmlPath = binaryPath.changeExtension(".xml");

        return {
            binary: contents.getFile(binaryPath),
            pdb: contents.hasFile(pdbPath) ? contents.getFile(pdbPath) : undefined,
            documentation: contents.hasFile(xmlPath) ? contents.getFile(xmlPath) : undefined,
            deploy: Deployment.flattenBinary,
        };
    }

    @@public
    export function createAssembly(contents: StaticDirectory, binaryLocation: RelativePath | File, targetFramework: string, references?: Reference[], isReferenceOnly?: boolean) : Assembly {
        const binary = createBinary(contents, binaryLocation);

        return {
            name: binaryLocation.nameWithoutExtension,
            targetFramework: targetFramework,
            compile: binary,
            runtime: isReferenceOnly ? undefined : binary,
            references: references,
            deploy: Deployment.flattenAssembly,
        };
    }

    @@public
    export function createNugetPackge(name: string, version: string, contents: StaticDirectory, compile: Binary[], runtime: Binary[], dependencies?: NugetPackage[]) : ManagedNugetPackage {
        // TODO: Delete this method once the new Nuget changes are merged and in LKG
        return createNugetPackage(name, version, contents, compile, runtime, dependencies);
    }

    @@public
    export function createNugetPackage(name: string, version: string, contents: StaticDirectory, compile: Binary[], runtime: Binary[], dependencies?: NugetPackage[]) : ManagedNugetPackage {
        return <ManagedNugetPackage>{
            name: name,
            version: version,
            contents: contents,
            compile: compile || [],
            runtime: runtime || [],
            dependencies: dependencies || [],
            deploy: Deployment.flattenPackage,
        };
    }

    @@public
    export function createTool(tool: Transformer.ToolDefinition) : Transformer.ToolDefinition {

        const toolDefault = {
            prepareTempDirectory: true,
        };

        const osToolDefault = Context.getCurrentHost().os === "win" ? {
            dependsOnWindowsDirectories: true,
            untrackedDirectoryScopes: [
                d`${Context.getMount("ProgramData").path}/microsoft/netFramework/breadcrumbStore`
            ]

        } : {
        };

        return Object.merge<Transformer.ToolDefinition>(
            toolDefault,
            osToolDefault,
            tool
        );
    }
}
