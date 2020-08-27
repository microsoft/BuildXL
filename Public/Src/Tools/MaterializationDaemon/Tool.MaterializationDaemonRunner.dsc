// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

// for internal use
interface CombinedArguments extends ServiceStartArguments, ServiceStartResult {
    ipcServerMoniker?: IpcMoniker;
}

@@public
export const runner: MaterializationRunner = {
    startDaemon: (
        args: ServiceStartArguments
    )
    => {
        return startService(
            overrideServerMoniker(<CombinedArguments>args),
            "start",
            "stop"
        );
    },
    
    loadManifestsAndMaterializeFiles: registerManifest,
};

function startService(args: CombinedArguments, startCommand: string, shutdownCmdName: string): ServiceStartResult {
    const moniker = Transformer.getNewIpcMoniker();

    const connectArgs = <CombinedArguments><ConnectionArguments>{maxConnectRetries: args.maxConnectRetries, connectRetryDelayMillis: args.connectRetryDelayMillis, ipcMoniker: moniker};

    const serviceStartCmd = getExecuteArguments(
        startCommand,
        overrideMoniker(args, moniker)
    );
    const shutdownCmd = getExecuteArguments(shutdownCmdName, connectArgs);

    const result = Transformer.createService(
        serviceStartCmd.merge<Transformer.CreateServiceArguments>({
            serviceShutdownCmd: shutdownCmd,
            serviceFinalizationCmds: [],
        })
    );
    
    return <ServiceStartResult>{
        materializationDaemonId: result.serviceId,
        ipcMoniker: moniker,
        outputs: result.getRequiredOutputFiles(),
    };
}

function registerManifest(startResult: ServiceStartResult, args : ConnectionArguments, directories: SharedOpaqueDirectory[]): Result {
    Contract.requires(
        startResult !== undefined,
        "result of starting the daemon must be provided"
    );

    const directoryMessageBody = directories.length !== 0
        ? [
            Cmd.options("--directory ", directories.map(dir => Artifact.input(dir))),
            Cmd.options("--directoryId ", directories.map(dir => Artifact.directoryId(dir))),            
          ]
        : []; 

    const combinedArgs = args.merge<CombinedArguments>({
        ipcServerMoniker: Transformer.getIpcServerMoniker(),
    });

    return executeDaemonCommand(
        startResult,
        "registerManifest",
        combinedArgs,
        ipcArgs => ipcArgs.merge<Transformer.IpcSendArguments>({
            messageBody: [
                ...directoryMessageBody,
            ],
            lazilyMaterializedDependencies: [
                // Current contract is that input SODs contain only manifest files.
                // To be extra safe, we are not going to materialize anything, and let the daemon be in charge of manifest materialization.
                ...directories,
            ],
            // The daemon is about materialization on the orchestrator machine, so naturually, all the IPC pips run there.
            mustRunOnMaster: true
        })
    );
}

function overrideServerMoniker<T>(args: T): T {
    return args.override<T>({
        ipcServerMoniker: Transformer.getIpcServerMoniker(),
    });
}

function overrideMoniker(args: Object, moniker: IpcMoniker): CombinedArguments {
    return args.override<CombinedArguments>({ipcMoniker: moniker});
}

// Any use of this directory should be in untracked scope.
// BuildXL creates a junction with stable name to the real log directory. If this directory gets tracked,
// the junction will be tracked. Since the junction's target changes build over build, incremental scheduling
// will be jeopardized.
const materializationDaemonLogDirectory = d`${Context.getMount("LogsDirectory").path}`;

function overrideToolArguments(tool: Transformer.ToolDefinition, args: CombinedArguments): Transformer.ToolDefinition {
    return tool.override<Transformer.ToolDefinition>({
        timeoutInMilliseconds: args.timeoutInMilliseconds,
        warningTimeoutInMilliseconds: args.warningTimeoutInMilliseconds,
        untrackedDirectoryScopes: [
            ...(tool.untrackedDirectoryScopes || []),
            ...(args.untrackedDirectoryScopes || []),
            materializationDaemonLogDirectory
        ],
    });
}

function getExecuteArguments(command: string, args: CombinedArguments): Transformer.ExecuteArguments {
    const nametag = `mmd-${command}`;
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
            nametag,
            ...(args.tags || []),
        ],
        arguments: [
            Cmd.argument(command),
            Cmd.option("--moniker ", args.ipcMoniker),
            Cmd.option("--ipcServerMoniker ", args.ipcServerMoniker),            
            Cmd.option("--maxDegreeOfParallelism ", args.maxDegreeOfParallelism),            
            Cmd.option("--maxConnectRetries ", args.maxConnectRetries),
            Cmd.option("--connectRetryDelayMillis ", args.connectRetryDelayMillis),
            Cmd.option("--logDir ", materializationDaemonLogDirectory.path),            
        ],
        consoleOutput: outDir.combine(`${nametag}-stdout.txt`),
        dependencies: [
            ...(args.dependencies || []),
        ],
    };
}

function executeDaemonCommand(serviceStartInfo: ServiceStartResult, command: string, args: CombinedArguments, overrideIpcArgs?: (a: Transformer.IpcSendArguments) => Transformer.IpcSendArguments ): Result {
    const ipcArgs = getIpcArguments(serviceStartInfo, command, args, overrideIpcArgs);
    const ipcResult = Transformer.ipcSend(ipcArgs);
    return <Result>{outputs: [ipcResult.outputFile]};
}

function getIpcArguments(serviceStartInfo: ServiceStartResult, command: string, args: CombinedArguments, overrideIpcArgs?: (a: Transformer.IpcSendArguments) => Transformer.IpcSendArguments ): Transformer.IpcSendArguments {
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
        targetService: serviceStartInfo.materializationDaemonId,
        messageBody: exeArgs.arguments,
        maxConnectRetries: args.maxConnectRetries,
        connectRetryDelayMillis: args.connectRetryDelayMillis,
        fileDependencies: exeArgs.dependencies,
        outputFile: args.consoleOutput || exeArgs.consoleOutput,
    };

    return overrideIpcArgs !== undefined ? overrideIpcArgs(ipcArgs) : ipcArgs;
}