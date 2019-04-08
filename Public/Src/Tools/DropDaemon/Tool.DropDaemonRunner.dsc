// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

export namespace DropDaemonRunner {
    @@public
    export const tool = DropTool.tool;

    @@public
    export const dropTag = "artifact-services-drop-pip";

    // for internal use
    interface UberArguments extends DropCreateArguments, ServiceStartResult {
        ipcServerMoniker?: IpcMoniker;
    }

    /**
     * Generic DropDaemon runner.  Can be used whenever VSO authentication using Azure Active Directory is sufficient, 
     * which is typically the case when running on a developer box, or VSTS lab machine.
     */
    @@public
    export const runner: DropRunner = {
        createDrop: (
            args: DropCreateArguments
        )
        => createDrop(args),
        
        addFilesToDrop: addFiles,

        addDirectoriesToDrop: addDirectoriesToDrop,

        addArtifactsToDrop: addArtifactsToDrop,

        fileInfoToDropFileInfo: fileInfoToDropFileInfo,

        directoryInfoToDropDirectoryInfo: directoryInfoToDropDirectoryInfo,

        startDaemonNoDrop: (
            args: ServiceStartArguments
        )
        => {
            return startService(
                <UberArguments>args,
                "start-nodrop",
                "stop",
                undefined,
                true
            );
        },

        pingDaemon: (serviceInfo: ServiceStartResult, args: DropOperationArguments) => executePingAsProcess(serviceInfo, args),
        
        testReadFile: (
            serviceInfo: ServiceStartResult,
            file: File,
            args: DropOperationArguments
        )
        => {
            Contract.requires(
                file !== undefined,
                "file to read must be defined"
            );
            return executeDropdCommand(
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
    export const cloudBuildRunner: DropRunner = {
        createDrop: (
            args: DropOperationArguments
        )
        => runner.createDrop(
            applyCloudBuildDefaultsAndSetEnvVars(args)
        ),
        addFilesToDrop: runner.addFilesToDrop,
        addDirectoriesToDrop: runner.addDirectoriesToDrop,
        addArtifactsToDrop: runner.addArtifactsToDrop,
        fileInfoToDropFileInfo: runner.fileInfoToDropFileInfo,
        directoryInfoToDropDirectoryInfo: runner.directoryInfoToDropDirectoryInfo,
        startDaemonNoDrop: runner.startDaemonNoDrop,
        pingDaemon: runner.pingDaemon,
        testReadFile: runner.testReadFile,
    };

    function fileInfoToDropFileInfo(fileInfo: FileInfo): DropFileInfo { 
        return <DropFileInfo>{ 
            kind: "file", 
            file: fileInfo.file, 
            dropPath: fileInfo.dropPath 
        }; 
    }

    function directoryInfoToDropDirectoryInfo(directoryInfo: DirectoryInfo): DropDirectoryInfo { 
        return <DropDirectoryInfo>{ 
            kind: "directory", 
            directory: directoryInfo.directory, 
            dropPath: directoryInfo.dropPath 
        }; 
    }

    function startService(args: UberArguments, startCommand: string, shutdownCmdName: string, finalizationCmdName?: string, skipValidation?: boolean): ServiceStartResult {
        if (skipValidation !== true) {
            Contract.requires(
                args.service !== undefined || args.dropServiceConfigFile !== undefined,
                "drop endpoint must be defined, or must be specified in drop config file"
            );
            Contract.requires(
                args.name !== undefined || args.dropServiceConfigFile !== undefined,
                "drop name must be defined, or must be inferrable in drop config file"
            );
        }
        
        const moniker = Transformer.getNewIpcMoniker();
        
        const connectArgs = <UberArguments><DropOperationArguments>{maxConnectRetries: args.maxConnectRetries, connectRetryDelayMillis: args.connectRetryDelayMillis, ipcMoniker: moniker};
        
        const serviceStartCmd = getExecuteArguments(
            startCommand,
            overrideMoniker(args, moniker)
        );
        const shutdownCmd = getExecuteArguments(shutdownCmdName, connectArgs);
        const finalizeCmd = finalizationCmdName !== undefined 
            ? getDropdCommandIpcArguments(connectArgs, finalizationCmdName, connectArgs, overrideMustRunOnMaster)
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
            dropDaemonId: result.serviceId,
            ipcMoniker: moniker,
            outputs: result.getRequiredOutputFiles(),
        };
    }

    function createDrop(args: DropCreateArguments): DropCreateResult {
        // start service
        const shutdownCmdName = "stop";
        const finalizationCmdName = "finalize";
        const startArgs = overrideServerMoniker(
            <UberArguments>args
        );
        const dropStartResult = startService(
            startArgs,
            "start",
            shutdownCmdName,
            finalizationCmdName,
            false
        );
        
        // execute 'dropd create'
        const result = executeDropdCommand(
            dropStartResult,
            "create",
            <UberArguments>args,
            overrideMustRunOnMaster
        );
        
        // return aggregate info
        return <DropCreateResult>{serviceStartInfo: dropStartResult, outputs: result.outputs};
    }

    function addFiles(createResult: DropCreateResult, args: DropOperationArguments, fileInfos: FileInfo[]): Result {
        Contract.requires(
            fileInfos !== undefined, 
            "file to add to drop must be defined"
        );
        Contract.requires(
            createResult !== undefined, 
            "result of the 'drop create' operation must be provided"
        );

        return addArtifactsToDrop(
            createResult, 
            args, 
            fileInfos.map(fi => <DropFileInfo>{ file: fi.file, dropPath: fi.dropPath, kind: "file" }));
    }

    function addDirectoriesToDrop(createResult: DropCreateResult, args: DropOperationArguments, directoryInfos: DirectoryInfo[]): Result {
        Contract.requires(
            directoryInfos !== undefined,
            "directories to add to drop must be defined"
        );
        Contract.requires(
            createResult !== undefined,
            "result of the 'drop create' operation must be provided"
        );

        return addArtifactsToDrop(
            createResult, 
            args, 
            directoryInfos.map(di => <DropDirectoryInfo>{ directory: di.directory, contentFilter: di.contentFilter, dropPath: di.dropPath, kind: "directory" }));
    }

    function addArtifactsToDrop(createResult: DropCreateResult, args: DropOperationArguments, artifactInfos: DropArtifactInfo[]): Result {
        Contract.requires(
            artifactInfos !== undefined,
            "artifacts to add to drop must be defined"
        );
        Contract.requires(
            createResult !== undefined,
            "result of the 'drop create' operation must be provided"
        );

        if (artifactInfos.length === 0) {
            return undefined;
        }

        const fileInfos = artifactInfos.filter(a => a.kind === "file").map(a => <DropFileInfo>a);
        const directoryInfos = artifactInfos.filter(a => a.kind === "directory").map(a => <DropDirectoryInfo>a);
        Contract.assert(
            fileInfos.length + directoryInfos.length === artifactInfos.length,
            "There are artifacts of unsuported type(s)."
        );

        for (let di of directoryInfos) {
            let kind = di.directory.kind;
            Contract.requires(
                kind === "exclusive"
                || kind === "full"
                || kind === "partial"
                || kind === "shared",
                `directory of kind '${kind}' is not supported`);
        }

        const fileMessageBody = fileInfos.length !== 0
            ? [
                Cmd.options("--file ", fileInfos.map(fi => Artifact.input(fi.file))),
                Cmd.options("--dropPath ", fileInfos.map(fi => toString(fi.dropPath))),
                Cmd.options("--hash ", fileInfos.map(fi => Artifact.vsoHash(fi.file))),
                Cmd.options("--fileId ", fileInfos.map(fi => Artifact.fileId(fi.file))),
              ]
            : [];
        
        const directoryMessageBody = directoryInfos.length !== 0
            ? [
                Cmd.options("--directory ", directoryInfos.map(di => Artifact.input(di.directory))),
                Cmd.options("--directoryId ", directoryInfos.map(di => Artifact.directoryId(di.directory))),
                Cmd.options("--directoryDropPath ", directoryInfos.map(di => toString(di.dropPath))),
                Cmd.options("--directoryFilter ", directoryInfos.map(di => di.contentFilter || ".*")),
              ]
            : [];

        const uberArgs = args.merge<UberArguments>({
            dependencies: createResult.outputs || [],
            ipcServerMoniker: Transformer.getIpcServerMoniker(),
        });

        return executeDropdCommand(
            createResult.serviceStartInfo,
            "addartifacts",
            uberArgs,
            ipcArgs => ipcArgs.merge<Transformer.IpcSendArguments>({
                messageBody: [
                    ...fileMessageBody,
                    ...directoryMessageBody,
                    
                ],
                lazilyMaterializedDependencies: fileInfos.map(fi => fi.file),
            })
        );
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
    const dropLogDirectory = d`${Context.getMount("LogsDirectory").path}`;

    function overrideToolArguments(tool: Transformer.ToolDefinition, args: UberArguments): Transformer.ToolDefinition {
        return tool.override<Transformer.ToolDefinition>({
            timeoutInMilliseconds: args.timeoutInMilliseconds,
            warningTimeoutInMilliseconds: args.warningTimeoutInMilliseconds,
            untrackedDirectoryScopes: [
                ...(tool.untrackedDirectoryScopes || []),
                ...(args.untrackedDirectoryScopes || []),
                dropLogDirectory
            ],
        });
    }

    function getExecuteArguments(command: string, args: UberArguments, ...additionalCmdArgs: Argument[]): Transformer.ExecuteArguments {
        const nametag = `dropd-${command}`;
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
                dropTag,
                nametag,
                ...(args.tags || []),
            ],
            arguments: [
                Cmd.argument(command),
                Cmd.option("--moniker ", args.ipcMoniker),
                Cmd.option("--ipcServerMoniker ", args.ipcServerMoniker),
                Cmd.option("--name ", args.name),
                Cmd.option("--service ", args.service),
                Cmd.option("--batchSize ", args.batchSize),
                Cmd.option("--maxConcurrentClients ", args.maxConcurrentClients),
                Cmd.option("--maxParallelUploads ", args.maxParallelUploads),
                Cmd.option("--nagleTimeMillis ", args.nagleTimeMillis),
                Cmd.option("--retentionDays ", args.retentionDays),
                Cmd.option("--maxConnectRetries ", args.maxConnectRetries),
                Cmd.option("--connectRetryDelayMillis ", args.connectRetryDelayMillis),
                Cmd.flag("--enableCloudBuildIntegration", args.enableCloudBuildIntegration),
                Cmd.flag("--enableTelemetry", args.enableTelemetry),
                Cmd.flag("--verbose", true),
                Cmd.option("--logDir ", dropLogDirectory.path),
                ...additionalCmdArgs,
                Cmd.option(
                    "--dropServiceConfigFile ",
                    Artifact.input(args.dropServiceConfigFile)
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

    /** 
     * If an environment variable with a given name is defined, returns the value of that 
     * variable in the form of an 'EnvironmentVariable' object; otherwise returns undefined.
     */
    function getVar(varName: string): Transformer.EnvironmentVariable {
        return Environment.hasVariable(varName) ? {
            name: varName,
            value: Environment.getStringValue(varName),
        }
        : undefined;
    }
    function executePingAsProcess(serviceStartInfo: ServiceStartResult, args: DropOperationArguments): Result {
        const exeArgs = getExecuteArguments("ping", overrideMoniker(args, serviceStartInfo.ipcMoniker)).merge<Transformer.ExecuteArguments>({
            servicePipDependencies: [ serviceStartInfo.dropDaemonId ],
        });
        const result = Transformer.execute(exeArgs);
        return <Result>{outputs: result.getRequiredOutputFiles() };
    }
    function executeDropdCommand(serviceStartInfo: ServiceStartResult, command: string, args: UberArguments, overrideIpcArgs?: (a: Transformer.IpcSendArguments) => Transformer.IpcSendArguments ): Result {
        const ipcArgs = getDropdCommandIpcArguments(serviceStartInfo, command, args, overrideIpcArgs);
        const ipcResult = Transformer.ipcSend(ipcArgs);
        return <Result>{outputs: [ipcResult.outputFile]};
    }
    function getDropdCommandIpcArguments(serviceStartInfo: ServiceStartResult, command: string, args: UberArguments, overrideIpcArgs?: (a: Transformer.IpcSendArguments) => Transformer.IpcSendArguments ): Transformer.IpcSendArguments {
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
            targetService: serviceStartInfo.dropDaemonId,
            messageBody: exeArgs.arguments,
            maxConnectRetries: args.maxConnectRetries,
            connectRetryDelayMillis: args.connectRetryDelayMillis,
            fileDependencies: exeArgs.dependencies,
            outputFile: args.consoleOutput || exeArgs.consoleOutput,
        };
        return overrideIpcArgs !== undefined ? overrideIpcArgs(ipcArgs) : ipcArgs;
    }
    function toString(relPath: RelativePath): string {
        return relPath && relPath.toPathAtoms().map(
            a => a.toString()
        ).join("\\");
    }
    const cloudBuildVarsPointingToDirs = [
        "ARTIFACT_CREDENTIALPROVIDERS_PATH",
        "__CLOUDBUILD_AUTH_HELPER_ROOT__",
        "__Q_DPAPI_Secrets_Dir",
        "__CREDENTIAL_PROVIDER_LOG_DIR",
    ];
    const cloudBuildVars: string[] = [
        "__CLOUDBUILD_AUTH_HELPER_CONFIG__", 
        "QAUTHMATERIALROOT",                  // Auth material for low-privilege build.
        ...cloudBuildVarsPointingToDirs];
    /**
     * Sets the values of the 'forwardEnvironmentVars'
     * (defined in 'CommonArguments') in the given 'args' object, to correspond to the magic
     * CloudBuild environment variables.
     */
    function applyCloudBuildDefaultsAndSetEnvVars<T>(args: T): T {
        const defaults = {
            enableTelemetry: true,
            enableCloudBuildIntegration: true,
            verbose: true,
            maxConnectRetries: 10,
            connectRetryDelayMillis: 3000,
            batchSize: 500,
            maxConcurrentClients: 500,
            timeoutInMilliseconds: 5 * 60 * 60 * 1000,
            // 5 hours
            warningTimeoutInMilliseconds: 4 * 60 * 60 * 1000,
            // 4 hours
        };
        return defaults.merge<T>(args).merge<T>(
            <CommonArguments>{
                forwardEnvironmentVars: cloudBuildVars
            }
        );
    }
}
