// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Transformer {
    /** Seals specified root folder with a set of files; the created pip is tagged with 'tags'. */
    @@public
    export function sealDirectory(root: Directory, files: File[], tags?: string[], description?: string): FullStaticContentDirectory {
        return _PreludeAmbientHack_Transformer.sealDirectory(root, files, tags, description);
    }

    /** Seals specified root folder without the need to specify all files provided root is under a readonly mount; the created pip is tagged with 'tags'. */
    @@public
    export function sealSourceDirectory(root: Directory, option?: SealSourceDirectoryOption, tags?: string[], description?: string, patterns?: string[]): SourceDirectory {
        return _PreludeAmbientHack_Transformer.sealSourceDirectory(root, option, tags, description, patterns);
    }

    /** Seals a partial view of specified root folder with a set of files; the created pip is tagged with 'tags'. */
    @@public
    export function sealPartialDirectory(root: Directory, files: File[], tags?: string[], description?: string): PartialStaticContentDirectory {
        return _PreludeAmbientHack_Transformer.sealPartialDirectory(root, files, tags, description);
    }

    /** Creates a shared opaque directory whose content is the aggregation of a collection of shared opaque directories.
     * The root can be any arbitrary directory that is a common ancestor to all the provided directories.
     * The resulting directory behaves as any other shared opaque, and can be used as a directory dependency.
    */
    @@public
    export function composeSharedOpaqueDirectories(root: Directory, directories: SharedOpaqueDirectory[]): SharedOpaqueDirectory {
        return _PreludeAmbientHack_Transformer.composeSharedOpaqueDirectories(root, directories);
    }

    /** Options for sealing source directory. */
    @@public
    export const enum SealSourceDirectoryOption {
        /** Indicates the SealedSourceDirectory can access only files in the root folder, not recursively. */
        topDirectoryOnly = 0,
        /** Indicates the SealedSourceDirectory can access all files in all directories under the root folder, recursively. */
        allDirectories,
    }

    /** allows a partial sealed directory to be narrowed to a smaller sealed directory */
    @@public
    export function reSealPartialDirectory(dir: StaticDirectory, relativePath: RelativePath, osRestriction?: "win" | "macOS" | "unix") : PartialStaticContentDirectory {
        if ((osRestriction || Context.getCurrentHost().os) !== Context.getCurrentHost().os) return undefined;

        const subFolder = d`${dir.root}/${relativePath}`;
        return sealPartialDirectory(
            subFolder,
            dir.contents.filter(f => f.isWithin(subFolder))
        );
    }
}
