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
        ? getIpcArguments(connectArgs, finalizationCmdName, connectArgs, overrideMustRunOnMaster)
        : undefined;
    
    const result = Transformer.createService(
        serviceStartCmd.merge<Transformer.CreateServiceArguments>({
            serviceShutdownCmd: shutdownCmd,
            serviceFinalizationCmds: finalizeCmd ? [finalizeCmd] : [],
            unsafe: {
                // only because ArtifactServices may run some external credentials provider
                hasUntrackedChildProcesses: true,
            },
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
        overrideMustRunOnMaster
    );
    
    // return aggregate info
    return <SymbolCreateResult>{serviceStartInfo: symbolStartResult, outputs: result.outputs};
}   

function addFiles(createResult: SymbolCreateResult, args: OperationArguments, files: File[]): Result {
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

    const symbolMetadataFile = indexSymbolFiles(files);

    const fileMessageBody = files.length !== 0
        ? [
            Cmd.options("--file ", files.map(fi => Artifact.input(fi))),                
            Cmd.options("--hash ", files.map(fi => Artifact.vsoHash(fi))),
            Cmd.options("--fileId ", files.map(fi => Artifact.fileId(fi))),
            Cmd.option("--symbolMetadata ", Artifact.input(symbolMetadataFile)),
            ]
        : [];      

    const uberArgs = args.merge<UberArguments>({
        dependencies: createResult.outputs || [],
        ipcServerMoniker: Transformer.getIpcServerMoniker(),
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

function indexSymbolFiles(files: File[]) : DerivedFile {
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
            Cmd.option("--symbolMetadata ", Artifact.output(outputPath)),
        ],
        unsafe: {
            // SymStoreUtil opens some files (namely .pdb) with ReadWrite access,
            // this causes DFAs and other issues.
            untrackedPaths: files
        },
        description : "SymbolIndexing: " + files[0].name + " and " + (files.length - 1) + " other file(s)"
    }; 

    // run the tool
    const result = Transformer.execute(executeArguments);

    const outputResult = result.getOutputFile(outputPath);

    return outputResult;
}

function overrideMustRunOnMaster(args: Transformer.IpcSendArguments): Transformer.IpcSendArguments {
    return args.override<Transformer.IpcSendArguments>({
        mustRunOnMaster: true
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
            Cmd.option("--retentionDays ", args.retentionDays),
            Cmd.option("--maxConnectRetries ", args.maxConnectRetries),
            Cmd.option("--connectRetryDelayMillis ", args.connectRetryDelayMillis),
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
        ],
        consoleOutput: outDir.combine(`${nametag}-stdout.txt`),
        dependencies: [
            ...(args.dependencies || []),
        ],
        environmentVariables: (args.additionalEnvironmentVars || []),
        unsafe: {
            passThroughEnvironmentVariables: (args.forwardEnvironmentVars || []),
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

function debugEntryCreateBehaviorToString(arg: DebugEntryCreateBehavior) {
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
    "__CREDENTIAL_PROVIDER_LOG_DIR"
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
