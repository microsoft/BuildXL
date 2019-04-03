// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace _PreludeAmbientHack_Hashing{
        /** Returns the SHA 256 of content */
        export declare function sha256(content: string): string;
}

namespace _PreludeAmbientHack_ValueCache {
    /** Returns an object from the cache for a given key. */
    export declare function getOrAdd<TKey, TValue>(key: TKey, createValue: () => TValue): TValue;
}

namespace _PreludeAmbientHack_Json{
    /** Writes an object as Json */
    export declare function write<T extends Object>(destinationFile: Path, data: T): File;
}

namespace _PreludeAmbientHack_Xml {
    export declare function read(xmlFile: SourceFile): Object;
    export declare function write(destinationFile: Path, doc: Object, options?: Object, tags?: string[], description?: string): File
}

namespace _PreludeAmbientHack_Transformer {
    interface SharedOpaqueDirectory extends StaticDirectory {
        kind: "shared"
    }

    interface ExclusiveOpaqueDirectory extends StaticDirectory {
        kind: "exclusive"
    }

    interface SourceTopDirectory extends StaticDirectory {
        kind: "sourceTopDirectories"
    }

    interface SourceAllDirectory extends StaticDirectory {
        kind: "sourceAllDirectories"
    }

    interface FullStaticContentDirectory extends StaticDirectory {
        kind: "full"
    }

    interface PartialStaticContentDirectory extends StaticDirectory {
        kind: "partial"
    }

    type SourceDirectory = SourceTopDirectory | SourceAllDirectory;

    export declare function execute(args: Object): Object;
    export declare function ipcSend(args: Object): Object;
    export declare function createService(args: Object): Object;
    export declare function getNewIpcMoniker(): IpcMoniker;
    export declare function getIpcServerMoniker(): IpcMoniker;
    export declare function getDominoIpcServerMoniker(): IpcMoniker;
    export declare function copyFile(sourceFile: File, destinationFile: Path, tags?: string[], description?: string, keepOutputsWritable?: boolean): DerivedFile;
    export declare function writeFile(destinationFile: Path, content: any, tags?: string[], separator?: string, description?: string): DerivedFile;
    export declare function writeData(destinationFile: Path, content: any, tags?: string[], description?: string): DerivedFile;
    export declare function writeAllLines(destinationFile: Path, contents: any[], tags?: string[], description?: string): DerivedFile;
    export declare function writeAllText(destinationFile: Path, content: string, tags?: string[], description?: string): DerivedFile;
    export declare function sealDirectory(root: Directory, files: File[], tags?: string[], description?: string, scrub?: boolean): FullStaticContentDirectory;
    export declare function sealSourceDirectory(root: Directory, option?: number, tags?: string[], description?: string, patterns?: string[]): SourceDirectory;
    export declare function sealPartialDirectory(root: Directory, files: File[], tags?: string[], description?: string): PartialStaticContentDirectory;
    export declare function composeSharedOpaqueDirectories(root: Directory, directories: SharedOpaqueDirectory[]): SharedOpaqueDirectory;
}
