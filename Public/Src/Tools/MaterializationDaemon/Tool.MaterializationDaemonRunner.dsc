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
            overrideServerMoniker(<CombinedArguments>applyDefaults(args)),
            "start",
            "stop",
            "finalize"
        );
    },
    
    loadManifestsAndMaterializeFiles: registerManifest,

    materializeOutputDirectories: materializeDirectories,
};

function startService(args: CombinedArguments, startCommand: string, shutdownCmdName: string, finalizationCmdName: string): ServiceStartResult {
    const moniker = Transformer.getNewIpcMoniker();

    const connectArgs = <CombinedArguments><ConnectionArguments>{maxConnectRetries: args.maxConnectRetries, connectRetryDelayMillis: args.connectRetryDelayMillis, ipcMoniker: moniker};

    const serviceStartCmd = getExecuteArguments(
        startCommand,
        overrideMoniker(args, moniker)
    );
    const shutdownCmd = getExecuteArguments(shutdownCmdName, connectArgs);
    const finalizeCmd = getIpcArguments(
        connectArgs, 
        finalizationCmdName, 
        connectArgs, 
        ipcArgs => ipcArgs.merge<Transformer.IpcSendArguments>({
            mustRunOnMaster: true
        }));

    const result = Transformer.createService(
        serviceStartCmd.merge<Transformer.CreateServiceArguments>({
            serviceShutdownCmd: shutdownCmd,
            serviceFinalizationCmds: [finalizeCmd],
            unsafe: {
                hasUntrackedChildProcesses: true,
            },
        })
    );
    
    return <ServiceStartResult>{
        materializationDaemonId: result.serviceId,
        ipcMoniker: moniker,
        outputs: result.getRequiredOutputFiles(),
    };
}

function registerManifest(startResult: ServiceStartResult, args : ConnectionArguments, directories: ManifestFileDirectory[]): Result {
    Contract.requires(
        startResult !== undefined,
        "result of starting the daemon must be provided"
    );

    const directoryMessageBody = directories.length !== 0
        ? [
            Cmd.options("--directory ", directories.map(dir => Artifact.input(dir.directory))),
            Cmd.options("--directoryId ", directories.map(dir => Artifact.directoryId(dir.directory))),
            Cmd.options("--directoryFilter ", directories.map(dir => dir.contentFilter || ".*")),
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
                ...directories.map(d => d.directory),
            ],
            // The daemon is about materialization on the orchestrator machine, so naturually, all the IPC pips run there.
            mustRunOnMaster: true
        })
    );
}

function materializeDirectories(startResult: ServiceStartResult, args : ConnectionArguments, directories: FilteredOpaqueDirectory[]): Result {
    Contract.requires(
        startResult !== undefined,
        "result of starting the daemon must be provided"
    );

    const directoryMessageBody = directories.length !== 0
    ? [
        Cmd.options("--directory ", directories.map(dir => Artifact.input(dir.directory))),
        Cmd.options("--directoryId ", directories.map(dir => Artifact.directoryId(dir.directory))),
        Cmd.options("--directoryFilter ", directories.map(dir => (dir.contentFilter && dir.contentFilter.regex) || ".*")),
        Cmd.options("--directoryFilterKind ", directories.map(dir => (dir.contentFilter && dir.contentFilter.kind) || "Include")),
      ]
    : [];

    const combinedArgs = args.merge<CombinedArguments>({
        ipcServerMoniker: Transformer.getIpcServerMoniker(),
    });

    return executeDaemonCommand(
        startResult,
        "materializeDirectories",
        combinedArgs,
        ipcArgs => ipcArgs.merge<Transformer.IpcSendArguments>({
            messageBody: [
                ...directoryMessageBody,
            ],
            lazilyMaterializedDependencies: [
                // We might not need all the content of the idrectories, so don't materialize anything on the engine side
                // and let the daemon be in charge of materialization.
                ...directories.map(d => d.directory),
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
            Cmd.option("--parserExe ", args.manifestParser !== undefined ? args.manifestParser.exePath : undefined),
            Cmd.option("--parserExeArgs ", args.manifestParser !== undefined ? args.manifestParser.additionalCommandLineArguments : undefined),
            Cmd.option("--maxConnectRetries ", args.maxConnectRetries),
            Cmd.option("--connectRetryDelayMillis ", args.connectRetryDelayMillis),
            Cmd.option("--logDir ", materializationDaemonLogDirectory.path),
        ],
        consoleOutput: outDir.combine(`${nametag}-stdout.txt`),
        dependencies: [
            ...(args.dependencies || []),
        ],
        unsafe: {
            passThroughEnvironmentVariables: (args.forwardEnvironmentVars || []),
        },
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

function applyDefaults(args: ServiceStartArguments): ServiceStartArguments {
    const defaults : ServiceStartArguments = {
        maxConnectRetries: 10,
        connectRetryDelayMillis: 3000,
        timeoutInMilliseconds: 5 * 60 * 60 * 1000,
        warningTimeoutInMilliseconds: 4 * 60 * 60 * 1000,

        maxDegreeOfParallelism: 100,
        forwardEnvironmentVars: ["_NTTREE"]
    };

    return defaults.merge(args);
}
