// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// Foreign functions can only be implemented in the prelude as ambients.
// We want to have these defined in other modules. So the temporary workaround is
// to expose the namespace in the prelude with a name that users won't use directly.

namespace _PreludeAmbientHack_Testing {
    export declare function setBuildParameter(key: string, value: string) : void;
    export declare function removeBuildParameter(key: string) : void;
    export declare function setMountPoint(mount: Mount) : void;
    export declare function removeMountPoint(name: string) : void;
    export declare function expectFailure(func: () => void, ...expectedMessages: (string | ExpectedMessage)[]) : void;
    export declare function dontValidatePips() : void;

    export interface ExpectedMessage {
        code: number;
        content: string;
    }
}

namespace _PreludeAmbientHack_Assert {
    export declare function fail(message: string) : void;
    export declare function isTrue(condition: boolean, message?: string) : void;
    export declare function isFalse(condition: boolean, message?: string) : void;
    export declare function areEqual<T>(left:T, right: T, message?: string) : void;
    export declare function notEqual<T>(left:T, right: T, message?: string) : void;
}

namespace _PreludeAmbientHack_KeyForm {
    export declare function getKeyForm(
        keyFormDll: File,
        arch: string,
        name: string,
        version: string,
        publicKeyToken: string,
        versionScope?: string,
        culture?: string,
        buildType?: string)
        : string;
}

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
    export declare function write<T extends Object>(destinationFile: Path, data: T, quoteChar?: "'" | "\"", tags?: string[], description?: string): File;
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
    export declare function writeData(destinationPathOrArgs: (Path | Object), content: any, tags?: string[], description?: string): DerivedFile;
    export declare function writeAllLines(destinationPathOrArgs: (Path | Object), contents: any[], tags?: string[], description?: string): DerivedFile;
    export declare function writeAllText(destinationPathOrArgs: (Path | Object), content: string, tags?: string[], description?: string): DerivedFile;
    
    export declare function sealDirectory(rootOrArgs: (Directory | Object), files: File[], tags?: string[], description?: string, scrub?: boolean): FullStaticContentDirectory;
    export declare function sealSourceDirectory(rootOrArgs: (Directory | Object), option?: number, tags?: string[], description?: string, patterns?: string[]): SourceDirectory;
    export declare function sealPartialDirectory(rootOrArgs: (Directory | Object), files: File[], tags?: string[], description?: string): PartialStaticContentDirectory;
    export declare function composeSharedOpaqueDirectories(rootOrArgs: (Directory | Object), directories: SharedOpaqueDirectory[]): SharedOpaqueDirectory;

    export declare function readPipGraphFragment(name: string, file: SourceFile, dependencyNames: string[]): string;
}
