// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

/** Default tool for executing script. */
const scriptTool: Transformer.ToolDefinition = {
    exe: OS.isWindows
        ? Environment.getFileValue("COMSPEC")
        : (Environment.hasVariable("SHELL") ? Environment.getFileValue("SHELL") : f`/bin/bash`),
    dependsOnCurrentHostOSDirectories: true,
    dependsOnAppDataDirectory: true,
    prepareTempDirectory: true
};

/** Default tool for executing PowerShell script. */
const powerShellFileExe = OS.isWindows ? a`powershell.exe` : a`pwsh`;
const powerShellTool: Transformer.ToolDefinition = {
    exe: Environment.hasVariable("POWERSHELL_INSTALL_DIR")
        ? f`${Environment.getDirectoryValue("POWERSHELL_INSTALL_DIR")}/${powerShellFileExe}`
        : (OS.isWindows 
            ? f`${Context.getMount("Windows").path}/System32/WindowsPowerShell/v1.0/${powerShellFileExe}`
            : f`${Context.getMount("UsrBin").path}/${powerShellFileExe}`),
    dependsOnCurrentHostOSDirectories: true,
    dependsOnAppDataDirectory: true,
    prepareTempDirectory: true
};

/** Semaphore for running tasks in mutual exclusive way. */
@@public
export const exclusiveTaskSemaphore: Transformer.SemaphoreInfo = {
    name: "Workflow.ExclusiveTaskSemaphore",
    limit: 1,
    incrementBy: 1,
};

/** Arguments for running a task of a project. */
@@public
export interface RunTaskArguments
{
    /** Working directory. */
    workingDirectory?: Directory;

    /** Environment variables. */
    environmentVariables?: Transformer.EnvironmentVariable[];

    /** Dependencies for the workflow run. */
    dependencies?: TaskDependency[];

    /** Outputs of the run. */
    outputs?: (Path | Directory)[];

    /**
     * Redirects console output (stdout) to a file.
     *
     * Explicit redirection of stdout to a file is particularly needed because in Linux manual redirection using '>'
     * does not work currently.
     */
    consoleOutput?: Path;

    /**
     * Redirects console error (stderr) to a file.
     *
     * Explicit redirection of stderr to a file is particularly needed because in Linux manual redirection using '>'
     * does not work currently.
     */
    consoleError?: Path;

    /** Untracked scopes. */
    untrackedScopes?: Directory[];

    /** Untracked file or directory paths. */
    untrackedPaths?: (File | Directory)[];

    /**
     * Semaphores.
     *
     * Semaphores are mainly used to run tasks in mutual exclusive way.
     */
    semaphores?: Transformer.SemaphoreInfo[];

    /**
     * Disables cache lookup when true.
     *
     * Some tasks may need to execute even if the cache is up-to-date.
     */
    disableCacheLookup?: boolean;

    /**
     * Disables computing the project dependency closure for the final dependencies.
     * 
     * If project A depends on project B, and project B depends on project C, then by default
     * the final dependencies of project A will include project C. When this flag is set to true,
     * then project C will not be included in the final dependencies of project A.
     *
     * Users typically find it difficult to specify project dependency closure.
     */
    disableTransitiveProjectOutputDependency?: boolean;

    /**
     * Disables computing the task dependency closure for the final dependencies.
     * 
     * If task S depends on task T, and task T depends on task U, then by default
     * the final dependencies of task S will include task U's outputs. When this flag is set to true,
     * then task U's outputs will not be included in the final dependencies of task S.
     * 
     * The transitive dependency computation has project dependency boundary. Suppose that project P depends on project Q,
     * and the outputs of Q include the outputs of task T, which depends on task U. If task S of P says that 
     * it depends on project Q, then it will only get the outputs of task T, but not the outputs of task U.
     * 
     * Users typically find it tedious to specify task dependency closure.
     */
    disableTransitiveTaskOutputDependency?: boolean;

    /**
     * Enables strict file monitoring when set to true.
     *
     * When strict file monitoring is enabled, the task is only allowed to access files that are specified
     * statically as dependencies, either through direct file/directory dependencies or through task/project
     * dependencies.
     * 
     * Implementation-wise, strict file monitoring disallows reads of undeclared files. In the future, this
     * flag may be used to make more restrictions (or to loosen up some restrictions), and hence the name is
     * made generic.
     */
    strictFileMonitoring?: boolean;

    /**
     * Names of process names that can break away from the sandbox when spawned by a task.
     */
    breakawayProcesses?: PathAtom[];

    /**
     * Names of child processes that can survive after the task execution and are allowed to be cleaned up by the sandbox.
     */
    allowedSurvivingChildProcessNames?: (PathAtom | string)[];

    /**
     * Environment variables that need to be passthrough
     * These environment variables will not be in process' fingerprint
    */
    passThroughEnvironmentVariables?: (string | Transformer.EnvironmentVariable)[];

    /** Tags for the task. */
    tags?: string[];

    /** Description of the task. */
    description?: string;
}

/**
 * Arguments for running a task.
 * 
 * This type makes writing task argument more convenient (or more natural). For example, instead of writing
 *     arguments: [                                // (1)
 *         Cmd.argument("foo"),
 *         Cmd.option("-m ", "bar"),
 *         Cmd.argument(file)
 *     ],
 * users can write as in other programming language as follows:
 *     arguments: ["foo", "-m", "bar", file]       // (2)
 * 
 * Given that they type Argument is also part of the type TaskArguments, users can also write the arguments
 * as in (1).
 */
@@public
export type TaskArguments = (string | Path | File | Directory | Argument)[];

/** Arguments for running a simple task consisting of an executable and an argument list. */
@@public
export interface RunSimpleTaskArguments extends RunTaskArguments
{
    /** Executable. */
    executable: File;

    /** Arguments. */
    arguments: TaskArguments;
}

/**
 * Arguments for running a script task.
 *
 * A script task can run a batch script, a PowerShell script, or a Bash script. Batch script is only supported on Windows,
 * and Bash script is only supported on Linux. PowerShell script is supported on both Windows and Linux.
 *
 * A script task can be run by either a script file or an inline script. The arguments property is only used
 * when scriptFile is defined. When scriptFile is undefined, the arguments property is ignored.
 */
@@public
export interface RunScriptTaskArguments extends RunTaskArguments
{
    /** Inline script. */
    script?: string[];

    /** Script file. */
    scriptFile?: File;

    /** Arguments, used when scriptFile is defined. */
    arguments?: TaskArguments;
}

/** Runs user specified workflow, given a repo configuration, on a project data, and returns a project output */
@@public
export function run(repoConfig: RepoConfig, project: Project, workflow: (c: RepoConfig, p: Project) => WorkflowOutput) : ProjectOutput
{
    const workflowOutput = workflow(repoConfig, project);
    return { 
        outputs: workflowOutput,
        references: [
            ...(project.buildtimeDeps || []),
            ...(project.runtimeDeps || []),
            ...(project.runtimeDevDeps || [])
        ].filter(d => d !== undefined)
    };
}

/**
 * Empty workflow outputs.
 * 
 * Empty workflow outputs are useful when a workflow does not produce any output.
 */
@@public
export const none: WorkflowOutput = [];

/** Runs a simple task in a workflow. */
@@public
export function runTask(runArgs: RunSimpleTaskArguments) : TaskOutput
{
    const tool = {
        exe: runArgs.executable,
        dependsOnCurrentHostOSDirectories: true,
        dependsOnAppDataDirectory: true,
        prepareTempDirectory: true
    };
    const arguments = getTransformerArguments(runArgs.arguments);
    const workingDirectory = runArgs.workingDirectory || Context.getNewOutputDirectory("wd");
    return runTaskCore(tool, arguments, workingDirectory, runArgs); 
}

/** 
 * Runs a script task.
 *
 * On Windows the script is executed by cmd.exe. On Linux the script is executed by bash.
 */
@@public
export function runScriptTask(runArgs: RunScriptTaskArguments) : TaskOutput
{
    const scriptFile = runArgs.script !== undefined ? createScriptFileFromInlineScript(runArgs.script) : runArgs.scriptFile;
    const arguments = createArgumentsForScript(scriptFile, runArgs.scriptFile !== undefined ? runArgs.arguments : undefined);
    const workingDirectory = runArgs.workingDirectory || d`${scriptFile.path.parent}`;
    return runTaskCore(scriptTool, arguments, workingDirectory, runArgs);
}

/** Runs a PowerShell task. */
@@public
export function runPowerShellTask(runArgs: RunScriptTaskArguments) : TaskOutput
{
    const scriptFile = runArgs.script !== undefined ? createPowerShellScriptFileFromInlineScript(runArgs.script) : runArgs.scriptFile;
    const arguments = createArgumentsForPowerShellScript(scriptFile, runArgs.scriptFile !== undefined ? runArgs.arguments : undefined);
    const workingDirectory = runArgs.workingDirectory || d`${scriptFile.path.parent}`;
    const psHome = d`/opt/microsoft/powershell/7`;
    const defaultUntrackedScopes = OS.isWindows 
        ? Environment.getDirectoryValues("PSModulePath", ";")
        : [
            d`${psHome}/Modules`,
            d`/usr/local/share/powershell/Modules`,
            d`${Context.getMount("UserProfile").path}/.local/share/powershell/Modules`,
            d`${Context.getMount("UserProfile").path}/.local/share/powershell/PSReadLine`,
            d`${Context.getMount("UserProfile").path}/.config/powershell`,
          ];
    runArgs = runArgs.merge({ untrackedScopes: defaultUntrackedScopes });
    return runTaskCore(powerShellTool, arguments, workingDirectory, runArgs);
}

/**
 * Empty task output.
 *
 * Empty task output is useful when writing task dependency on optional tasks. For example, one can write
 *     const task1 = condition ? runTask(...) : undefined;
 *     const task2 = runTask({ dependencies: [task1] });
 * Although dependencies property can have an undefined value, there is nothing preventing users from passing task1
 * to another function that does not handle the undefined value. To avoid this problem, users can write
 *     const task1 = condition ? runTask(...) : noTask;
 *     const task2 = runTask({ dependencies: [task1] });
 */
@@public
export const noTask: TaskOutput = { taskOutputs: [] };

function runTaskCore(tool: Transformer.ToolDefinition, arguments: Argument[], workingDirectory: Directory, runArgs: RunTaskArguments) : TaskOutput
{
    // Ensure that the task has an output:
    // - use the specified outputs if they are defined.
    // - or, use the repo root if a writable "RepoRoot" is declared,
    // - otherwise, use the working directory.
    const outputPaths = runArgs.outputs !== undefined && runArgs.outputs.length > 0
        ? runArgs.outputs
        : (Context.hasMount("RepoRoot") ? [d`${Context.getMount("RepoRoot").path}`] : [workingDirectory]);
    const outputs = getOutputsFromPaths(outputPaths);
    const dependencies = flattenTaskDependencies(
        runArgs.disableTransitiveProjectOutputDependency === true,
        runArgs.disableTransitiveTaskOutputDependency === true,
        ...(runArgs.dependencies || []));

    const result = Transformer.execute({
        tool: tool,
        arguments: arguments,
        workingDirectory: workingDirectory,
        outputs: outputs,
        consoleOutput: runArgs.consoleOutput,
        consoleError: runArgs.consoleError,
        dependencies: dependencies,
        tempDirectory: Context.getTempDirectory(undefined),
        environmentVariables: Env.removeTemp(runArgs.environmentVariables),
        tags: runArgs.tags,
        enforceWeakFingerprintAugmentation: true,
        preservePathSetCasing: true,
        doubleWritePolicy: "allowSameContentDoubleWrites",
        sourceRewritePolicy: "safeSourceRewritesAreAllowed",
        allowUndeclaredSourceReads: runArgs.strictFileMonitoring !== true,
        disableCacheLookup: runArgs.disableCacheLookup,
        acquireSemaphores: runArgs.semaphores,
        allowedSurvivingChildProcessNames: runArgs.allowedSurvivingChildProcessNames,
        unsafe: {
            requireGlobalDependencies: true,
            untrackedScopes: runArgs.untrackedScopes,
            untrackedPaths: runArgs.untrackedPaths,
            childProcessesToBreakawayFromSandbox: runArgs.breakawayProcesses,
            passThroughEnvironmentVariables: runArgs.passThroughEnvironmentVariables
        },
        description: runArgs.description
    });

    const taskOutput = { taskOutputs: [...result.getOutputFiles(), ...result.getOutputDirectories()] };
    return augmentTaskOutputWithReferences(taskOutput, runArgs.dependencies);
}

/**
 * Runs Transformer.Execute as a task.
 *
 * This function is useful when users need the full power of Transformer.Execute. This function also
 * allows for substitutability, i.e., users can run Transformer.Execute as a task dependency.
 */
@@public
export function runTransformerTask(arguments: Transformer.ExecuteArguments) : TaskOutput
{
    const result = Transformer.execute(arguments);
    return { taskOutputs: [ ...result.getOutputFiles(), ...result.getOutputDirectories() ] };
}

/**
 * Converts task arguments to Transformer arguments.
 *
 * When creating tasks, instead of creating Transformer's argument values or using Cmd SDK, users can specify task arguments
 * simply as strings, paths, directories, or files. This function converts those values to Transformer argument values. In
 * the conversion, paths, directories, and files are converted to Artifact.none.
 */
function getTransformerArguments(arguments: TaskArguments) : Argument[]
{
    return arguments.map(a => {
        const t = typeof(a);
        if (t === "string") return Cmd.argument(<string>a);
        if (t === "Path") return Cmd.argument(Artifact.none(<Path>a));
        if (t === "Directory") return Cmd.argument(Artifact.none(<Directory>a));
        if (t === "File") return Cmd.argument(Artifact.none(<File>a));
        return <Argument>a;
    });
}

/**
 * Converts user-specified output paths to Transformer output values.
 *
 * When creating tasks, users can specify outputs simply as paths (for output files) or directories (for output directories).
 * This function converts the directories into shared opaque directories. In this way, users do not need to be aware of
 * the (shared) opaque directory abstraction.
 */
function getOutputsFromPaths(outputPaths: (Path | Directory)[]) : Transformer.Output[]
{
    return outputPaths.map(o =>  typeof o === "Directory"
        ? <Transformer.DirectoryOutput>{ kind: "shared", directory: <Directory>o }
        : <Path>o);
}

/** Creates a script file from an inline script. */
function createScriptFileFromInlineScript(content: string[]) : File
{
    const outputDir = Context.getNewOutputDirectory("script");
    const script = OS.isWindows
        ? [
            "@ECHO OFF",
            ...content,
            "IF %ERRORLEVEL% NEQ 0 (",
            "    EXIT /b %ERRORLEVEL%",
            ")",
            "EXIT /B 0"
          ]
        : [
            "#!/bin/bash",
            "",
            ...content,
            "exitcode=$?",
            "if [ $exitcode -ne 0 ]; then",
            "    exit $exitcode",
            "fi",
            "exit 0"
          ];
    const scriptFile = Transformer.writeAllLines(p`${outputDir}/script.cmd`, script);
    return OS.isWindows
        ? scriptFile
        : Transformer.makeExecutable(scriptFile, p`${outputDir}/script.sh`);
}

/** Creates arguments for batch/bash script file. */
function createArgumentsForScript(scriptFile: File, taskArguments?: TaskArguments) : Argument[]
{
    const arguments = getTransformerArguments(taskArguments || []);
    const scriptArguments = OS.isWindows
        ? [
            Cmd.argument("/D"),
            Cmd.argument("/C"),
            Cmd.argument(Artifact.input(scriptFile))
          ]
        : [Cmd.argument(Artifact.input(scriptFile))];
    return [...scriptArguments, ...arguments];
}

/** Creates a PowerShell script file from an inline script. */
function createPowerShellScriptFileFromInlineScript(content: string[]) : File
{
    const outputDir = Context.getNewOutputDirectory("script");
    return Transformer.writeAllLines(p`${outputDir}/script.ps1`, content);
}

/** Creates arguments for PowerShell script file. */
function createArgumentsForPowerShellScript(scriptFile: File, taskArguments?: TaskArguments) : Argument[]
{
    const arguments = getTransformerArguments(taskArguments || []);
    return [
        Cmd.argument   ("-NonInteractive"),
        Cmd.argument   ("-NoProfile"),
        Cmd.option     ("-ExecutionPolicy ", "Unrestricted"),
        Cmd.argument   ("-Command"),
        Cmd.rawArgument("\"&"),
        Cmd.argument   (Artifact.input(scriptFile)),
        ...arguments,
        Cmd.rawArgument("\"")
    ];
}

/**
 * Gets the NuGet dependency closure of a project.
 */
@@public
export function getProjectNuGetDependencies(project: Project) : NuGetReference[]
{
    const closure = getProjectDependencyClosure(project);
    const references = [
        ...(project.buildtimeDeps || []),
        ...(project.runtimeDeps || []),
        ...(project.runtimeDevDeps || []),
        ...closure.mapMany(p => p.references)
    ];
    const nugetReferences = references.filter(d => d !== undefined && d["kind"] === "NuGet").map(d => <NuGetReference>d);
    const map = Map.empty<string, NuGetReference>().addRange(...nugetReferences.map<[string, NuGetReference]>(r => [r.name, r]));
    return map.values();
}