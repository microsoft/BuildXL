// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

/**
 * Result of deploying to disk.
 */
@@public
export interface OnDiskDeployment {
    /** Input data that was used for deployment */
    deployedDefinition: Definition;

    /** Static (sealed) directory that contains all the deployed files */
    contents: StaticDirectory;

    /** Optional primary file, i.e. an executable or test dll */
    primaryFile?: File;

    /** Optional opaque directories robocopied/rsynced into this deployment */
    targetOpaques?: OpaqueDirectory[];
}

/**
 * Arguments to fine tune how things are deployed to disk
 */
@@public
export interface DeployToDiskArguments {
    /** The deployment definition to lay out on disk */
    definition: Definition;

    /** The target location where the deployment definition should be deployed to. */
    targetDirectory: Directory;

    /** Optional primary file for the resulting deployment. i.e. executable or test file. */
    primaryFile?: PathFragment;

    /** Optional list of tags to tag the pips with. */
    tags?: string[];

    /** A set of options specific to the deployment. deployToDisk just dumbly passes it along to the flatten method of the Deployable interface. */
    deploymentOptions?: DeploymentOptions;
}

/**
 * Deploys a given deployment to disk
 */
@@public
export function deployToDisk(args: DeployToDiskArguments): OnDiskDeployment {
    let rootDir = args.targetDirectory || Context.getNewOutputDirectory("deployment");

    const flattened = flatten(args.definition, undefined, args.deploymentOptions);

    const targetFiles = flattened.flattenedFiles.forEach(tuple => {
        const relativeTarget = tuple[0];
        const data = tuple[1];

        const targetPath = rootDir.combine(relativeTarget);

        return Transformer.copyFile(data.file, targetPath, args.tags);
    });

    const targetOpaques = flattened.flattenedOpaques.toArray().map(tuple => {
        const relativeTarget = tuple[0];
        const opaque = tuple[1];

        const targetDir = d`${rootDir}/${relativeTarget}`;

        const args = Context.getCurrentHost().os === "win"
            ? {
                tool: {
                    exe: f`${Context.getMount("Windows").path}/System32/Robocopy.exe`,
                    dependsOnWindowsDirectories: true,
                    description: "Copy Directory",
                },
                workingDirectory: targetDir,
                successExitCodes: [
                    0,
                    1,
                    2,
                    4,
                ],
                arguments: [
                    Cmd.argument(Artifact.input(opaque)),
                    Cmd.argument(Artifact.output(targetDir)),
                    Cmd.argument("*.*"),
                    Cmd.argument("/MIR"), // Mirror the directory
                    Cmd.argument("/NJH"), // No Job Header
                    Cmd.argument("/NFL"), // No File list reducing stdout processing
                    Cmd.argument("/NP"),  // Don't show per-file progress counter
                    Cmd.argument("/MT"),  // Multi threaded
                ]
            }
            : {
                tool: {
                    exe: f`/usr/bin/rsync`,
                    description: "Copy Directory",
                },
                workingDirectory: targetDir,
                arguments: [
                    Cmd.argument("-arvh"),
                    Cmd.argument(Cmd.join("", [ Artifact.input(opaque), '/' ])),
                    Cmd.argument(Artifact.output(targetDir)),
                    Cmd.argument("--delete"),
                ]
            };

        Debug.writeLine(`=== ${args.tool.exe} ${Debug.dumpArgs(args.arguments)}`);
        const result = Transformer.execute(args);
        return result.getOutputDirectory(targetDir);
    });

    // TODO: We lack the ability to combine files and OpagueDuirecties into a new OpaqueDirectory (unless we write a single process that would do all the copies)
    // Therefore for now we'll just copy the opaques but don't make it part of the output StaticDirectory field contents
    // There is a hole here for consumers but today we only use this in selfhost in the final deployment.
    const contents = Transformer.sealPartialDirectory(rootDir, targetFiles, args.tags);

    return {
        deployedDefinition: args.definition,
        contents: contents,
        primaryFile : args.primaryFile ? contents.getFile(args.primaryFile) : undefined,
        targetOpaques: targetOpaques
    };
}

/**
 * Creates a deployment from disk by globbing the tree and constructing a definition out of it.
 * @param sourceRoot - The root of where to start from to create the deployment
 * @param patternOrOptions - The optional pattern to pass to the glob function for files. Defaults to '*'
 * @param recursive - Optionally indicates if the deployment should be crated recursively. Defaults to true.
 *
 * Remarks: The overloaded argument is for backwards compatibility. The intent is to deprecated the explicit arguments in favor of a compound options field.
 */
@@public
export function createFromDisk(sourceRoot: Directory, patternOrOptions?: (string | CreateFromDiskOptions), recursive?: boolean) : Definition {

    // Handle overload pattern
    const options : CreateFromDiskOptions = typeof patternOrOptions === "string" ? undefined : patternOrOptions;

    // Pattern needs to check the overloaded argument. Pattern defaults to '*'
    const pattern : string = typeof patternOrOptions === "string"
        ? patternOrOptions
        : options !== undefined ? options.pattern : "*";

    // Recursive prefers the excplicit argument. Recursive defaults to true
    recursive = recursive || (options !== undefined ? options.recursive : true);

    // Skip any files under excluded directories
    if (options && options.excludeDirectories) {
        if (options.excludeDirectories.indexOf(sourceRoot) >= 0) {
            return {
                contents: [],
            };
        }
    }
    
    let content : DeployableItem[] = [];
    
    let files = glob(sourceRoot, pattern);
    if (options && options.excludeFiles) {
        files = files.filter(file => !options.excludeFiles.contains(file));
    }
    content = files;

    if (recursive) {
        let directories = globFolders(sourceRoot, "*");
        for (let directory of directories) {
            const nested = createFromDisk(directory, patternOrOptions, recursive);
            const nestedWithFolder = {
                subfolder: directory.name,
                contents: [
                    nested
                ]
            };

            content = content.push(nestedWithFolder);
        }
    }

    return {
        contents: content,
    };
}

@@public
export interface CreateFromDiskOptions {
    /** Which directories to exclude */
    excludeDirectories?: Directory[],

    /** Which files to exclude */
    excludeFiles?: Set<File>,

    /** Wildcard pattern to match in each directory. */
    pattern?: string,

    /** Whether to recurse into directories or not  */
    recursive?: boolean
}