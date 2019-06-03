// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Transformer {
    /** Copies a file to a new destination; the created copy-pip is tagged with 'tags'. */
    @@public
    export function copyFile(sourceFile: File, destinationFile: Path, tags?: string[], description?: string, keepOutputsWritable?: boolean): DerivedFile {
        return _PreludeAmbientHack_Transformer.copyFile(sourceFile, destinationFile, tags, description, keepOutputsWritable);
    }

        /** The different kind of supported output artifacts.
     * If a Path or File is passed directly, they are interpreted as required outputs.
     * If a Directory is passed directly, it is interpreted as an (exclusive) opaque directory
     * Otherwise, kinds associated to each of these entities are passed explicitly via DirectoryOutput or FileOrPathOutput
     * */
    @@public
    export type Output = Path | File | Directory | DirectoryOutput | FileOrPathOutput;

    @@public
    @@obsolete("Please use 'Output' instead")
    export type OutputArtifact = Path | File | Directory;

    /** Kinds of input artifact that can be argument types for the inputs functions. */
    @@public
    export type Input = File | StaticDirectory;

    /** Kinds of input artifact that can be argument types for the inputs functions. */
    @@public
    export type InputArtifact = File | StaticDirectory;

    /** Represents a shared or regular (exclusive) opaque directory */
    @@public
    export interface DirectoryOutput {
        kind: OutputDirectoryKind;
        directory: Directory;
    }

    /** An output directory can be shared or exclusive
     * Note: Shared directories is an experimental feature, use it at your own risk.
     */
    @@public
    export type OutputDirectoryKind = "shared" | "exclusive";

    /** Represents a path where the output is to be created, or a file for the case of a rewritten file */
    @@public
    export interface FileOrPathOutput {
        existence: FileExistenceKind;
        artifact: Path | File;
    }

    /** An output file can be required, optional or temporary. */
    @@public
    export type FileExistenceKind = "required" | "optional" | "temporary";

    @@public
    export interface RunnerArguments {
        tool?: ToolDefinition;

        /** Arbitrary pip tags. */
        tags?: string[];

        /**
         * Arbitrary pip description.
         * Pip description does not affect pip cacheability.
         */
        description?: string;
    }

        /** Schedules a new pip, according to given arguments. */
        @@public
        export function execute(args: ExecuteArguments): ExecuteResult {
            return <ExecuteResult>_PreludeAmbientHack_Transformer.execute(args);
        }

        @@public
        export interface ExecuteArguments extends ExecuteArgumentsCommon {
            /** Regular process pips that make calls to one or more service
              * pips should use this field to declare those dependencies
              * (so that they don't get scheduled for execution before all
              * the services have started). */
            servicePipDependencies?: ServiceId[];

            /** Whether to grant the read/write permissions of this pip to
              * the declared service pips (permissions are granted only
              * throughout the lifetime of this pip). */
            delegatePermissionsToServicePips?: PermissionDelegationMode;
        }

        /** Different options for delegating permissions of a process to a service pip. */
        @@public
        export const enum PermissionDelegationMode {
            /** Don't grant any permissions at all. */
            none,

            /** Grant permissions only throughout the lifetime of the caller pip. */
            temporary,

            /** Grant permissions permanently, i.e., until the service pip terminates. */
            permanent
        }

        @@public
        export interface ExecuteArgumentsCommon extends RunnerArguments {
            /** Command-line arguments. */
            arguments: Argument[];

            /** Working directory. */
            workingDirectory: Directory;

            /** Tools dependencies. */
            dependencies?: InputArtifact[];

            /** Implicit outputs. */
            // TODO: Uncomment this out once we move the selfhost to not use this field
            // @@obsolete("Please use 'Outputs' instead")
            implicitOutputs?: OutputArtifact[];

            /** Optional (or temporary) implicit outputs. */
            // TODO: Uncomment this out once we move the selfhost to not use this field
            // @@obsolete("Please use 'Outputs' instead")
            optionalImplicitOutputs?: OutputArtifact[];

            /** Tool outputs */
            outputs?: Output[];

            /** Console input. */
            consoleInput?: File | Data;

            /** Redirect console output to file. */
            consoleOutput?: Path;

            /** Specifies the standard error file to use for the process. */
            consoleError?: Path;

            /** Environment variables. */
            environmentVariables?: EnvironmentVariable[];

            /** Regex that would be used to extract warnings from the output. */
            warningRegex?: string;

            /** Regex that would be used to extract errors from the output. */
            errorRegex?: string;

            /** Semaphores to acquire */
            acquireSemaphores?: SemaphoreInfo[];

            /** Mutexes to acquire */
            acquireMutexes?: string[];

            /** A custom set of success exit codes. Any other exit code would indicate failure. If unspecified, by default, 0 is the only successful exit code. */
            successExitCodes?: number[];

            /** A custom set of exit codes that causes pip to be retried by BuildXL. If an exit code is also in the successExitCode, then the pip is not retried on exiting with that exit code. */
            retryExitCodes?: number[];

            /** Temporary directory for the tool to use (use Context.getTempDirectory() to obtain one), and set TEMP and TMP. */
            tempDirectory?: Directory;

            /** Additional temporary directories, but none set TEMP or TMP. */
            additionalTempDirectories?: Directory[];

            /** Unsafe arguments */
            unsafe?: UnsafeExecuteArguments;

            /** Whether to mark this process as "light". */
            isLight?: boolean;

            /** Set outputs to remain writable */
            keepOutputsWritable?: boolean;

            /** Privilege level required by this process to execute. */
            privilegeLevel?: "standard" | "admin";

            /** Whether this process should run in an isolated container (i.e. filesystem isolation)
             * When running in a container, the isolation level can be controlled by 'containerIsolationLevel' field.
             * Note: this is an experimental feature for now, use at your own risk
             * Default is globally controlled by the sandbox configuration
             * */
            runInContainer?: boolean;

            /**
             * Configures which inputs and outputs of this process should be isolated.
             * Default is globally controlled by the sandbox configuration
             * TODO: input isolation is not implemented
             */
            containerIsolationLevel?: ContainerIsolationLevel;

            /**
             * The policy to apply when a double write occurs.
             * Default is globally controlled by the sandbox configuration
             */
            doubleWritePolicy?: DoubleWritePolicy;
        }

        @@public
        export interface ExecuteResult {
            getOutputFile(output: Path): DerivedFile;
            getOutputFiles(): DerivedFile[];
            getRequiredOutputFiles(): DerivedFile[];
            getOutputDirectory(dir: Directory): OpaqueDirectory;
        }

        @@public
        export type EnvironmentValueType = string | boolean | number | Path | Path[] | File | File[] | Directory | Directory[] | StaticDirectory | StaticDirectory[];

        @@public
        export interface EnvironmentVariable {
            name: string;
            value: EnvironmentValueType;
            separator?: string;
        }

        @@public
        export const enum ExitCodeSuccessCriteria {
            zeroIsSuccess = 1,
            zeroOr255IsSuccess,
        }

        @@public
        export interface UnsafeExecuteArguments {
            untrackedPaths?: (File | Directory)[];
            untrackedScopes?: Directory[];
            hasUntrackedChildProcesses?: boolean;
            allowPreservedOutputs?: boolean;
            passThroughEnvironmentVariables?: string[];
            preserveOutputWhitelist?: (File | Directory)[];
        }

        /**
         * Data for a declared semaphore
         */
        @@public
        export interface SemaphoreInfo {
            /** The maximum value */
            limit: number;

            /** The resource name */
            name: string;

            /** The semaphore value */
            incrementBy: number;
        }

            /** Schedules a new Ipc pip. */
    @@public
    export function ipcSend(args: IpcSendArguments): IpcSendResult {
        return <IpcSendResult>_PreludeAmbientHack_Transformer.ipcSend(args);
    }

    /**
     * Arguments for the 'Transformer.ipcSend' function.
     */
    @@public
    export interface IpcSendArguments {
        /** Opaque identifier for the IPC channel. */
        moniker: IpcMoniker;

        /** Command-line arguments to be rendered as a string and used as the IPC message body. */
        messageBody: Argument[];

        /** Service pip that this IPC call will be directed at. */
        targetService: ServiceId;

        /** Output file to write the result to.  If not specified, a default output file name is used.*/
        outputFile?: Path;

        /** Additional input dependencies. */
        fileDependencies?: InputArtifact[];

        /** Maximum number of retries to establish a connection. */
        maxConnectRetries?: number;

        /** Delay in milliseconds between two consecutive retries to establish a connection. */
        connectRetryDelayMillis?: number;

        /**
         * Files not to materialize eagerly.
         *
         * IPC pips may want to use this option when they will explicitly request file materialization
         * from BuildXL, via a BuildXL service identified by the Transformer.getIpcServerMoniker()
         * moniker, just before the files are needed.  This makes sense for pips that expect that often
         * times they will not have to access the actual files on disk.
         */
        lazilyMaterializedDependencies?: File[];

        /**
         * Whether this pip must execute on the master node in a distributed build.  Defaults to false.
         */
        mustRunOnMaster?: boolean;

        /** Arbitrary tags */
        tags?: string[];
    }

    /**
     * Result of the 'ipcSend' function.
     */
    @@public
    export interface IpcSendResult {
        outputFile: DerivedFile;
    }

    /**
     * Opaque type representing a moniker used for inter-process communication (IPC).
     *
     * A value of this type should not be created directly; instead, always use Transformer.getNewIpcMoniker().
     */
    @@public
    export interface IpcMoniker {
        __ipcMonikerBrand: any;
    }


    /** Seals specified root folder with a set of files; the created pip is tagged with 'tags'. */
    @@public
    export function sealDirectory(rootOrArgs: any, files: File[], tags?: string[], description?: string, scrub?: boolean): StaticDirectory {
        return _PreludeAmbientHack_Transformer.sealDirectory(rootOrArgs, files, tags, description, scrub);
    }

    /** Seals specified root folder without the need to specify all files provided root is under a readonly mount; the created pip is tagged with 'tags'. */
    @@public
    export function sealSourceDirectory(rootOrArgs: any, option?: SealSourceDirectoryOption, tags?: string[], description?: string, patterns?: string[]): StaticDirectory {
        return _PreludeAmbientHack_Transformer.sealSourceDirectory(rootOrArgs, option, tags, description, patterns);
    }

    /** Seals a partial view of specified root folder with a set of files; the created pip is tagged with 'tags'. */
    @@public
    export function sealPartialDirectory(rootOrArgs: any, files: File[], tags?: string[], description?: string): StaticDirectory {
        return _PreludeAmbientHack_Transformer.sealPartialDirectory(rootOrArgs, files, tags, description);
    }

    /** Creates a shared opaque directory whose content is the aggregation of a collection of shared opaque directories.
     * The provided root can be any arbitrary directory that is a common ancestor to all the provided directories
    */
    @@public
    export function composeSharedOpaqueDirectories(rootOrArgs: any, directories: SharedOpaqueDirectory[]): SharedOpaqueDirectory {
        return _PreludeAmbientHack_Transformer.composeSharedOpaqueDirectories(rootOrArgs, directories);
    }


    /** Options for sealing source directory. */
    @@public
    export const enum SealSourceDirectoryOption {
        /** Indicates the SealedSourceDirectory can access only files in the root folder, not recursively. */
        topDirectoryOnly = 0,
        /** Indicates the SealedSourceDirectory can access all files in all directories under the root folder, recursively. */
        allDirectories,
    }


    /** Schedules a new service pip. */
    @@public
    export function createService(args: CreateServiceArguments): CreateServiceResult {
        return <CreateServiceResult>_PreludeAmbientHack_Transformer.createService(args);
    }

    /**
     * Returns a new moniker for any IPC, as provided by Domino.Ipc.dll.
     *
     * A moniker must be provided to every 'ipcSend' call because a moniker is used to identify
     * the communication channel for the inter-process communication.
     */
    @@public
    export function getNewIpcMoniker(): IpcMoniker {
        return _PreludeAmbientHack_Transformer.getNewIpcMoniker();
    }

    /**
     * Returns the moniker identifying the BuildXL IPC server.
     */
    @@public
    @@obsolete("Please use getNewIpcMoniker")
    export function getDominoIpcServerMoniker(): IpcMoniker {
        return getNewIpcMoniker();
    }

    @@public
    export interface CreateServiceArguments extends ExecuteArgumentsCommon {
        /** A command for BuildXL to execute at the end of /phase:Execute
          * to gracefully shut down this service. */
        serviceShutdownCmd?: ExecuteArguments | IpcSendArguments;

        /** A command for BuildXL to schedule after all client pips of this service pip. */
        serviceFinalizationCmds?: (ExecuteArguments | IpcSendArguments)[];
    }

    @@public
    export interface CreateServiceResult extends ExecuteResult {
        /** Unique service pip identifier assigned by BuildXL at creation time.  */
        serviceId: ServiceId;
    }

    @@public
    export interface ServiceId {}

        /**
     * A detailed definition of a tool.
     */
    @@public
    export interface ToolDefinition extends ToolDefinitionOptions {
        /** The file with the actual executable. */
        exe: File;
    }

    @@public
    export interface ToolDefinitionOptions {

        /** Common description that will be used for all pips. */
        description?: string;

        /** List of nested tools used by current executable. */
        nestedTools?: ToolDefinition[];

        /**
         * The files that are runtime dependencies for this executable.
         *
         * Unlike "untrackedFiles", BuildXL is tracking if these files change.
         */
        runtimeDependencies?: File[];

        /**
         * The directories that are runtime dependencies for this executable.
         * The executable may access any files under these directories.
         *
         * Unlike "untrackedDirectoryScopes", BuildXL is tracking if these files change.
         */
        runtimeDirectoryDependencies?: StaticDirectory[];

        /** Runtime environment for the executable.  */
        runtimeEnvironment?: RuntimeEnvironment;

        /** This tool needs a temporary directory. */
        prepareTempDirectory?: boolean;

        /** True if the executable depends on Windows directories. This signals that accesses to the Windows Directories should be allowed. */
        dependsOnWindowsDirectories?: boolean;

        /**
         * True if the executable depends on the per-user AppData directory. Setting this to true means that AppData
         * will be an untracked directory scope and the specific location of AppData will not be included in the pip's fingerpint.
         */
        dependsOnAppDataDirectory?: boolean;

        /** Files to which read and write accesses should not be tracked. */
        untrackedFiles?: File[];

        /** Directories to which accesses should not be tracked (however, accesses to all nested files and subdirectories are tracked). */
        untrackedDirectories?: Directory[];

        /** Directories in which nested file and directory accesses should not be tracked. */
        untrackedDirectoryScopes?: Directory[];

        /** Provides a hard timeout after which the Process will be marked as failure due to timeout and terminated. */
        timeoutInMilliseconds?: number;

        /**
         * Sets an interval value that indicates after which time BuildXL will issue a warnings that the process is running longer
         * than anticipated */
        warningTimeoutInMilliseconds?: number;

        // TODO: unimplemented
        /*
            producesPathIndependentOutputs?: boolean;
            // tags?: string[]; moved to RunnerArguments
        */
    }

    /**
     * Specifies specific settings that should be used when launching a CLR application. These settings are implemented
     * by setting specific environment variables for the launched process. This can be used to run an application under
     * a specific CLR or a CLR that is xcopy installed on a machine.
     */
    @@public
    export interface ClrConfig {
        /** Path to the installation root of the CLR. COMPLUS_InstallRoot will be set to this path. */
        installRoot?: Path;

        /** The version of the CLR. COMPLUS_InstallRoot will be set to this value. */
        version?: string;

        /** Value for COMPLUS_NoGuiFromShim environment variable. */
        guiFromShim?: boolean;

        /** Value for COMPLUS_DbgJitDebugLaunchSetting environment variable. */
        dbgJitDebugLaunchSetting?: boolean;

        /**
         * Force all apps to use the checked in CLR, despite <supportedRuntime> elements in the config.
         * We need this to deal with tools that specify supportedRuntime v4.0 or v4.0.30319 since we can
         * only specify one runtime version (COMPLUS_Version).  COMPLUS_OnlyUseLatestClr will be set to "1"
         * if this value is true.
         */
        onlyUseLatestClr?: boolean;

        /** Default version of the CLR.  COMPLUS_DefaultVersion will be set to this value. */
        defaultVersion?: string;
    }

    /**
     * Information about the runtime environment of a process.
     */
    @@public
    export interface RuntimeEnvironment {
        /** Minimum supported OS version. No requirement when not set. */
        minimumOSVersion?: Version;

        /** Maximum supported OS version. Unbounded when not set. */
        maximumOSVersion?: Version;

        /** Minimum required CLR version. No requirement when not set. */
        minimumClrVersion?: Version;

        /** Maximum supported CLR version. Unbounded when not set. */
        maximumClrVersion?: Version;

        /**
         * Overrides the default CLR that would be used when launching a process.
         * Specifying this value will cause various environment variables to be set
         * which will cause a specific version of the CLR to be used.
         */
        clrOverride?: ClrConfig;
    }

    /**
     * Version information for an assembly, OS, or CLR. Corresponds to System.Version as described at:
     * http://msdn.microsoft.com/en-us/library/System.Version(v=vs.110).aspx
     */
    @@public
    export interface Version {
        /** The build number. */
        buildNumber?: number;

        /** The major version number. Must be non-negative. */
        major: number;

        /** The minor version number. Must be non-negative. */
        minor: number;

        /** The revision number. */
        revision?: number;
    }
        /**
     * Writes lines to a new file; the created write-pip is tagged with 'tags'.
     * If FileContent is an array, an optional separator can be passed that will be used to join the lines. New line is the default separator.
     **/
    @@public
    export function writeFile(destinationFile: Path, content: FileContent, tags?: string[], separator?: string, description?: string): DerivedFile {
        return _PreludeAmbientHack_Transformer.writeFile(destinationFile, content, tags, separator, description);
    }

    /** Writes data to file. */
    @@public
    export function writeData(destinationFile: any, content: Data, tags?: string[], description?: string): DerivedFile {
        return _PreludeAmbientHack_Transformer.writeData(destinationFile, content, tags, description);
    }

    /** Write all lines. */
    @@public
    export function writeAllLines(destinationFile: any, contents: Data[], tags?: string[], description?: string): DerivedFile {
        return _PreludeAmbientHack_Transformer.writeAllLines(destinationFile, contents, tags, description);
    }

    /** Write all text. */
    @@public
    export function writeAllText(destinationFile: any, content: string, tags?: string[], description?: string): DerivedFile {
        return _PreludeAmbientHack_Transformer.writeAllText(destinationFile, content, tags, description);
    }

    /** Interface for data. */
    @@public
    export type Data = string | number | Path | PathFragment | CompoundData | Directory;

    /** Interface for compound data. */
    @@public
    export interface CompoundData {
        separator?: string;
        contents: Data[];
    }

    /** The content of a file that can be written using writeFile. */
    @@public
    export type FileContent = PathFragment | Path | (PathFragment | Path)[];
}

export namespace Annotation {
    @@public
    export type AnnotationResult = (a: any) => any;

    /**
     * Placeholder that should be used as a body for ambient annotations.
     *
     * DScript extends TypeScript language with support of ambient decorators (i.e. decorators with no additional behavior).
     * Such annotations could be used on type declarations and doesn't have any runtime semantics.
     *
     * DScript uses the same set of rules that TypeScript has and requires all ambient decorators to return a function.
     * To simplify following this contract, every function that is expected to be used as an annotation should return annotationBody.
     */
    @@public
    export const annotationBody: AnnotationResult = dummyArg => dummyArg;
}

namespace Artifact {
    /** Creates an input artifact from file or directory. */
    @@public
    export function input(value: Transformer.InputArtifact): Artifact {
        return createArtifact(value, ArtifactKind.input);
    }

    /** Creates a list of input artifacts from a list of files and directories. */
    @@public
    export function inputs(values: Transformer.InputArtifact[]): Artifact[] {
        return (values || []).mapDefined(input);
    }

    /** Creates an output artifact from a file. */
    @@public
    export function output(value: Transformer.OutputArtifact): Artifact {
        return createArtifact(value, ArtifactKind.output);
    }

    /**
     * Creates a shared opaque directory from a directory.
     * This is an unsafe feature, the current implementation is in a prototyping stage. Use at your own risk.
     * */
    @@public
    export function sharedOpaqueOutput(value: Directory): Artifact {
        return createArtifact(value, ArtifactKind.sharedOpaque);
    }

    /** Creates a list of output artifacts from a list of files.  */
    @@public
    export function outputs(values: Transformer.OutputArtifact[]): Artifact[] {
        return (values || []).mapDefined(output);
    }

    /**
     * Creates a rewritten artifact from a file.
     * If an output path is specified (not undefined), then the (original) to-be-rewritten file will be copied first to the specified output path.
     * The result of copying is then used as a dependency of the transformer that consumes this artifact.
     */
    @@public
    export function rewritten(originalInput: File, outputPath?: Path): Artifact {
        return outputPath !== undefined
            ? createArtifact(outputPath, ArtifactKind.rewritten, originalInput)
            : createArtifact(originalInput, ArtifactKind.rewritten);
    }

    /** Creates an artifact from a file or a directory, but marks it as neither an input nor an output. */
    @@public
    export function none(value: Transformer.InputArtifact | Transformer.OutputArtifact | Directory): Artifact {
        if (value === undefined) return undefined;

        return createArtifact(value.path, ArtifactKind.none);
    }

    /** Creates an input artifact from file or directory. */
    @@public
    export function vsoHash(value: File): Artifact {
        return createArtifact(value, ArtifactKind.vsoHash);
    }

    /** Creates an input artifact from file or directory. */
    @@public
    export function fileId(value: File): Artifact {
        return createArtifact(value, ArtifactKind.fileId);
    }

    function createArtifact(value: Transformer.InputArtifact | Transformer.OutputArtifact, kind: ArtifactKind, original?: File): Artifact {
        if (value === undefined) return undefined;

        return <Artifact>{
            path: value,
            kind: kind,
            original: original
        };
    }
}

export namespace Cmd {
    @@public
    export const defaultArgumentSeparator : string = " ";

    // TODO: Argument validation are based on custom function isNotNullOrEmpty, because currently interpreter does not support !! pattern.
    function isNullOrEmpty(value: string): boolean {
        return value === undefined || value.length === 0;
    }

    /**
     * Adds some literal text to the command-line for the process.
     * This text is inserted as-is with no automatic quoting or any other processing except for the fact that text is
     * separated from other text by whitespace.
     *
     * Example:
     *  Cmd.rawArgument("/arg value with space") => "tool /arg value with space"
     *  Cmd.option("/arg ", "value with space") => "tool /arg 'value with space'"
     */
    @@public
    export function rawArgument(value: string): Argument {
        Contract.requires(!isNullOrEmpty(value), "value should not be undefined or empty");

        return {
            name: undefined,
            value: createPrimitive(value, ArgumentKind.rawText),
        };
    }

    /**
     * Adds a flag argument to the tool command lines if 'flagValue' is true.
     * Example: Cmd.flag("/nologo", arg.noLogo) => /nologo argument would be added only when arg.noLogo is true.
     */
    @@public
    export function flag(name: string, flagValue: boolean): Argument {
        Contract.requires(!isNullOrEmpty(name), "name should not be undefined or empty");

        if (flagValue === undefined || flagValue === false) {
            return undefined;
        }

        return {
            name: name,
            value: createPrimitive(undefined, ArgumentKind.flag),
        };
    }

    @@public
    export function optionalBooleanFlag(name: string, boolValue: boolean, trueFlag: string, falseFlag: string): Argument {
        Contract.requires(!isNullOrEmpty(name), "name should not be undefined or empty");

        if (boolValue === undefined) return undefined;

        let flag = boolValue ? trueFlag : falseFlag;
        return {
            name: name,
            value: (flag === "") ? createPrimitive(undefined, ArgumentKind.flag) : flag,
        };
    }

    /**
     * Creates an argument with '-' if boolValue is false, and the argument with '+' if true.
     * If enableNeedsNoPlus is set and boolValue is true, will just return the argument.
     * If boolValue is undefined 'undefined' would be returned.
     *
     * Example:
     *  Cmd.sign("/debug", arg.debug) => "tool /debug+" if arg.debug is true, "tool /debug-" if arg.debug is false.
     *  Cmd.sign("/opt", arg.optimize, true) => "tool /opt" if arg.optimize is true, "tool /opt-" if arg.optimize is false.
     */
    @@public
    export function sign(name: string, boolValue: boolean, enableNeedsNoPlus?: boolean): Argument {
        return optionalBooleanFlag(name, boolValue, enableNeedsNoPlus? "" : "+", "-");
    }

    /**
     * Specifies whether the process can use response files.
     * If the argument is true, then it forces the remaining arguments into a response file.
     * If false, then a response file is only used if the command-line for the tool exceeds the maximum allowed
     * by the system.
     *
     * By default, the prefix for response file is "@". Some tools, like C# or C++ compiler accepts a response
     * file in the form of "@file.rsp", where "@" is the prefix.
     *
     * Example:
     *   let args: Argument[] = [
     *     Cmd.flag("/noLogo", args.noLogo),
     *     Cmd.startUsingResponseFile(),
     * ];
     */
    @@public
    export function startUsingResponseFile(force?: boolean): Argument {
        return startUsingResponseFileWithPrefix(undefined, force);
    }

    /**
     * Specifies whether the process can use response files like startUsingResponseFile, but also allows users to specify a prefix.
     * For example, C# or C++ compiler accepts a response file in the form of "@file.rsp", where "@" is the prefix.
     * If prefix is undefined, then the default is "@".
     *
     * Example:
     *   let args: Argument[] = [
     *     Cmd.flag("/noLogo", args.noLogo),
     *     Cmd.startUsingResponseFile("@", true),
     * ];
     */
    @@public
    export function startUsingResponseFileWithPrefix(prefix: string, force?: boolean): Argument {
        let forceStr = (force === undefined) ? undefined : (force ? "true" : "false");
        return {
            name: prefix,
            value: createPrimitive(forceStr, ArgumentKind.startUsingResponseFile),
        };
    }

    /**
     * Special factory method that creates an argument with a set of files.
     *
     * Example:
     *   Cmd.files(arg.sources) => tool source1 source2
     */
    @@public
    export function files(files: File[]): Argument {
        if (files === undefined || files.length === 0) {
            return undefined;
        }

        return {
            name: undefined,
            value: Artifact.inputs(files),
        };
    }

    /**
     * Creates regular unnamed command line argument with specified value.
     * If value is undefined the function will return 'undefined', and no arguments would be added to the tool's command line.
     *
     * Example:
     *   Cmd.argument(arg.sourceFileName) => "tool sourceFileName"
     */
    @@public
    export function argument(value: ArgumentValue): Argument {
        if (value === undefined) {
            return undefined;
        }

        return {
            name: undefined,
            value: value
        };
    }

    /**
     * Creates regular unnamed command line argument with specified values.
     * If value is 'undefined' the function will return 'undefined', and no arguments would be added to the tool's command line.
     *
     * Example:
     *   Cmd.args(["x", "y"]) => "tool x y"
     */
    @@public
    export function args(values: ArgumentValue[]): Argument {
        if (values === undefined) {
            return undefined;
        }

        return {
            name: undefined,
            value: values
        };
    }

    /**
     * Creates named command line option.
     * If value is 'undefined' the function will return 'undefined' and no arguments would be added to the tool's command line.
     *
     * Example:
     *   Cmd.option("/timeout:", 42) => "tool /timeout:42"
     *   Cmd.option("--timeout ", 42) => "tool --timeout 42"
     *   Cmd.option("/r:", Cmd.join(",", ['mscorlib.dll', 'system.dll'])) => "tool /r:mscorlib.dll,system.dll"
     */
    @@public
    export function option(name: string, value: ArgumentValue, condition?: boolean): Argument {
        Contract.requires(!isNullOrEmpty(name), "name should not be undefined or empty");

        if (value === undefined || condition === false) {
            return undefined;
        }

        return {
            name: name,
            value: value,
        };
    }

    /**
     * Creates named command line options with multiple values.
     * This function will create a special argument that would have multiple values with the same name.
     *
     * Example:
     *   Cmd.options("/r:", ['r1.dll', 'r2.dll']) => "tool /r:r1.dll /r:r2.dll"
     */
    @@public
    export function options(name: string, values: ArgumentValue[]): Argument {
        Contract.requires(!isNullOrEmpty(name), "name should not be undefined or empty");

        if (values === undefined || values.length === 0) {
            return undefined;
        }

        return {
            name: name,
            value: values,
        };
    }

    /**
     * Creates a CompoundArgumentValue instance with a separator.
     * This helper function is very useful for creating complex command line options that contains multiple values.
     */
    @@public
    export function join(separator: string, values: ArgumentValue[]): CompoundArgumentValue {
        if (values === undefined || values.length === 0) {
            return undefined;
        }

        return {
            values: values,
            separator: separator,
        };
    }

    @@public
    export function concat(values: ArgumentValue[]): CompoundArgumentValue {
        return join("", values);
    }

    function createPrimitive(value: PrimitiveValue, kind: ArgumentKind): PrimitiveArgument {
        return {
            value: value,
            kind: kind,
        };
    }
}

namespace Tool {
    /** The option value separation mode which describes whether how the parser must take care of the separator. */
    @@public
    export const enum OptionSeparationMode {
        /** The option separation mode is not set. */
        notSet = 0,

        /** Means that for scalar options option name and the value are not separated anyhow of the following form /[opt][value].
         * For example, the following string /pathB:\src represents an option named 'path' and value 'B:\src'. */
        notSupported,

        /** Means that for scalar options option name and the value maybe separated by an optional separator character and are of the following
         * form /[opt][optional separator][value]. For example, the following strings /mode:strict and /modestrict represent an option named 'mode'
        * and value 'strict'. */
        supported,

        /** Same as Supported but separator is required. */
        required
    }

    @@public
    export interface Options {
        /** The option name and value separation mode which describes whether how the parser must take care of the separator. */
        optionSeparationMode?: OptionSeparationMode;

        /**The option name and value separator character. */
        optionSeparator?: string;

        /** The value which indicates whether the option supports multiple values that follow single option name. */
        supportsMultipleValues?: boolean;

        /** The multiple values separator character. */
        multipleValueSeparator?: string;

        /** The value which indicates whether the option should be negated/toggled for boolean transformer arguments. */
        negateOption?: boolean;

        valueSeparator?: string;
    }

    /** The annotation for an option for a runner. It represents a mapping between a command
     * line option for a tool's executable and the runner's argument interface properties
     */
    @@public
    export function option(opt: string, moreOptions?: Options): Annotation.AnnotationResult {
        return Annotation.annotationBody;
    }

    /** The annotation for a runner. */
    @@public
    export function name(name: string): Annotation.AnnotationResult {
        return Annotation.annotationBody;
    }

    /** The annotation for a runner function declaration. */
    @@public
    export function runner(name: string): Annotation.AnnotationResult {
        return Annotation.annotationBody;
    }

    /** A structure representing some metadata for a builder. */
    @@public
    export interface BuilderMetadata {
        /** The builder name */
        name: string;

        /** The transitive closure of runners that are invoked by the builder. */
        invokesTransformers: string[];
    }

    /** The annotation for a builder function declaration. */
    @@public
    export function builder(metadata: BuilderMetadata): Annotation.AnnotationResult {
        return Annotation.annotationBody;
    }


}
