// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <reference path="Prelude.Core.dsc"/>
/// <reference path="Prelude.IO.dsc"/>

namespace Context {
    /** Returns the namespace of the current global variable being evaluated. */
    export declare function getLastActiveUseNamespace(): string;

    /** Returns the fully-qualified name of the current global variable being evaluated. */
    export declare function getLastActiveUseName(): string;

    /** Returns the current Module name of the current const being evaluated. */
    export declare function getLastActiveUseModuleName(): string;
    
    /** Returns the current DScript spec file being evaluated. */
    export declare function getLastActiveUsePath(): Path;

    /** Returns a new output directory (where, for example, a tool may write its results). */
    export declare function getNewOutputDirectory(hint: PathAtomOrString): Directory;

    /** Returns a new temporary directory. */
    export declare function getTempDirectory(hint: PathAtomOrString): Directory;

    /** Whether the current execution is running on Windows. */
    export declare function getCurrentHost(): CurrentHostInformation;

    /** Whether the current execution is running on Windows. */
    export declare function isWindowsOS(): boolean;

    /** Returns the mount associated with the given name, if one is defined; throws an error otherwise. */
    export declare function getMount(mountName: string): Mount;

    /** Returns whether a mount point with a given name is defined. */
    export declare function hasMount(mountName: string): boolean;

    /** Returns current user's home directory. */
    export declare function getUserHomeDirectory(): Directory;

    /** Returns current DScript spec file. (same as getLastActiveUsePath, except the result type is File) */
    export declare function getSpecFile(): File;

    /** Returns current DScript spec file directory. */
    export declare function getSpecFileDirectory(): Directory;

    /** 
     * Returns the BuildXL bin directory. This legacy name will be deprecated in favor of
     * getBuildEngineDirectory(); see the description of that method for more complete notes.
     */
    export declare function getBuildEngineDirectory(): Directory;
    export declare function getDominoBinDirectory(): Directory;

    /** 
     * Returns the BuildXL bin directory. 
     *
     * BuildXL bin directory is the directory of the executing bxl.exe. If server
     * mode is off, then the BuildXL bin directory is the directory where the user specified bxl.exe lives. 
     * However, if server mode is on, then the BuildXL bin directory is the BuildXL server deployment cache.
     *
     * BuildXL bin directory can be obtained by querying the mount table with "BuildXLBinPath" 
     * as the key, i.e., Context.getMount("BuildXLBinPath"). But such a query only works on
     * non-configuration file. On interpreting the configuration file, config.dsc, the engine
     * that carries the mount table has not been initialized. Thus, querying the mount table
     * will fail. This method can be used to get the bin directory during configuration parsing
     * and interpretation.
     *
     * This method is particularly introduced as a remedy of the issue concerning untracked
     * executables when users are consuming the released BuildSL SDK. Let us call the bin directory where 
     * the user specified bxl.exe lives the client bin directory, and call the server deployment cache
     * the server bin directory. Since users are not aware of the server bin directory, to consume the SDK,
     * users create a resolver pointing to the SDK that resides underneath the client bin directory. 
     * When the tools inside the SDK are launched, although those tools live in the client bin directory,
     * these tools are are marked as untracked because they do not live under any mount point. 
     * The expected mount point, BuildXLBinPath, actually points to the server bin directory.
     */
    export declare function getBuildEngineDirectory(): Directory;

    /**
     * Returns the template object captured by the last top-level const invocation. Returns the empty object
     * if no template was in scope.
     */
    export declare function getTemplate<T>(): T;

    /**
     * Represents a mount point, defined by a name and a root path.  All mount
     * names and root paths must be unique within a single BuildXL execution.
     */
    interface Mount {
        /** The name of the mount. */
        name: PathAtom;

        /** The path where the mount starts. */
        path: Path; // TODO: change to Directory
    }

    /**
     * Exposes information about the current host that is running the build.
     */
    export interface CurrentHostInformation {
        /** The current Os type */
        os: OsType;
        /** The current cpu architecture  */
        cpuArchitecture: "x64" | "x86";
        /** Wheter the current build is run with elevated permissions (admin/sudo) */
        isElevated: boolean;
    }
}
