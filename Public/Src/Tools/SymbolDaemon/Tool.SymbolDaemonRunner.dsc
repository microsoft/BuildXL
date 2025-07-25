// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";


@@public
export const symbolTag = "artifact-services-symbol-pip";

// for internal use
interface UberArguments extends SymbolCreateArguments, ServiceStartResult {
    ipcServerMoniker?: IpcMoniker;
}

/**
 * Generic SymbolDaemon runner.  Can be used whenever VSO authentication using Azure Active Directory is sufficient,
 * which is typically the case when running on a developer box, or VSTS lab machine.
 */
@@public
export const runner: SymbolRunner = {
    createSymbol: (
        args: SymbolCreateArguments
    )
    => createSymbol(args),

    addFilesToSymbol: addFiles,

    addDirectoriesToSymbol: addDirectories,

    startDaemonNoSymbol: (
        args: ServiceStartArguments
    )
    => {
        return startService(
            <UberArguments>args,
            "start-noservice",
            "stop",
            undefined,
            true
        );
    },

    pingDaemon: (serviceInfo: ServiceStartResult, args: OperationArguments) => executePingAsProcess(serviceInfo, args),

    testReadFile: (
        serviceInfo: ServiceStartResult,
        file: File,
        args: OperationArguments
    )
    => {
        Contract.requires(
            file !== undefined,
            "file to read must be defined"
        );
        return executeDaemonCommand(
            serviceInfo,
            "test-readfile",
            <UberArguments>args,
            (ipcArgs) => ipcArgs.merge<Transformer.IpcSendArguments>({
                messageBody: [
                    Cmd.option("--file ", Artifact.input(file)),
                ],
            })
        );
    },
};

/**
 * Specialized runner for CloudBuild, which sets some magic environment variables
 * CloudBuild uses to discover credential providers needed to authenticate against VSO.
 */
@@public
export const cloudBuildRunner: SymbolRunner = {
    createSymbol: (
        args: SymbolCreateArguments
    )
    => runner.createSymbol(
        applyCloudBuildDefaultsAndSetEnvVars(args)
    ),
    addFilesToSymbol: runner.addFilesToSymbol,
    addDirectoriesToSymbol: runner.addDirectoriesToSymbol,
    startDaemonNoSymbol: runner.startDaemonNoSymbol,
    pingDaemon: runner.pingDaemon,
    testReadFile: runner.testReadFile,
};

function startService(args: UberArguments, startCommand: string, shutdownCmdName: string, finalizationCmdName?: string, skipValidation?: boolean): ServiceStartResult {
    if (skipValidation !== true) {
        Contract.requires(
            args.service !== undefined || args.symbolServiceConfigFile !== undefined,
            "symbol endpoint must be defined, or must be specified in symbol config file"
        );
        Contract.requires(
            args.name !== undefined || args.symbolServiceConfigFile !== undefined,
            "symbol name must be defined, or must be inferrable in symbol config file"
        );
    }

    const moniker = Transformer.getNewIpcMoniker();

    const connectArgs = <UberArguments><OperationArguments>{maxConnectRetries: args.maxConnectRetries, connectRetryDelayMillis: args.connectRetryDelayMillis, ipcMoniker: moniker};

    const serviceStartCmd = getExecuteArguments(
        startCommand,
        overrideMoniker(args, moniker)
    );
    const shutdownCmd = getExecuteArguments(shutdownCmdName, connectArgs);
    const finalizeCmd = finalizationCmdName !== undefined
        ? getIpcArguments(connectArgs, finalizationCmdName, connectArgs, overrideMustRunOnOrchestrator)
        : undefined;

    const result = Transformer.createService(
        serviceStartCmd.merge<Transformer.CreateServiceArguments>({
            serviceShutdownCmd: shutdownCmd,
            serviceFinalizationCmds: finalizeCmd ? [finalizeCmd] : [],
            unsafe: {
                // only because ArtifactServices may run some external credentials provider
                hasUntrackedChildProcesses: true,
            },
            serviceTrackableTag: symbolTag,
            serviceTrackableTagDisplayName: "SymbolTrackerOverhangMs",
            moniker: moniker
        })
    );

    return <ServiceStartResult>{
        symbolDaemonId: result.serviceId,
        ipcMoniker: moniker,
        outputs: result.getRequiredOutputFiles(),
    };
}

function createSymbol(args: SymbolCreateArguments): SymbolCreateResult {
    // start service
    const shutdownCmdName = "stop";
    const finalizationCmdName = "finalize";
    const startArgs = overrideServerMoniker(
        <UberArguments>args
    );
    const symbolStartResult = startService(
        startArgs,
        "start",
        shutdownCmdName,
        finalizationCmdName,
        false
    );

    const result = executeDaemonCommand(
        symbolStartResult,
        "create",
        <UberArguments>args,
        overrideMustRunOnOrchestrator
    );

    // return aggregate info
    return <SymbolCreateResult>{serviceStartInfo: symbolStartResult, outputs: result.outputs};
}

function addFiles(createResult: SymbolCreateResult, args: OperationArguments, files: File[], clientKeys?: string[], customClientKeyPrefixEnvVariable?: string, tags?: string[]): Result {
    Contract.requires(
        files !== undefined,
        "files to add to a symbol request must be defined"
    );
    Contract.requires(
        createResult !== undefined,
        "result of the 'symbol create' operation must be provided"
    );

    if (files.length === 0) {
        return undefined;
    }

    const symbolMetadataFile = indexSymbolFiles(args, files, clientKeys);

    const fileMessageBody = files.length !== 0
        ? [
            Cmd.options("--file ", files.map(fi => Artifact.input(fi))),
            Cmd.options("--hash ", files.map(fi => Artifact.vsoHash(fi))),
            Cmd.options("--fileId ", files.map(fi => Artifact.fileId(fi))),
            Cmd.option("--symbolMetadata ", Artifact.input(symbolMetadataFile)),
            Cmd.option("--customClientKeyPrefixEnvVariable ", customClientKeyPrefixEnvVariable)
            ]
        : [];

    const uberArgs = args.merge<UberArguments>({
        dependencies: createResult.outputs || [],
        ipcServerMoniker: Transformer.getIpcServerMoniker(),
        tags: tags
    });

    return executeDaemonCommand(
        createResult.serviceStartInfo,
        "addsymbolfiles",
        uberArgs,
        ipcArgs => ipcArgs.merge<Transformer.IpcSendArguments>({
            messageBody: [
                ...fileMessageBody,
            ],
            lazilyMaterializedDependencies: [
                // note: symbolMetadataFile is not included in this list
                ...files,
            ],
        })
    );
}

function addDirectories(createResult: SymbolCreateResult, args: OperationArguments, directories: StaticDirectory[], directoryContentFilters?: string[], directoryReplacementForClientKeys?: string[], customClientKeyPrefixEnvVariable?: string, tags?: string[]): Result {
    Contract.requires(
        createResult !== undefined,
        "result of the 'symbol create' operation must be provided"
    );

    Contract.requires(
        directoryContentFilters === undefined || directoryContentFilters.length === directories.length,
        "The length of directoryContentFilters doesn't match directories length"
    );
    
    const filters = directoryContentFilters === undefined? directories.map(dir => ".*") : directoryContentFilters;

    const symbolMetadataFile = indexSymbolFilesInDirectories(directories, createResult, args, filters, directoryReplacementForClientKeys);

    const directoryMessageBody = directories.length !== 0
        ? [
            Cmd.options("--directory ", directories.map(dir => Artifact.input(dir))),
            Cmd.options("--directoryId ", directories.map(dir => Artifact.directoryId(dir))),
            Cmd.option("--symbolMetadata ", Artifact.input(symbolMetadataFile)),
            Cmd.options("--directoryContentFilter ", filters),
            Cmd.option("--customClientKeyPrefixEnvVariable ", customClientKeyPrefixEnvVariable)
            ]
        : [];

    const uberArgs = args.merge<UberArguments>({
        dependencies: createResult.outputs || [],
        ipcServerMoniker: Transformer.getIpcServerMoniker(),
        tags: tags
    });

    return executeDaemonCommand(
        createResult.serviceStartInfo,
        "addSymbolFilesFromDirectories",
        uberArgs,
        ipcArgs => ipcArgs.merge<Transformer.IpcSendArguments>({
            messageBody: [
                ...directoryMessageBody,
            ],
            lazilyMaterializedDependencies: [
                ...directories,
            ],
        })
    );
}

function indexSymbolFiles(args: OperationArguments, files?: File[], clientKeys?: string[], fileMetadata?: File) : DerivedFile {
    const symbolDataFileName = "symbol_data.txt";

    Contract.requires(
        files.length !== 0,
        "The list of files cannot be empty"
    );

    // get a directory where we are going to store the result of indexing
    const outDir = Context.getNewOutputDirectory("symbol_indexing");

    // the result of indexing
    const outputPath = p`${outDir.path}/${symbolDataFileName}`;

    const executeArguments = <Transformer.ExecuteArguments>{
        tool: tool,
        workingDirectory: outDir,
        arguments: [
            Cmd.argument("indexFiles"),
            Cmd.startUsingResponseFile(false),
            Cmd.options("--file ", files.map(f => Artifact.input(f))),
            Cmd.options("--hash ", files.map(f => Artifact.vsoHash(f))),
            Cmd.options("--customClientKey ", clientKeys),
            Cmd.option("--symbolMetadata ", Artifact.output(outputPath)),
        ],
        unsafe: {
            // SymStoreUtil opens some files (namely .pdb) with ReadWrite access,
            // this causes DFAs and other issues.
            untrackedPaths: files
        },
        description : "SymbolIndexing: " + files[0].name + " and " + (files.length - 1) + " other file(s)",
        tags: [
            symbolTag,
            `symbold-indexFiles`,
            ...(args.tags || []),
        ],
        allowedSurvivingChildProcessNames: ["conhost.exe"]
    };

    // run the tool
    const result = Transformer.execute(executeArguments);

    const outputResult = result.getOutputFile(outputPath);

    return outputResult;
}

function indexSymbolFilesInDirectories(directories: StaticDirectory[], createResult: SymbolCreateResult, args: OperationArguments, directoryContentFilters: string[], directoryReplacementForClientKeys?: string[]) : DerivedFile {
    Contract.requires(
        directories !== undefined,
        "directories to index must be identified"
    );

    Contract.requires(
        directories.length !== 0,
        "The list of directories cannot be empty"
    );

    Contract.requires(
        directoryContentFilters !== undefined && directoryContentFilters.length === directories.length,
        "The length of directoryContentFilters doesn't match directories length"
    );

    Contract.requires(
        directoryReplacementForClientKeys === undefined || (directoryReplacementForClientKeys !== undefined && directoryReplacementForClientKeys.length === directories.length),
        "The length of directoryReplacementForClientKeys doesn't match directories length"
    );

    const directoryMessageBody = directories.length !== 0
        ? [
            Cmd.options("--directory ", directories.map(dir => Artifact.input(dir))),
            Cmd.options("--directoryId ", directories.map(dir => Artifact.directoryId(dir))),
            Cmd.options("--directoryContentFilter ", directoryContentFilters)
            ]
        : [];

    const outDir = Context.getNewOutputDirectory("symbol_indexing");
    const dirContentFile = p`${outDir.path}/dir_content.txt`;

    const uberArgs = args.merge<UberArguments>({
        dependencies: createResult.outputs || [],
        ipcServerMoniker: Transformer.getIpcServerMoniker(),
        consoleOutput: dirContentFile
    });

    const dirContentResult = executeDaemonCommand(
        createResult.serviceStartInfo,
        "getDirectoriesContent",
        uberArgs,
        ipcArgs => ipcArgs.merge<Transformer.IpcSendArguments>({
            messageBody: directoryMessageBody,
            lazilyMaterializedDependencies: directories,
        })
    );

    Contract.requires(
        dirContentResult.outputs.length === 1,
        "There must be exactly one output file"
    );

    const outputPath = p`${outDir.path}/symbol_data.txt`;

    const executeArguments = <Transformer.ExecuteArguments>{
        tool: tool,
        workingDirectory: outDir,
        arguments: [
            Cmd.argument("indexDirectories"),
            Cmd.startUsingResponseFile(false),
            Cmd.options("--directory ", directories.map(dir => Artifact.input(dir))),
            Cmd.option("--inputDirectoriesContent ", Artifact.input(dirContentResult.outputs[0])),
            Cmd.options("--directoryReplacementForClientKey ", directoryReplacementForClientKeys),
            Cmd.option("--symbolMetadata ", Artifact.output(outputPath)),
        ],
        // SymStoreUtil opens some files (namely .pdb) with ReadWrite access, this causes DFAs and other issues.
        // Because the pip consumes dynamic directories we need to specify both the double wrtite policy and the
        // untracked scopes.
        doubleWritePolicy: "allowSameContentDoubleWrites",
        unsafe: {
            untrackedScopes: directories.map(dir => dir.root)
        },
        description : "SymbolIndexing: " + directories[0].name + " and " + (directories.length - 1) + " other directories(s)",
        tags: [
            symbolTag,
            `symbold-indexDirectories`,
            ...(args.tags || []),
        ],
        allowedSurvivingChildProcessNames: ["conhost.exe"]
    };

    // run the tool
    return Transformer.execute(executeArguments).getOutputFile(outputPath);
}

function overrideMustRunOnOrchestrator(args: Transformer.IpcSendArguments): Transformer.IpcSendArguments {
    return args.override<Transformer.IpcSendArguments>({
        mustRunOnOrchestrator: true
    });
}

function overrideServerMoniker<T>(args: T): T {
    return args.override<T>({
        ipcServerMoniker: Transformer.getIpcServerMoniker(),
    });
}

function overrideMoniker(args: Object, moniker: IpcMoniker): UberArguments {
    return args.override<UberArguments>({ipcMoniker: moniker});
}

// Any use of this directory should be in untracked scope.
// BuildXL creates a junction with stable name to the real log directory. If this directory gets tracked,
// the junction will be tracked. Since the junction's target changes build over build, incremental scheduling
// will be jeopardized.
const symbolLogDirectory = d`${Context.getMount("LogsDirectory").path}`;

function overrideToolArguments(tool: Transformer.ToolDefinition, args: UberArguments): Transformer.ToolDefinition {
    return tool.override<Transformer.ToolDefinition>({
        timeoutInMilliseconds: args.timeoutInMilliseconds,
        warningTimeoutInMilliseconds: args.warningTimeoutInMilliseconds,
        untrackedDirectoryScopes: [
            ...(tool.untrackedDirectoryScopes || []),
            ...(args.untrackedDirectoryScopes || []),
            symbolLogDirectory
        ],
    });
}

function getExecuteArguments(command: string, args: UberArguments, ...additionalCmdArgs: Argument[]): Transformer.ExecuteArguments {
    const nametag = `symbold-${command}`;
    const outDir = Context.getNewOutputDirectory(nametag);
    const selectedTool: Transformer.ToolDefinition = args.tool || tool;
    Contract.assert(
        selectedTool !== undefined,
        "tool not specified"
    );

    return <Transformer.ExecuteArguments>{
        tool: overrideToolArguments(selectedTool, args),
        workingDirectory: outDir,
        tags: [
            symbolTag,
            nametag,
            ...(args.tags || []),
        ],
        arguments: [
            Cmd.argument(command),
            Cmd.option("--moniker ", args.ipcMoniker),
            Cmd.option("--ipcServerMoniker ", args.ipcServerMoniker),
            Cmd.option("--name ", args.name),
            Cmd.option("--service ", args.service),
            Cmd.option("--maxConcurrentClients ", args.maxConcurrentClients),
            Cmd.option("--maxParallelUploads ", args.maxParallelUploads),
            Cmd.option("--nagleTimeMillis ", args.nagleTimeMillis),
            Cmd.option("--batchSize ", args.batchSize),
            Cmd.option("--retentionDays ", args.retentionDays),
            Cmd.option("--maxConnectRetries ", args.maxConnectRetries),
            Cmd.option("--connectRetryDelayMillis ", args.connectRetryDelayMillis),
            Cmd.option("--sessionId ", args.sessionId),
            Cmd.flag("--enableCloudBuildIntegration", args.enableCloudBuildIntegration),
            Cmd.flag("--enableTelemetry", args.enableTelemetry),
            (args.debugEntryCreateBehavior !== undefined) ? Cmd.option("--debugEntryCreateBehavior ", debugEntryCreateBehaviorToString(args.debugEntryCreateBehavior)) : undefined,
            Cmd.flag("--verbose", true),
            Cmd.option("--logDir ", symbolLogDirectory.path),
            ...additionalCmdArgs,
            Cmd.option(
                "--configFile ",
                Artifact.input(args.symbolServiceConfigFile)
            ),
            Cmd.option("--operationTimeoutMinutes ", args.operationTimeoutMinutes),
            Cmd.option("--maxOperationRetries ", args.maxOperationRetries),
        ],
        consoleOutput: outDir.combine(`${nametag}-stdout.txt`),
        dependencies: [
            ...(args.dependencies || []),
        ],
        environmentVariables: (args.additionalEnvironmentVars || []),
        unsafe: {
            passThroughEnvironmentVariables: 
            [
                ...(args.forwardEnvironmentVars || []),
                "BuildXLEnableGrpcIpc"
            ],
        }
    };
}

function executePingAsProcess(serviceStartInfo: ServiceStartResult, args: OperationArguments): Result {
    const exeArgs = getExecuteArguments("ping", overrideMoniker(args, serviceStartInfo.ipcMoniker)).merge<Transformer.ExecuteArguments>({
        servicePipDependencies: [ serviceStartInfo.symbolDaemonId ],
    });
    const result = Transformer.execute(exeArgs);
    return <Result>{outputs: result.getRequiredOutputFiles() };
}

function executeDaemonCommand(serviceStartInfo: ServiceStartResult, command: string, args: UberArguments, overrideIpcArgs?: (a: Transformer.IpcSendArguments) => Transformer.IpcSendArguments ): Result {
    const ipcArgs = getIpcArguments(serviceStartInfo, command, args, overrideIpcArgs);
    const ipcResult = Transformer.ipcSend(ipcArgs);
    return <Result>{outputs: [ipcResult.outputFile]};
}

function getIpcArguments(serviceStartInfo: ServiceStartResult, command: string, args: UberArguments, overrideIpcArgs?: (a: Transformer.IpcSendArguments) => Transformer.IpcSendArguments ): Transformer.IpcSendArguments {
    Contract.requires(
        serviceStartInfo !== undefined,
        "service start info must be defined"
    );
    Contract.requires(
        serviceStartInfo.ipcMoniker !== undefined,
        "IPC moniker must be defined"
    );
    const exeArgs = getExecuteArguments(command, args);
    const ipcArgs = <Transformer.IpcSendArguments>{
        tags: exeArgs.tags,
        moniker: serviceStartInfo.ipcMoniker,
        targetService: serviceStartInfo.symbolDaemonId,
        messageBody: exeArgs.arguments,
        maxConnectRetries: args.maxConnectRetries,
        connectRetryDelayMillis: args.connectRetryDelayMillis,
        fileDependencies: exeArgs.dependencies,
        outputFile: args.consoleOutput || exeArgs.consoleOutput,
    };

    return overrideIpcArgs !== undefined ? overrideIpcArgs(ipcArgs) : ipcArgs;
}

function debugEntryCreateBehaviorToString(arg: DebugEntryCreateBehavior) : string {
    switch (arg) {
        case DebugEntryCreateBehavior.ThrowIfExists:
            return "ThrowIfExists";
        case DebugEntryCreateBehavior.SkipIfExists:
            return "SkipIfExists";
        case DebugEntryCreateBehavior.OverwriteIfExists:
            return "OverwriteIfExists";
        default:
            return "Unspecified";
    };
}

const cbEnvironmentVariables: string[] = [
    "__CLOUDBUILD_AUTH_HELPER_CONFIG__",
    "QAUTHMATERIALROOT",
    "ARTIFACT_CREDENTIALPROVIDERS_PATH",
    "__CLOUDBUILD_AUTH_HELPER_ROOT__",
    "__Q_DPAPI_Secrets_Dir",
    "__CREDENTIAL_PROVIDER_LOG_DIR",
    "AZURE_ARTIFACTS_CREDENTIALPROVIDERS_PATH", // Cloudbuild auth helper executable path for build cache, symbol, and drop
    "Q_SESSION_GUID" // TODO: Remove after golden update (#2104026)
];

function applyCloudBuildDefaultsAndSetEnvVars(args: SymbolCreateArguments): SymbolCreateArguments {
    const defaults : SymbolCreateArguments = {
        enableTelemetry: true,
        enableCloudBuildIntegration: true,
        verbose: true,
        maxConnectRetries: 10,
        connectRetryDelayMillis: 3000,
        maxConcurrentClients: 500,
        timeoutInMilliseconds: 5 * 60 * 60 * 1000,
        warningTimeoutInMilliseconds: 4 * 60 * 60 * 1000,
    };

    return defaults.merge(args).merge(
        <OperationArguments>{
            forwardEnvironmentVars: cbEnvironmentVariables
        }
    );
}
