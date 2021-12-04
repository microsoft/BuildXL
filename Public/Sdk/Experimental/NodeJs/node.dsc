// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";
import { Npm } from "Sdk.JavaScript";

namespace Node {

    @@public
    export const tool : Transformer.ToolDefinition = getNodeTool();
    
    @@public
    export const npmCli : File = getNpmCli();

    const nodeExecutablesDir : Directory = d`${getNodeTool().exe.parent}`;

    /**
     * Self-contained node executables. Platform dependent.
     */
    @@public
    export const nodeExecutables : Deployment.Deployable = getNodeExecutables();

    function getNodeExecutables() : Deployment.Deployable {
        const nodePackage = getNodePackage();
        const relativePath = nodePackage.root.path.getRelative(nodeExecutablesDir.path);

        return Deployment.createDeployableOpaqueSubDirectory(
            <OpaqueDirectory>nodePackage, relativePath);
    }

    @@public
    export function run(args: Transformer.ExecuteArguments) : Transformer.ExecuteResult {
        // Node code can access any of the following user specific environment variables.
        const userSpecificEnvrionmentVariables = [
            "APPDATA",
            "LOCALAPPDATA",
            "USERPROFILE",
            "USERNAME",
            "HOMEDRIVE",
            "HOMEPATH",
            "INTERNETCACHE",
            "INTERNETHISTORY",
            "INETCOOKIES",
            "LOCALLOW",
        ];
        const execArgs = Object.merge<Transformer.ExecuteArguments>(
            {
                tool: tool,
                workingDirectory: tool.exe.parent,
                unsafe: {
                    passThroughEnvironmentVariables: userSpecificEnvrionmentVariables
                }
            },
            args
        );

        return Transformer.execute(execArgs);
    }

    const nodeVersion = "v17.2.0";
    const nodeWinDir = `node-${nodeVersion}-win-x64`;
    const nodeOsxDir = `node-${nodeVersion}-darwin-x64`;
    const nodeLinuxDir = `node-${nodeVersion}-linux-x64`;

    function getNodePackage(): OpaqueDirectory {
        const host = Context.getCurrentHost();
    
        Contract.assert(host.cpuArchitecture === "x64", "Only 64bit versions supported.");
    
        let pkgContents : OpaqueDirectory = undefined;
        
        switch (host.os) {
            case "win":
                pkgContents = <OpaqueDirectory>importFrom("NodeJs.win-x64").extracted;
                break;
            case "macOS": 
                pkgContents = <OpaqueDirectory>importFrom("NodeJs.osx-x64").extracted;
                break;
            case "unix": 
                pkgContents = <OpaqueDirectory>importFrom("NodeJs.linux-x64").extracted;
                break;
            default:
                Contract.fail(`The current NodeJs package doesn't support the current OS: ${host.os}. Ensure you run on a supported OS -or- update the NodeJs package to have the version embedded.`);
        }

        return pkgContents;
    }
    
    function getNodeTool() : Transformer.ToolDefinition {
        const host = Context.getCurrentHost();
    
        Contract.assert(host.cpuArchitecture === "x64", "Only 64bit versions supported.");
    
        let executable : RelativePath = undefined;
        let pkgContents : OpaqueDirectory = getNodePackage();
        
        switch (host.os) {
            case "win":
                executable = r`${nodeWinDir}/node.exe`;
                break;
            case "macOS": 
                executable = r`${nodeOsxDir}/bin/node`;
                break;
            case "unix": 
                executable = r`${nodeLinuxDir}/bin/node`;
                break;
            default:
                Contract.fail(`The current NodeJs package doesn't support the current OS: ${host.os}. Esure you run on a supported OS -or- update the NodeJs package to have the version embdded.`);
        }
  
        return {
            exe: pkgContents.assertExistence(executable),
            runtimeDirectoryDependencies: [
                pkgContents,
            ],
            prepareTempDirectory: true,
            dependsOnCurrentHostOSDirectories: true,
            dependsOnAppDataDirectory: true,
        };
    }

    function getNpmCli() : File {
        const host = Context.getCurrentHost();
    
        Contract.assert(host.cpuArchitecture === "x64", "Only 64bit versions supported.");
    
        let executable : RelativePath = undefined;
        let pkgContents : StaticDirectory = undefined;
        
        switch (host.os) {
            case "win":
                pkgContents = importFrom("NodeJs.win-x64").extracted;
                executable = r`${nodeWinDir}/node_modules/npm/bin/npm-cli.js`;
                break;
            case "macOS": 
                pkgContents = importFrom("NodeJs.osx-x64").extracted;
                executable = r`${nodeOsxDir}/lib/node_modules/npm/bin/npm-cli.js`;
                break;
            case "unix": 
                pkgContents = importFrom("NodeJs.linux-x64").extracted;
                executable = r`${nodeLinuxDir}/lib/node_modules/npm/bin/npm-cli.js`;
                break;
            default:
                Contract.fail(`The current NodeJs package doesn't support the current OS: ${host.os}. Ensure you run on a supported OS -or- update the NodeJs package to have the version embedded.`);
        }

        return pkgContents.assertExistence(executable);
    }

    /**
     * The tool definition representing npm
     */
    @@public
    export function getNpmTool() : Transformer.ToolDefinition {
        const host = Context.getCurrentHost();
        let executable : RelativePath = undefined;

        switch (host.os) {
            case "win":
                executable = r`${nodeWinDir}/npm.cmd`;
                break;
            case "macOS": 
                executable = r`${nodeOsxDir}/bin/npm`;
                break;
            case "unix": 
                executable = r`${nodeLinuxDir}/bin/npm`;
                break;
            default:
                Contract.fail(`The current NodeJs package doesn't support the current OS: ${host.os}. Ensure you run on a supported OS -or- update the NodeJs package to have the version embedded.`);
        }
       
        return Npm.getNpmTool(getNodePackage(), executable);
    }

    @@public 
    export function runNpmInstall(
        targetFolder: Directory, 
        dependencies: (File | StaticDirectory)[]) : SharedOpaqueDirectory {
        
        return Npm.runNpmInstall({
            nodeTool: tool,
            npmTool: tool,
            additionalArguments: [Cmd.argument(Artifact.input(npmCli))],
            targetFolder: targetFolder,
            additionalDependencies: dependencies,
            noBinLinks: true,
            userNpmrcLocation: "local",
            globalNpmrcLocation: "local"
            });
    }

    @@public 
    export function runNpmPackageInstall(
        targetFolder: Directory, 
        dependencies: (File | StaticDirectory)[], 
        package: {name: string, version: string}) : SharedOpaqueDirectory {
        
        const nodeModules = d`${targetFolder}/node_modules`;

        const result = Npm.runNpmInstallWithAdditionalOutputs({
            nodeTool: tool,
            npmTool: tool,
            additionalArguments: [Cmd.argument(Artifact.input(npmCli))],
            package: package,
            targetFolder: targetFolder,
            additionalDependencies: dependencies,
            noBinLinks: true,
            userNpmrcLocation: "local",
            globalNpmrcLocation: "local"}, 
            [nodeModules]);
        
            return <SharedOpaqueDirectory> result.getOutputDirectory(nodeModules);
    }

    @@public
    export function tscCompile(workingDirectory: Directory, dependencies: Transformer.InputArtifact[]) : SharedOpaqueDirectory {
        const outPath = d`${workingDirectory}/out`;
        const arguments: Argument[] = [
            Cmd.argument(Artifact.none(f`${workingDirectory}/node_modules/typescript/lib/tsc.js`)),
            Cmd.argument("-p"),
            Cmd.argument("."),
        ];

        const result = Node.run({
            arguments: arguments,
            workingDirectory: workingDirectory,
            dependencies: dependencies,
            outputs: [
                { directory: outPath, kind: "shared" }
            ]
        });

        return <SharedOpaqueDirectory>result.getOutputDirectory(outPath);
    }

    @@public 
    export interface Arguments {
        /** Static directories containing all the TypeScript files that need to be built. */
        sources: StaticDirectory[];

        /** Dependencies needed for running npm install */
        npmDependencies?: Transformer.InputArtifact[];

        /** Dependencies needed for compiling TypeScript sources */
        dependencies?: Transformer.InputArtifact[];
    }

    /**
     * Builds a collection of TypeScript files and produces a self-contained opaque directory that includes the compilation
     * result and all the required node_modules dependencies
     */
    @@public
    export function tscBuild(args: Arguments) : OpaqueDirectory {
        
        const sources = args.sources || [];

        let displayName : PathAtom = sources.length === 0 ?
            // Unlikely this will be an empty array, but let's be conservative
            a`${Context.getLastActiveUseModuleName()}` :
            sources[0].root.name;

        // Copy all the sources to an output directory so we don't polute the source tree with outputs
        const outputDir = Context.getNewOutputDirectory(a`node-build-${displayName}`);
        const srcCopies = sources.map(source => Deployment.copyDirectory(
            source.root, 
            outputDir, 
            source));

        const srcCopy: SharedOpaqueDirectory = Transformer.composeSharedOpaqueDirectories(outputDir, srcCopies);

        // Install required npm packages
        const npmInstall = runNpmInstall(srcCopy.root, [srcCopy, ...(args.npmDependencies || []), ...srcCopies]);

        // Compile
        const compileOutDir: SharedOpaqueDirectory = Node.tscCompile(
            srcCopy.root, 
            [ srcCopy, npmInstall, ...(args.dependencies || []) ]);

        const outDir = Transformer.composeSharedOpaqueDirectories(
            outputDir, 
            [compileOutDir]);

        const nodeModules = Deployment.createDeployableOpaqueSubDirectory(npmInstall, r`node_modules`);
        const out = Deployment.createDeployableOpaqueSubDirectory(outDir, r`out`);

        // The deployment also needs all node_modules folder that npm installed
        // This is the final layout the tool needs
        const privateDeployment : Deployment.Definition = {
            contents: [
                out,
                {
                    contents: [{subfolder: `node_modules`, contents: [nodeModules]}]
                }
            ]
        };
        
        // We need to create a single shared opaque that contains the full layout
        const sourceDeployment : Directory = Context.getNewOutputDirectory(a`private-deployment-${displayName}`);
        const onDiskDeployment = Deployment.deployToDisk({definition: privateDeployment, targetDirectory: sourceDeployment, sealPartialWithoutScrubbing: true});

        const finalOutput : SharedOpaqueDirectory = Deployment.copyDirectory(
            sourceDeployment, 
            Context.getNewOutputDirectory(a`output-${displayName}`),
            onDiskDeployment.contents,
            onDiskDeployment.targetOpaques);

        return finalOutput;
    }
}