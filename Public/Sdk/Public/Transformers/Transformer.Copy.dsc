// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Transformer {

    /** Copies a file to a new destination; the created copy-pip is tagged with 'tags'. */
    @@public
    export function copyFile(sourceFile: File, destinationFile: Path, tags?: string[], description?: string, keepOutputsWritable?: boolean): DerivedFile {
        return _PreludeAmbientHack_Transformer.copyFile(sourceFile, destinationFile, tags, description, keepOutputsWritable);
    }

    /** Arguments for 'copyDirectory' */
    @@public
    export interface CopyDirectoryArguments {
        sourceDir: Directory; 
        targetDir: Directory;
        dependencies?: StaticDirectory[];
        pattern?: string;
        recursive?: boolean;
        keepOutputsWritable?: boolean
    }
    
    /**
     * Based on the current platform schedules either a robocopy.exe or rsync pip to copy 'sourceDir' to 'targetDir'.
     * That pip takes a dependency on `sourceDirDep` and, optionally, on a collection of opaque directories.  
     * If 'sourceDir' is not within `sourceDirDep.root`, disallowed file accesses are almost certain to happen. opaqueDirDeps
     * allows for the case where there are opaque directories under the given root, which is sometimes the case of a deployment on disk
     */
    @@public
    export function copyDirectory(arguments: CopyDirectoryArguments): SharedOpaqueDirectory {
        const args: Transformer.ExecuteArguments = Context.getCurrentHost().os === "win"
            ? <Transformer.ExecuteArguments>{
                tool: {
                    exe: f`${Context.getMount("Windows").path}/System32/Robocopy.exe`,
                    dependsOnWindowsDirectories: true,
                    description: "Copy Directory",
                },
                workingDirectory: arguments.targetDir,

                // source: https://support.microsoft.com/en-us/help/954404/return-codes-that-are-used-by-the-robocopy-utility-in-windows-server-2
                successExitCodes: [ 0, 1, 2, 3, 4, 5, 6, 7 ],

                arguments: [
                    Cmd.argument(Artifact.none(arguments.sourceDir)),
                    Cmd.argument(Artifact.none(arguments.targetDir)),
                    Cmd.argument(arguments.pattern || "*.*"),
                    Cmd.flag("/E", arguments.recursive !== false),   // Copy subdirectories including empty ones (but no /PURGE, i.e., don't delete dest files that no longer exist)
                    Cmd.argument("/NJH"), // No Job Header
                    Cmd.argument("/NFL"), // No File list reducing stdout processing
                    Cmd.argument("/NP"),  // Don't show per-file progress counter
                    // TODO: Enable multi-threaded.
                    //       Currently this can cause missing files in the target directory; mainly happens during NPM install pips.
                    // Cmd.argument("/MT"),  // Multi threaded
                ],
                dependencies: arguments.dependencies || [],
                outputs: [
                    { directory: arguments.targetDir, kind: "shared" }
                ],
                keepOutputsWritable: arguments.keepOutputsWritable
            }
            : <Transformer.ExecuteArguments>{
                tool: {
                    exe: f`/usr/bin/rsync`,
                    description: "Copy Directory",
                    dependsOnCurrentHostOSDirectories: true,
                    prepareTempDirectory: true
                },
                workingDirectory: arguments.targetDir,
                arguments: [
                    Cmd.argument(arguments.recursive === false ? "-avh" : "-arvh"),
                    Cmd.option("--include ", arguments.pattern),
                    Cmd.argument(Cmd.join("", [ Artifact.none(arguments.sourceDir), '/' ])),
                    Cmd.argument(Artifact.none(arguments.targetDir)),
                ],
                dependencies: arguments.dependencies || [],
                outputs: [
                    { directory: arguments.targetDir, kind: "shared" }
                ],
                keepOutputsWritable: arguments.keepOutputsWritable
            };

        const result = Transformer.execute(args);
        return <SharedOpaqueDirectory>result.getOutputDirectory(arguments.targetDir);
    }
}
