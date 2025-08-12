// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

const daemonTag = "blob-daemon-pip";

// for internal use
interface CombinedArguments extends ServiceStartArguments, ServiceStartResult {
    ipcServerMoniker?: IpcMoniker;
}

@@public
export const runner: BlobRunner = {
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
    
    uploadArtifacts : uploadArtifactsInternal,
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
            mustRunOnOrchestrator: true
        }));

    const result = Transformer.createService(
        serviceStartCmd.merge<Transformer.CreateServiceArguments>({
            serviceShutdownCmd: shutdownCmd,
            serviceFinalizationCmds: [finalizeCmd],
            unsafe: {
                hasUntrackedChildProcesses: true,
            },
            serviceTrackableTag: daemonTag,
            serviceTrackableTagDisplayName: "BlobDaemonTrackerOverhangMs",
            moniker: moniker
        })
    );
    
    return <ServiceStartResult>{
        blobDaemonId: result.serviceId,
        ipcMoniker: moniker,
        outputs: result.getRequiredOutputFiles(),
    };
}

function uploadArtifactsInternal(startResult: ServiceStartResult, args: ConnectionArguments, artifacts: BlobArtifactInfo[]): Result {
    Contract.assert(
        startResult !== undefined,
        "startResult must be defined"
    );

    Contract.assert(
        artifacts !== undefined,
        "artifacts must be defined"
    );
   
    const files = artifacts.filter(a => a.kind === "file").map(a => <BlobFileInfo>a);
    const directories = artifacts.filter(a => a.kind === "directory").map(a => <BlobDirectoryInfo>a);

    // Validate user input

    Contract.assert(
       artifacts.all(artifact => 
        {
            return artifact.authEnvironmentVariable !== undefined;
        }),
        "The name of the environment variable that contains authentication information must be specified for all artifacts."
    );

    Contract.assert(
       artifacts.all(artifact => 
        {
            return artifact.uploadLocation !== undefined;
        }),
        "The upload location must be specified for all artifacts."
    );
    
    Contract.assert(
        files.length + directories.length === artifacts.length,
        "There are artifacts of unsupported type(s)."
    );

    const fileMessageBody = files.length !== 0
        ? [
            Cmd.options("--file ", files.map(fi => Artifact.input(fi.file))),
            Cmd.options("--fileId ", files.map(fi => Artifact.fileId(fi.file))),
            Cmd.options("--hash ", files.map(fi => Artifact.vsoHash(fi.file))),
            Cmd.options("--fileTarget ", files.map(fi => serializeUploadLocation(fi.uploadLocation))),
            Cmd.options("--fileAuthVar ", files.map(fi => fi.authEnvironmentVariable)),
          ]
        : [];

    const directoryMessageBody = directories.length !== 0
        ? [
            Cmd.options("--directory ", directories.map(di => Artifact.input(di.directory))),
            Cmd.options("--directoryId ", directories.map(di => Artifact.directoryId(di.directory))),
            Cmd.options("--directoryFilter ", directories.map(di => di.contentFilter || ".*")),
            Cmd.options("--directoryFilterUseRelativePath ", directories.map(di => optionalBooleanToString(di.applyContentFilterToRelativePath))),
            Cmd.options("--directoryRelativePathReplace ", directories.map(di => serializeRelativePathReplace(di.relativePathReplacementArguments))),
            Cmd.options("--directoryTarget ", directories.map(di => serializeUploadLocation(di.uploadLocation))),
            Cmd.options("--directoryAuthVar ", directories.map(di => di.authEnvironmentVariable)),
            ]
        : [];

    const combinedArgs = args.merge<CombinedArguments>({
        ipcServerMoniker: Transformer.getIpcServerMoniker(),
    });

    return executeDaemonCommand(
        startResult,
        "uploadArtifacts",
        combinedArgs,
        ipcArgs => ipcArgs.merge<Transformer.IpcSendArguments>({
            messageBody: [
                ...fileMessageBody,
                ...directoryMessageBody,
            ],
            lazilyMaterializedDependencies: [
                // output of a 'create' command is only used as an order dependency
                ...startResult.outputs,
                // inputs are not materialized; the daemon is in charge of requesting materialization of artifacts it needs
                ...files.map(fi => fi.file),
                ...directories.map(di => di.directory)
            ],
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
const blobDaemonLogDirectory = d`${Context.getMount("LogsDirectory").path}`;

function overrideToolArguments(tool: Transformer.ToolDefinition, args: CombinedArguments): Transformer.ToolDefinition {
    return tool.override<Transformer.ToolDefinition>({
        timeoutInMilliseconds: args.timeoutInMilliseconds,
        warningTimeoutInMilliseconds: args.warningTimeoutInMilliseconds,
        untrackedDirectoryScopes: [
            ...(tool.untrackedDirectoryScopes || []),
            ...(args.untrackedDirectoryScopes || []),
            blobDaemonLogDirectory
        ],
    });
}

function getExecuteArguments(command: string, args: CombinedArguments): Transformer.ExecuteArguments {
    const nametag = `bd-${command}`;
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
            daemonTag,
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
            Cmd.option("--logDir ", blobDaemonLogDirectory.path),
        ],
        consoleOutput: outDir.combine(`${nametag}-stdout.txt`),
        dependencies: [
            ...(args.dependencies || []),
        ],
        unsafe: {
            passThroughEnvironmentVariables: [ 
                ...(args.forwardEnvironmentVars || []),
                // Remove after A/B testing
                "BuildXLEnableGrpcIpc" 
            ],
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
        targetService: serviceStartInfo.blobDaemonId,
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
    };

    return defaults.merge(args);
}

function serializeRelativePathReplace(replacementArgs: RelativePathReplacementArguments): string {
    // we need to use a char that cannot appear in url/uri
    const delim = '#';

    if (replacementArgs !== undefined) {
        return `${delim}${replacementArgs.oldValue}${delim}${replacementArgs.newValue}${delim}`;
    }

    return `${delim}${delim}`;
}

function serializeUploadLocation(uploadLocation: UploadLocation): string {
    // we need to use a char that cannot appear in url/uri
    const delim = '#';

    Contract.assert(
        uploadLocation !== undefined,
        "uploadLocation must be defined"
    );

    Contract.assert(
        uploadLocation.kind === "uri" || uploadLocation.kind === "container",
        "uploadLocation must be of type UploadLocationUri or UploadLocationContainer"
    );

    if (uploadLocation.kind === "uri") {
        const uri = uploadLocation as UploadLocationUri;
        return `${delim}${uri.kind}${delim}${uri.uri}${delim}`;
    } else if (uploadLocation.kind === "container") {
        const container = uploadLocation as UploadLocationContainer;
        return `${delim}${container.kind}${delim}${container.accountName}${delim}${container.containerName}${delim}${container.uploadPath}${delim}`;
    }

    // should never happen
    return undefined;
}

function optionalBooleanToString(value?: boolean): string {
    if (value === undefined) {
        return "false";
    }

    return value.toString();
}