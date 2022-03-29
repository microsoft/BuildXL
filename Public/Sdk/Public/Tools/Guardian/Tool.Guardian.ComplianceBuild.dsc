// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Json from "Sdk.Json";
import * as Drop from "Sdk.Drop";

// Compliance build specific Environment variables
const directoriesNamesToIgnore = "[Tool.Guardian]complianceIgnoreDirectories";
const filesPerCredScanCall = "[Tool.Guardian]complianceFilesPerCredScanCall";
const logLevel = "[Tool.Guardian]complianceLogLevel";
const enabledTools = "[Tool.Guardian]enabledTools";

const autoGenerateBaselines = Environment.getFlag(guardianGenerateBaselines);
const autoGenerateSuppressions = !autoGenerateBaselines && Environment.getFlag(guardianGenerateSuppressions);
const complianceBaselineSuppressionLocation = d`${Context.getMount("SourceRoot").path}/.config/buildxl/compliance`;
const guardianConfigFileDirectory = Context.getNewOutputDirectory("guardianconfiguration");
const guardianCredScanConfigFile = createConfigurationFile(credScanConfiguration(), p`${guardianConfigFileDirectory.path}/credScanConfiguration.gdnconfig`);
const guardianEslintConfigFile = createConfigurationFile(eslintConfiguration(), p`${guardianConfigFileDirectory.path}/eslintConfiguration.gdnconfig`);
const complianceLogLevel : GuardianLogLevel = Environment.hasVariable(logLevel) ? Environment.getStringValue(logLevel) as GuardianLogLevel : "Warning";

// Drop related
const dropEnabled = Environment.hasVariable("BUILDXL_COMPLIANCE_BUILD_DROP_CONFIG");
const dropRunner = dropEnabled ? Drop.cloudBuildRunner : undefined;
const dropSettings = dropEnabled 
    ? { 
        dropServiceConfigFile: Environment.getFileValue("BUILDXL_COMPLIANCE_BUILD_DROP_CONFIG"),
        generateBuildManifest: false, // Disabled because we don't care about generating a manifest for the Guardian outputs
        signBuildManifest: false
      }
    : undefined;
const dropCreateResult = dropEnabled ? Drop.cloudBuildRunner.createDrop(dropSettings) : undefined;

/**
 * This function exists for compatibility with old versions of the build runner. See runComplianceBuildOnEntireRepository.
*/
@@public
export function runCredScanOnEntireRepository(guardianToolRoot : StaticDirectory, guardianBuildRoot : Directory) : Transformer.ExecuteResult[] {
    return runComplianceBuildOnEntireRepository(guardianToolRoot, guardianBuildRoot, undefined, undefined);
}

/**
 * Calling this function will create Guardian pips with CredScan and EsLint on the entire repository from the guardianBuildRoot directory.
 *  
 * When running on Cloudbuild, it is not necessary to provide a Guardian install location. Instead the Guardian binaries will
 * be from acquired from the Guardian drop.
 */
@@public
export function runComplianceBuildOnEntireRepository(guardianToolRoot : StaticDirectory, guardianBuildRoot : Directory, nodeToolRoot : OpaqueDirectory, nodeToolExe : File) : Transformer.ExecuteResult[] {
    if (!Environment.hasVariable("TOOLPATH_GUARDIAN")) {
        Contract.fail("Guardian drop root must be provided with the 'TOOLPATH_GUARDIAN' environment variable.");
    }

    const guardianDrop : Directory = d`${Environment.getPathValue("TOOLPATH_GUARDIAN")}`;

    // Package directory must be partially sealed first
    const packageDirectory : StaticDirectory = Transformer.sealPartialDirectory(
        d`${guardianDrop}/packages`,
        globR(d`${guardianDrop}/packages`, "*").filter(file => file.extension !== a`rex`), // This avoid sealing the .lite.rex files for CredScan to avoid double writes
        [guardianTag],
        "Seal Guardian package directory"
    );

    const supportedTools : Set<string> = Set.create<string>("credscan", "eslint");
    let toolsToRun : Set<string> = Set.empty<string>();

    if (Environment.hasVariable(enabledTools)) {
        const tools = Set.create<string>(...Environment.getStringValue(enabledTools).toLowerCase().split(","));

        if (!tools.isSubsetOf(supportedTools)) {
            Contract.fail(`'${enabledTools}' environment variable contains an unsupported tool for the Compliance Build. Supported tools: '${supportedTools.toArray()}', requested tools: '${tools.toArray()}'.`);
        }

        toolsToRun = supportedTools.intersect(tools);
    }
    else {
        toolsToRun = supportedTools;
    }

    const files : File[] = discoverFilesToScan(guardianBuildRoot);
    const guardianResults = [
        ...(toolsToRun.contains("credscan") ? addCredScanCalls(guardianBuildRoot, guardianToolRoot, packageDirectory, guardianDrop, files) : []),
        ...(toolsToRun.contains("eslint")   ? addGuardianEsLintCalls(guardianBuildRoot, guardianToolRoot, packageDirectory, guardianDrop, nodeToolRoot, nodeToolExe, files) : [] )
    ];

    return guardianResults;
}

/**
 * Discovers a set of potential files that Guardian will run on for this repository.
 */
function discoverFilesToScan(rootDirectory : Directory) : File[] {
    // These are directories that are local to a given repository that are not checked in remotely
    const directoryAtomsToIgnore = Set.create<PathAtom>(
        // Defaults
        a`.git`,
        a`.cloudbuild`,
        a`.corext`,
        a`out`,
        a`node_modules`,
        // User specified
        ...addIfLazy(Environment.hasVariable(directoriesNamesToIgnore), () => {
            const directoryList = Environment.getStringValue(directoriesNamesToIgnore).split(",");

            return directoryList.map(dir => Context.getCurrentHost().os === "win" ? a`${dir.toLowerCase()}` : a`${dir}`);
        })
    );
    
    const directoryPathsToIgnore = Set.create<Directory>(
        d`${Context.getMount("SourceRoot").path}/common/temp` // well known path for rush install (not part of initially checked out sources)
    );

    const filesToIgnore = Set.create<File>(
        f`${Context.getMount("SourceRoot").path}/.SubstLock`
    );
    
    let files : File[] = glob(rootDirectory).filter(f => !filesToIgnore.contains(f));
    let directories = globFolders(rootDirectory, "*", /*recursive*/false);
    let directoryIndex = 0;

    while (directoryIndex < directories.length) {
        const directoryAtom = Context.getCurrentHost().os === "win" ? a`${directories[directoryIndex].name.toString().toLowerCase()}` : directories[directoryIndex].name;
        if (directoryAtomsToIgnore.contains(directoryAtom) || directoryPathsToIgnore.contains(directories[directoryIndex])) {
            directoryIndex++;
            continue;
        }

        files = files.concat(glob(directories[directoryIndex], "*"));
        directories = directories.concat(globFolders(directories[directoryIndex], "*", /*recursive*/ false));

        directoryIndex++;
    }

    return files;
}

/**
 * Adds a set of batched credscan guardian calls.
 */
function addCredScanCalls(rootDirectory : Directory, guardianToolRoot : StaticDirectory, packageDirectory : StaticDirectory, guardianDrop : Directory, files : File[]) : Transformer.ExecuteResult[] {    
    let results : MutableSet<Transformer.ExecuteResult> = MutableSet.empty<Transformer.ExecuteResult>();
    let directories = globFolders(rootDirectory, "*", /*recursive*/false);
    let directoryIndex = 0;

    const minFilesPerCall = Environment.hasVariable(filesPerCredScanCall) ? Environment.getNumberValue(filesPerCredScanCall) : 500;
    const additionalCalls = Math.mod(files.length, minFilesPerCall) > 0 ? 1 : 0;
    const numCredScanCalls = Math.div(files.length, minFilesPerCall) + additionalCalls;

    for (let i = 0; i < numCredScanCalls; i++) {
        const credScanWorkingDirectory = Context.getNewOutputDirectory("credscan");
    
        // Generate a TSV file for all files to be scanned by CredScan
        const scannedFiles = i === numCredScanCalls-1 && additionalCalls === 1
            ? files.slice(minFilesPerCall * i, files.length)
            : files.slice(minFilesPerCall * i, minFilesPerCall * (i + 1));
        
        const scanPaths = scannedFiles.map(file => file.path);
        const tsvFile = Transformer.writeAllLines(p`${credScanWorkingDirectory.path}/guardian.TSV`, scanPaths);
        
        // Schedule cred scan pips
        results.add(createGuardianCall(
            guardianToolRoot,
            packageDirectory,
            guardianDrop,
            scannedFiles,
            `credscan_${i}`,
            credScanWorkingDirectory,
            a`CredScan_${i.toString()}.sarif`,
            [tsvFile],
            [guardianCredScanConfigFile],
            /*environmentVariables*/undefined,
            /*retryExitCodes*/[-9000],
            /*processRetries*/3,
            /*pathDirectories*/undefined,
            /*additionalOutputs*/undefined,
            /*untrackedPaths*/undefined,
            /*untrackedScopes*/[d`${packageDirectory.path}/nuget/Microsoft.Security.CredScan.Client.2.2.9.6-preview/lib/netcoreapp3.1/SRM`],
            /*allowUndeclaredSourceReads*/false)
        );
    }

    return results.toArray();
}

/**
 * Adds an eslint guardian call for each package.json file discovered under the rootDirectory.
 */
function addGuardianEsLintCalls(rootDirectory : Directory, guardianToolRoot : StaticDirectory, packageDirectory : StaticDirectory, guardianDrop : Directory, nodeToolRoot : OpaqueDirectory, nodeToolExe : File, files : File[]) : Transformer.ExecuteResult[] {
    let results : MutableSet<TransformerExecuteResult> = MutableSet.empty<TransformerExecuteResult>();
    const genericEslintSettingsDirectory = Context.getNewOutputDirectory("eslint");
    const dummyEsLintExclusionFile = Transformer.writeData(p`${genericEslintSettingsDirectory.path}/.eslintignore`, "");
    let packageJsonFiles = files.filter(file => file.name === a`package.json`);

    // If only one package.json file is left, then this is a repo with a single project
    if (packageJsonFiles.length > 1) {
        // Sort projects so that we can add exclusion patterns later
        packageJsonFiles = packageJsonFiles.sort((a, b) => b.parent.toString().localeCompare(a.parent.toString()));
    }

    for (let packageIndex = 0; packageIndex < packageJsonFiles.length; packageIndex++) {
        const workingDirectory = Context.getNewOutputDirectory("eslint");

        // Since the packageJsonFiles is in order, we can just check in order which subprojects need to be excluded for this call
        let excludePaths : MutableSet<Path> = MutableSet.empty<Path>();
        let excludeIndex = packageIndex + 1;
        while (excludeIndex < packageJsonFiles.length && packageJsonFiles[excludeIndex].isWithin(packageJsonFiles[packageIndex].parent)) {
            excludePaths.add(packageJsonFiles[excludeIndex].parent);
            excludeIndex++;
        }

        const excludePathsArray = excludePaths.toArray();
        // Write exclusions to .eslintignore file that will be picked up by an environment variable
        let eslintExclusionFile : DerivedFile = undefined;
        if (excludePathsArray.length > 0) {
            eslintExclusionFile = Transformer.writeData(
                {
                    outputPath: p`${workingDirectory.path}/.eslintignore`,
                    contents: { separator: "/\n", contents: [...excludePathsArray, ""] }, // Empty string added to the end so that the separator will be inserted to the last path on the list
                    pathRenderingOption: "forwardSlashes"
                }
            );
        }

        const environmentVariables : Transformer.EnvironmentVariable[] = [
            // Adding \\** will tell eslint to glob all files under the provided directory and scan those
            {name: "EsLintTargetDirPattern", value: packageJsonFiles[packageIndex].parent.path},
            // We can skip scanning any directories that are subdirectories of the current package json root because they will be scanned by a separate guardian pip
            {name: "EsLintExclusionFile", value: excludePathsArray.length > 0 ? eslintExclusionFile : dummyEsLintExclusionFile }
        ];

        // Get all files under the current package.json scope
        let scannedFiles = files.filter(file => file.isWithin(packageJsonFiles[packageIndex].parent));

        // Filter out files in subprojects
        for(let excludePath of excludePathsArray) {
            scannedFiles = scannedFiles.filter(file => !file.isWithin(excludePath.parent));
        }

        // Eslint will also read files under the node_modules directory under the root directory of each project
        const node_modules = Transformer.sealSourceDirectory(d`${packageJsonFiles[packageIndex].parent}/node_modules`, Transformer.SealSourceDirectoryOption.allDirectories);

        results.add(createGuardianCall(
            guardianToolRoot,
            packageDirectory,
            guardianDrop,
            scannedFiles,
            `eslint_${packageIndex}`,
            workingDirectory,
            a`EsLint_${packageIndex.toString()}.sarif`,
            /*additionalDependencies*/[eslintExclusionFile === undefined ? dummyEsLintExclusionFile : eslintExclusionFile, nodeToolRoot, node_modules],
            [guardianEslintConfigFile],
            environmentVariables,
            /*retryExitCodes*/undefined,
            /*processRetries*/undefined,
            [d`${nodeToolExe.parent}`],
            [<Transformer.FileOrPathOutput>{ existence: "optional", artifact: f`${workingDirectory.path}/.eslintcache`}],
            // It is necessary to untrack the package.json files except for the main package.json file due to the cascading configuration feature reading these files outside of the provided scope
            // https://eslint.org/docs/user-guide/configuring/configuration-files#cascading-and-hierarchy
            packageJsonFiles.filter(file => file !== packageJsonFiles[packageIndex]),
            /*untrackedScopes*/[d`${Context.getMount("SourceRoot").path}/common/temp`],
            /*allowUndeclaredSourceReads*/true)
        );
    }

    return results.toArray();
}

/**
 * Generates a Guardian call for the compliance build.
 * Baselines/Suppressions will be picked up from {SourceRoot}/.config/buildxl/compliance automatically.
 * LogLevel is set to Warning in Guardian.
 */
function createGuardianCall(
    guardianToolRoot : StaticDirectory,
    guardianPackageDirectory: StaticDirectory,
    guardianDrop : Directory,
    files : File[],
    baselineName : string,
    workingDirectory : Directory,
    outputSarifFile : PathAtom,
    additionalDependencies : Transformer.InputArtifact[],
    configFiles : File[],
    environmentVariables: Transformer.EnvironmentVariable[],
    retryExitCodes: number[],
    processRetries: number,
    pathDirectories: Directory[],
    additionalOutputs: Transformer.Output[],
    untrackedPaths: (File | Directory)[],
    untrackedScopes: Directory[],
    allowUndeclaredSourceReads: boolean)
    : Transformer.ExecuteResult {

    const baselines = glob(complianceBaselineSuppressionLocation, "*.gdnbaselines");
    const suppressions = glob(complianceBaselineSuppressionLocation, "*.gdnsuppress");
    
    const guardianArgs : GuardianArguments = {
        guardianCommand: "Run",
        guardianToolRootDirectory: guardianToolRoot,
        guardianConfigFiles: configFiles,
        guardianResultFile: f`${Context.getMount("LogsDirectory").path}/Guardian/${outputSarifFile}`,
        guardianPackageDirectory: guardianPackageDirectory,
        guardianToolWorkingDirectory: workingDirectory, // Set this to pick up the newly generated tsv file automatically
        filesToBeScanned: files,
        additionalDependencies: additionalDependencies,
        logLevel: complianceLogLevel, // Display only warnings and errors only to simplify debugging and reduce log file size
        baselineFiles: baselines.length > 0 ? baselines : undefined,
        suppressionFiles: suppressions.length > 0 ? suppressions : undefined,
        autoGeneratedBaselineSuppressionLocation: autoGenerateBaselines || autoGenerateSuppressions
            ? d`${Context.getMount("LogsDirectory").path}/Guardian`
            : undefined,
        baselineFileName: autoGenerateBaselines ? a`${baselineName}.gdnbaselines` : undefined,
        suppressionFileName: autoGenerateSuppressions ? a`${baselineName}.gdnsuppressions` : undefined,
        retryExitCodes: retryExitCodes,
        processRetries: processRetries,
        environmentVariables: environmentVariables,
        pathDirectories: pathDirectories,
        additionalOutputs: additionalOutputs,
        untrackedPaths: untrackedPaths,
        untrackedScopes: untrackedScopes,
        allowUndeclaredSourceReads: allowUndeclaredSourceReads
    };

    const guardianResult = runGuardian(guardianArgs);

    if (dropEnabled) {
        dropRunner.addFilesToDrop(dropCreateResult, /*args*/{}, [{ dropPath: r`${outputSarifFile}`, file: guardianResult.getOutputFile(guardianArgs.guardianResultFile.path) }]);
    }

    return guardianResult;
}

/**
 * Writes a JSON configuration file for this Guardian run.
 * Extend this configuration file to add more Guardian tools to the compliance build.
 */
function createConfigurationFile(config : Object, destination : Path) : File {
    const options : Json.AdditionalJsonOptions = {
        pathRenderingOption: Context.getCurrentHost().os !== "win" ? "escapedBackSlashes" : "forwardSlashes"
    };

    return Json.write(destination, config, "\"", [guardianTag], "Generate Configuration File for Compliance Build", options);
}

/**
 * Configuration files for each Guardian tool to be run in this build.
 */
function eslintConfiguration() : Object {
    return {
        "fileVersion": "0.1",
        "tools": [
            {
                "fileVersion": "0.1",
                "tool": {
                    "name": "eslint",
                    "version": "7.32.0.2"
                },
                "arguments": {
                    "DisableDefaultConfigurationFile": false,
                    "ConfigurationFile": "$(SDLRequiredConfigurationFile)",
                    "Extensions": [
                        ".js",
                        ".ts"
                    ],
                    "ExclusionPatterns": "*.d.ts",
                    "ExclusionsFilePath": "$(EsLintExclusionFile)",
                    "OutputFormat": "$(InstallDirectory)\\node_modules\\@microsoft\\eslint-formatter-sarif\\sarif.js",
                    "Targets": "\"$(EsLintTargetDirPattern)\\**\"",
                    "Debug": complianceLogLevel === "Trace" || complianceLogLevel === "Verbose"
                },
                "outputExtension": "sarif",
                "successfulExitCodes": [
                    0,
                    1
                ],
                "errorExitCodes": {
                    "2": "Linting was unsuccessful due to a configuration problem or an internal error. If you have enabled exit-on-fatal-error one or more of your files could possibly have a parsing error. Check the errors or the produced SARIF files to fix the parsing errors."
                },
                "outputPaths": [ ]
            }
        ]
    };
}

function credScanConfiguration() : Object {
    return {
        "fileVersion": "1.4",
        "tools": [
            {
                "fileVersion": "1.4",
                "tool": {
                    "name": "CredScan",
                    "version": "2.2.9.6-preview"
                },
                "arguments": {
                    "TargetDirectory": "$(WorkingDirectory)/guardian.TSV",
                    "OutputType": "pre",
                    "SuppressAsError": true,
                    "Verbose": complianceLogLevel === "Trace"
                },
                "outputExtension": "xml",
                "successfulExitCodes": [
                    0,
                    2,
                    4,
                    6
                ],
                "errorExitCodes": {
                    "1": "Partial scan completed with warnings.",
                    "3": "Partial scan completed with credential matches and warnings.",
                    "5": "Partial scan completed with application warnings and credential matches",
                    "7": "Partial scan completed with application warnings, suppressed warnings, and credential matches",
                    "-1000": "Argument Exception.",
                    "-1100": "Invalid configuration.",
                    "-1500": "Configuration Exception.",
                    "-1600": "IO Exception.",
                    "-9000": "Unexpected Exception."
                }
            }
        ]
    };
}

/**
 * Installs EsLint and returns a static install directory.
 */
function installEsLintWithGuardian(guardianToolRoot: StaticDirectory, guardianPackageDirectory: StaticDirectory, pathDirectories : Directory[], nodeToolRoot : OpaqueDirectory) : StaticDirectory {
    // Install EsLint (temporary)
    // Generate dummy eslint config for install
    const eslintConfigFile = a`eslintConfig_dummy.gdnconfig`;
    const guardianEslintConfigFile = createConfigurationFile(eslintConfiguration(), p`${guardianConfigFileDirectory.path}/${eslintConfigFile}`);

    // Working directory is not important for this call because the tool will be installed under the package directory
    const workingDirectory = Context.getNewOutputDirectory("eslintinstall");
    const userProfile = Context.getNewOutputDirectory("userprofile");

    const guardianArgs : GuardianArguments = {
        guardianCommand: "Install",
        guardianToolRootDirectory: guardianToolRoot,
        guardianConfigFiles: [guardianEslintConfigFile],
        guardianResultFile: f`${Context.getMount("LogsDirectory").path}/Guardian/install_eslint.sarif`,
        guardianPackageDirectory: guardianPackageDirectory,
        guardianToolWorkingDirectory: workingDirectory, // Set this to pick up the newly generated tsv file automatically
        filesToBeScanned: [],
        additionalDependencies: [nodeToolRoot],
        logLevel: "Trace", // Display only warnings and errors only to simplify debugging and reduce log file size
        baselineFiles: undefined,
        suppressionFiles: undefined,
        autoGeneratedBaselineSuppressionLocation: undefined,
        baselineFileName:undefined,
        suppressionFileName:undefined,
        noPolicy: true, // Setting --no-policy to avoid installing policy packages (these are already installed on Cloudbuild)
        pathDirectories: pathDirectories,
        environmentVariables: [{ name: "USERPROFILE", value: userProfile.path }],
        untrackedScopes: [d`${userProfile.path}/npm-cache`]
    };

    return runGuardian(guardianArgs).getOutputDirectory(guardianPackageDirectory.root);
}