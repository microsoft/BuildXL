// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer, Tool} from "Sdk.Transformers";
import * as Json from "Sdk.Json";

@@public
export const guardianTag = "msguardian";

const guardianInstallMutex = "BuildXL.Tools.Guardian.Install.Phase";
const guardianExecutableName : PathAtom = PathAtom.create("guardian.cmd");
const defaultGuardianToolWorkingDirectory = d`${Context.getMount("SourceRoot").path}`;
const guardianUntrackedDirectories = addIfLazy(Context.getCurrentHost().os === "win", () => [
    // Accessed by the Guardian CLI
    d`${Context.getMount("ProgramFilesX86").path}/dotnet`,
    d`${Context.getMount("ProgramFiles").path}/dotnet`,
    d`${Context.getMount("ProgramData").path}/Microsoft/NetFramework`,
    // Config files accessed by nuget during Guardian install phase
    d`${Context.getMount("ProgramFilesX86").path}/Nuget`,
    d`${Context.getMount("ProgramFiles").path}/Nuget`,
]);

/**
 * Tool definition for guardian
 * Note: Package root is untracked because we rely on the .gdn/c directory to know when a new Guardian tool is available.
 */
function getGuardianTool(guardianRoot : StaticDirectory, guardianPaths : GuardianPaths) : Transformer.ToolDefinition {
    return {
        exe: f`${guardianRoot.path}/${guardianExecutableName}`,
        description: "Microsoft Guardian",
        dependsOnWindowsDirectories: true,
        dependsOnAppDataDirectory: true,
        prepareTempDirectory: true,
        untrackedDirectoryScopes: [...guardianUntrackedDirectories, d`${guardianPaths.install}`],
        // Untracking localRepo/.gdnhistory to ignore double writes between guardian init and guardian run
        // the history in here is not important because each Guardian call gets it's own .gdnhistory file.
        // globalRepo/.gdnhistory will also be read from, but it will not be written to.
        untrackedFiles: [ f`${guardianPaths.localHistory}`, f`${guardianPaths.globalHistory}` ]
    };
}

/**
 * Schedules a guardian pip with the specified arguments.
 * 
 * Guardian is currently only supported on Windows.
 * 
 * Guardian Lifecycle:
 * - The "guardian run" command is several different Guardian commands run at once by the Guardian process.
 * - These are split up here instead of a single call because some parts are not able to execute concurrently.
 * 
 *  1. guardian init: Initializes a local .gdn directory - can be executed concurrently with other guardian calls.
 *  2. guardian install: Installs packages specified in config file - *can not* be executed concurrently.
 *      NOTE: acquires mutex: guardianInstallMutex
 *  3. guardian run: This call includes the --no-install flag, which allows BuildXL to run it concurrently. It has the following sub-phases:
 *      a. guardian clear: clears results directories
 *      b. guardian analyze: Runs analysis tools and stores raw data in .gdn/.r directory
 *      c. guardian process: Processes the data collected in the previous step into .gdn/r directory
 *      d. guardian break: Look at processed data from previous step, return bad exit code if breaking results found and export results to file.
 */
@@public
export function runGuardian(args: GuardianArguments) : Transformer.ExecuteResult {
    validateArguments(args);

    const outputDirectory = Context.getNewOutputDirectory("guardianOut");
    const guardianPaths = createGuardianPaths(outputDirectory, args.guardianPackageDirectory);
    const guardianTool = getGuardianTool(args.guardianToolRootDirectory, guardianPaths);
    let guardianDependencies : Transformer.InputArtifact[] = [args.guardianToolRootDirectory, f`${guardianPaths.globalGuardianRepo}`, args.guardianConfigFile];

    // 0. Create a Guardian settings
    const genericSettingsFile = generateGenericGuardianSettingsFile(guardianPaths);
    const installSettingsFile = generateGuardianInstallSettingsFile(guardianPaths);

    // 1. Initialize Guardian for this Guardian run
    //      - Settings files from previous step not necessary here, can be run concurrently with the WriteFile operation.
    const initializeResult = initializeGuardian(guardianTool, guardianPaths, guardianDependencies);

    // Steps below this depend on the results of step 0 and step 1
    guardianDependencies = guardianDependencies.concat([
        f`${guardianPaths.globalSettings}`,
        initializeResult.getOutputDirectory(d`${guardianPaths.localGuardianRepo}`)
    ]);

    // 2. Run Guardian Install phase
    const installResult = runGuardianInstall(args, guardianTool, installSettingsFile, guardianDependencies, guardianPaths);
    
    // 3. Guardian run to run static analysis tools specified in config file, break build if any breaking changes are found, and export results.
    guardianDependencies = guardianDependencies.concat([installResult.getOutputFile(guardianPaths.installLog.path), genericSettingsFile]);
    const guardianResult = runGuardianInternal(args, guardianTool, genericSettingsFile, guardianDependencies, guardianPaths);
    
    return guardianResult;
}

/**
 * Validates that all required arguments are provided, and no conflicting arguments are set.
 */
function validateArguments(args: GuardianArguments) : void {
    Contract.requires(args !== undefined, "Guardian arguments cannot be undefined.");
    Contract.requires(args.guardianToolRootDirectory !== undefined, "Guardian root must be set.");
    Contract.requires(args.guardianConfigFile !== undefined, "Guardian config file must be set.");
    Contract.requires(args.guardianResultFile !== undefined, "Guardian output file must be set.");
    Contract.requires(args.guardianPackageDirectory !== undefined, "Guardian tool package install directory must be set.");
    Contract.requires(args.filesToBeScanned !== undefined, "Files to be scanned by Guardian must be set.");

    if (args.fast && args.baselineFiles) {
        Contract.fail("The --fast argument is incompatible with the output baseline file argument, as this will require a full run of guardian break to generate all the results.");
    }

    if (args.noBaseline && args.baselineFiles) {
        Contract.fail("noBaseline and baselineFile cannot be specified together.");
    }

    if (args.noSuppressions && (args.suppressionFiles || args.suppressionSets)) {
        Contract.fail("noSuppressions and suppressionFiles/suppressionSets cannot be specified together.");
    }
}

/**
 * Generates a set of paths that are produced/consumed by Guardian based on a generated output directory
 * and user specified package install directory.
 */
function createGuardianPaths(outputDirectory : Directory, packageDirectory : Directory) : GuardianPaths {
    return { 
        globalGuardianRepo: p`${Context.getMount("SourceRoot").path}/.gdn`,
        localGuardianRepo: p`${outputDirectory}/.gdn`,
        config: p`${outputDirectory}/.gdn/c`,
        rawResults: p`${outputDirectory}/.r`,
        results: p`${outputDirectory}/r`,
        convertedResults: p`${outputDirectory}/rc`,
        install: p`${packageDirectory}`,
        localSettings: p`${outputDirectory}/buildxl.gdnsettings`,
        localInstallSettings: p`${outputDirectory}/buildxl_install.gdnsettings`,
        globalSettings: p`${Context.getMount("SourceRoot").path}/.gdn/.gdnsettings`,
        localHistory: p`${outputDirectory}/.gdn/internal.gdnhistory`,
        globalHistory: p`${Context.getMount("SourceRoot").path}/.gdn/internal.gdnhistory`,
        installLog: f`${outputDirectory}/install`,
    };
}

/**
 * Generates a Guardian configuration using specified paths.
 */
function generateGenericGuardianSettingsFile(guardianPaths : GuardianPaths) : File {
    const settings = {
        folders: {
            GuardianRepo: guardianPaths.localGuardianRepo,
            Config: guardianPaths.config,
            RawResults: guardianPaths.rawResults,
            Results: guardianPaths.results,
            ConvertedResults: guardianPaths.convertedResults,
            Install: guardianPaths.install
        }
    };

    return generateGuardianSettingsFile(guardianPaths.localSettings, settings, "Generate generic Guardian settings file");
}

/**
 * Generates a config file for Guardian install step. This requires a separate settings file without config, raw results, results, or converted results directories set
 * so that it uses the local .gdn repo.
 */
function generateGuardianInstallSettingsFile(guardianPaths : GuardianPaths) : File {
    const settings = {
        folders: {
            GuardianRepo: guardianPaths.localGuardianRepo,
            Install: guardianPaths.install
        }
    };
    
    return generateGuardianSettingsFile(guardianPaths.localInstallSettings, settings, "Generate Guardian install settings file");
}

/**
 * Call Json SDK to write settings file.
 */
function generateGuardianSettingsFile(outputPath : Path, settings : Object, description : string) : File {
    const options : Json.AdditionalJsonOptions = {
        pathRenderingOption: Context.getCurrentHost().os !== "win" ? "escapedBackSlashes" : "forwardSlashes"
    };

    return Json.write(outputPath, settings, "\"", [guardianTag], "Generate Guardian install settings file", options);
}

/**
 * Initialize Guardian under a new output directory for this run. This will allow BuildXL to isolate each Guardian call into it's own repository.
 */
function initializeGuardian(guardianTool : Transformer.ToolDefinition, guardianPaths : GuardianPaths, guardianDependencies : Transformer.InputArtifact[] ) : Transformer.ExecuteResult {
    return Transformer.execute({
        tool: guardianTool,
        tags: [ guardianTag ],
        arguments: [Cmd.argument("init"), Cmd.argument("--force")],
        workingDirectory: d`${guardianPaths.localGuardianRepo.parent}`,
        dependencies: guardianDependencies,
        outputs: [ d`${guardianPaths.localGuardianRepo}` ],
        description: "Guardian Initialize"
    });
}

/**
 * Runs the "guardian install" command for the tools specified by the user
 * Packages will be installed under guardianPaths.install
 * This step requires a mutex as multiple Guardian calls may try to install at the same time to the same directory.
 */
function runGuardianInstall(args : GuardianArguments, guardianTool : Transformer.ToolDefinition, settingsFile : File, guardianDependencies : Transformer.InputArtifact[], guardianPaths : GuardianPaths) : Transformer.ExecuteResult {
    const arguments : Argument[] = [
        Cmd.argument("install"),
        Cmd.option("--settings-file ", settingsFile.path),
        Cmd.option("--config ", args.guardianConfigFile.path),
        Cmd.option("--logger-filepath ", guardianPaths.installLog.path)
    ];

    guardianDependencies = guardianDependencies.push(settingsFile);

    return Transformer.execute({
        tool: guardianTool,
        tags: [ guardianTag ],
        arguments: arguments,
        workingDirectory: d`${guardianPaths.localGuardianRepo.parent}`,
        dependencies: guardianDependencies,
        outputs: [ guardianPaths.installLog ],
        acquireMutexes: [ guardianInstallMutex ],
        description: "Guardian Install"
    });
}

/**
 * Performs a Guardian run operation. Uses the --no-install argument to skip installing, since this was already done in a previous call.
 * See block comment on the runGuardian function to see which steps this call will perform.
 */
function runGuardianInternal(args : GuardianArguments, guardianTool : Transformer.ToolDefinition, settingsFile : File, guardianDependencies : Transformer.InputArtifact[], guardianPaths : GuardianPaths) : Transformer.ExecuteResult {
    const arguments: Argument[] = [
        Cmd.argument("run"),
        Cmd.argument("--no-install"),
        Cmd.option("--settings-file ", settingsFile.path),
        Cmd.option("--config ", args.guardianConfigFile.path),
        Cmd.option("--export-breaking-results-to-file ", args.guardianResultFile.path),
        Cmd.option("--logger-filepath ", args.loggerPath && args.loggerPath.path),
        Cmd.flag("--analyze-fast", args.fast),
        Cmd.flag("--no-baseline", args.noBaseline),
        Cmd.option("--baseline-file ", Cmd.join(" ", args.baselineFiles && args.baselineFiles.map(e => e.path))),
        Cmd.flag("--no-suppressions", args.noSuppressions),
        Cmd.option("--suppression-file ", Cmd.join(" ", args.suppressionFiles && args.suppressionFiles.map(e => e.path))),
        Cmd.options("--suppression-set ", args.suppressionSets),
        Cmd.flag("--no-policy", args.noPolicy),
        Cmd.option("--policy ", args.policy)
    ];

    // Dependencies
    guardianDependencies = guardianDependencies.concat(args.filesToBeScanned);
    
    if (args.baselineFiles) {
        guardianDependencies = guardianDependencies.concat(args.baselineFiles);
    }

    if (args.suppressionFiles) {
        guardianDependencies = guardianDependencies.concat(args.suppressionFiles);
    }
    
    // Outputs
    // If no errors are generated, then no output file will be generated.
    const maybeExportFile : Transformer.FileOrPathOutput = { existence: "optional", artifact: f`${args.guardianResultFile.path}` };

    // Log file only generated if caller sets args.loggerPath
    const maybeLogFile : Transformer.FileOrPathOutput = args.loggerPath
        ? { existence: "optional", artifact: f`${args.loggerPath.path}` }
        : undefined;

    let outputs : Transformer.Output[] = [
        maybeExportFile,
        d`${guardianPaths.rawResults}`,
        d`${guardianPaths.results}`,
        d`${guardianPaths.convertedResults}`
    ];

    if (maybeLogFile) {
        outputs = outputs.push(maybeLogFile);
    }

    // Run Guardian
    const result : Transformer.ExecuteResult = Transformer.execute({
        tool: guardianTool,
        tags: [guardianTag],
        arguments: arguments,
        workingDirectory: args.guardianToolWorkingDirectory ? args.guardianToolWorkingDirectory : defaultGuardianToolWorkingDirectory,
        dependencies: guardianDependencies,
        outputs: outputs,
        description: "Guardian Run"
    });

    return result;
}


/**
 * Set of guardian arguments. See notes on each argument for any special considerations that need to be
 * taken before using them.
 */
@@public
export interface GuardianArguments extends Transformer.RunnerArguments {
    /** Root directory for Guardian install (contains guardian.cmd)
     ** Note: Guardian will still read/write to this directory for updates to itself
     **       or for tool updates, so this should be partially sealed.
     **
     ** Under the guardian root directory the guardian tool will:
     **  - Get new versions of itself to ./versions/, and run the latest Guardian Cli from here
     **  - Get the latest versions of tools being run under ./packages/
     **  - Read ./.gdnversion to check the current Guardian version to check whether it should be updated
     **  - Potentially write temporary files to ./.tmp/ */
    guardianToolRootDirectory: StaticDirectory;
    /** A configuration file to run with guardian */
    guardianConfigFile: File;
    /** Path to export guardian result file
     ** Note: No export file will be generated if guardian executes without any errors. */
    guardianResultFile: File;
    /** Specify where Guardian tool packages will be installed. 
     ** Note: This path can either be the .gdn/i directory, but must always be declared so that Guardian does
     **       try to read packages from outside of this location (due to its package cache feature). */
    guardianPackageDirectory: Directory;
    /** Optional Guardian tool working directory. Default: SourceRoot
     ** Note: the default directory "TargetDirectory" for many tools will be this working directory. */
    guardianToolWorkingDirectory?: Directory;
    /** Collection of files that guardian will be scanning
     ** Note: in many cases (such as with credscan), a user cannot specify which files specifically
     **       to run through the tool, so glob all source files that will be touched by the tool here */
    filesToBeScanned: File[];
    /** Any additional artifacts to depend on before running Guardian in addition to the ones declared in filesToBeScanned */
    additionalDependencies?: Transformer.InputArtifact[];
    /** An optional path to a file to redirect the guardian logger output */
    loggerPath?: File;
    /** Indicate whether Guardian should fail entire job after the first failure
     ** Note: defaults to false */
    fast?: boolean;
    /**  Will not allow the use of any baselines including the default baseline (.gdn/.gdnbaselines)*/
    noBaseline?: boolean;
    /** Absolute paths to optional baseline files */
    baselineFiles?: File[];
    /** Will not allow any suppressions including the default suppression set in .gdn/.gdnsuppress */
    noSuppressions?: boolean;
    /** Absolute paths to optional suppression files */
    suppressionFiles?: File[];
    /** Will use specified suppression set(s) to filter breaking results. Only results in these sets will cause a break */
    suppressionSets?: string[];
    /** Specify policy name (default: microsoft) for break method */
    policy?: string;
    /** For use with policy, set minimum severity as defined by policy to cause a break. Any results below this severity will NOT cause a break. */
    severity?: string;
    /** Do not apply any policy */
    noPolicy?: boolean;
}

/**
 * Collection of Paths that are produced or consumed by Guardian.
 */
interface GuardianPaths {
    /** SourceRoot/.gdn */
    globalGuardianRepo: Path,
    /** OutputDir/.gdn */
    localGuardianRepo: Path,
    /** localGuardianRepo/c */
    config: Path,
    /** OutputDir/.r */
    rawResults: Path,
    /** OutputDir/r */
    results: Path,
    /** OutputDir/rc */
    convertedResults: Path,
    /** Path to Guardian tool install directory (usually located under .gdn/i or guardianRoot/packages). */
    install: Path,
    /** Path to locally generated Guardian settings file to be used */
    localSettings: Path,
    /** Path to locally generated Guardian settings file to be used only for guardian install. */
    localInstallSettings: Path,
    /** Path to global settings file checked into repository */
    globalSettings: Path,
    /** localGuardianRepo/internal.gdnhistory */
    localHistory: Path,
    /** globalGuardianRepo/internal.gdnhistory */
    globalHistory: Path,
    /** intermediate Guardian install log file. */
    installLog: File,
}