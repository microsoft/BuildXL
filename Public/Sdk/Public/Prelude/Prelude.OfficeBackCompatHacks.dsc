// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// Because when the Office SDk was written we did not follow the new recommendation of using the Transformer from "Sdk.Transforms" 
// but hardcoded it to the Prelude one which we wanted to remove we now have this hack in place that when we are building for office 
// we will include this file with the back compat apis.
// Newer office branches have been updated. 
// 
// !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
// !!!!!! DO NOT UPDATE THIS FILE EVER !!!!!! 
// IF OFFICE NEEDS A NEW API, UPDATE the Sdk.Transformers MODULE
// AND UPDATE THE CODE IN OFFICE THAT CALLS THE API TO IMPORT Transformers namespace
// FROM Sdk.Transformers
// !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
//

/** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
namespace Transformer {
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export declare function execute(args: ExecuteArguments): ExecuteResult;
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export declare function sealDirectory(root: Directory, files: File[], tags?: string[], description?: string): StaticDirectory;
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export declare function sealSourceDirectory(root: Directory, option?: SealSourceDirectoryOption, tags?: string[], description?: string, patterns?: string[]): StaticDirectory;
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export declare function sealPartialDirectory(root: Directory, files: File[], tags?: string[], description?: string): StaticDirectory;
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export declare function copyFile(sourceFile: File, destinationFile: Path, tags?: string[], description?: string, keepOutputsWritable?: boolean): DerivedFile;
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export declare function writeFile(destinationFile: Path, content: FileContent, tags?: string[], separator?: string, description?: string): DerivedFile;
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export declare function writeData(destinationFile: Path, content: Data, tags?: string[], description?: string): DerivedFile;
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export declare function writeAllText(destinationFile: Path, content: string, tags?: string[], description?: string): DerivedFile;
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export declare function writeAllText(destinationFile: Path, content: string, tags?: string[], description?: string): DerivedFile;
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export declare function readPipGraphFragment(name: string, file: SourceFile, dependencyNames: string[]): string;
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export const enum SealSourceDirectoryOption { 
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        topDirectoryOnly = 0, 
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        allDirectories, 
    }
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export type Data = string | number | Path | PathFragment | CompoundData | Directory;
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export type FileContent = PathFragment | Path | (PathFragment | Path)[];
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export interface CompoundData { 
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        separator?: string; 
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        contents: Data[]; 
    }
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export interface RunnerArguments {
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        tool?: ToolDefinition;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        tags?: string[];
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        description?: string;
    }
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export interface ToolDefinition extends RunnerArguments {
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        exe: File;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        description?: string;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        nestedTools?: ToolDefinition[];
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        runtimeDependencies?: File[];
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        runtimeDirectoryDependencies?: StaticDirectory[];
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        prepareTempDirectory?: boolean;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        dependsOnWindowsDirectories?: boolean;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        dependsOnAppDataDirectory?: boolean;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        untrackedFiles?: File[];
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        untrackedDirectories?: Directory[];
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        untrackedDirectoryScopes?: Directory[];
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        timeoutInMilliseconds?: number;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        warningTimeoutInMilliseconds?: number;
    }
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export interface ExecuteArguments extends RunnerArguments {
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        arguments: Argument[];
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        workingDirectory: Directory;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        dependencies?: InputArtifact[];
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        implicitOutputs?: OutputArtifact[];
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        optionalImplicitOutputs?: OutputArtifact[];
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        consoleInput?: File | Data;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        consoleOutput?: Path;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        consoleError?: Path;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        environmentVariables?: EnvironmentVariable[];
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        warningRegex?: string;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        errorRegex?: string;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        acquireSemaphores?: SemaphoreInfo[];
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        acquireMutexes?: string[];
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        successExitCodes?: number[];
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        retryExitCodes?: number[];
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        tempDirectory?: Directory;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        additionalTempDirectories?: Directory[];
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        unsafe?: UnsafeExecuteArguments;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        isLight?: boolean;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        keepOutputsWritable?: boolean;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        priority?: number;
    }
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export type InputArtifact = File | StaticDirectory;
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export type OutputArtifact = Path | File | Directory;
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export interface UnsafeExecuteArguments {
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        untrackedPaths?: (File | Directory)[];
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        untrackedScopes?: Directory[];
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        hasUntrackedChildProcesses?: boolean;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        allowPreservedOutputs?: boolean;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        passThroughEnvironmentVariables?: string[];
    }
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export interface EnvironmentVariable {
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        name: string;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        value: EnvironmentValueType;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        separator?: string;
    }
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export type EnvironmentValueType = string | boolean | number | Path | Path[] | File | File[] | Directory | Directory[] | StaticDirectory | StaticDirectory[];
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export interface SemaphoreInfo {
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        limit: number;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        name: string;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        incrementBy: number;
    }
    /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
    export interface ExecuteResult {
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        getOutputFile(output: Path): DerivedFile;
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        getOutputFiles(): DerivedFile[];
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        getRequiredOutputFiles(): DerivedFile[];
        /** Obsolete: You must use import {Transformer} from "Sdk.Transformers" */
        getOutputDirectory(dir: Directory): StaticDirectory;
    }
}
