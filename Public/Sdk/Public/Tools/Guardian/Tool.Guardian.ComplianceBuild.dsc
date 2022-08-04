// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Json from "Sdk.Json";
import * as Drop from "Sdk.Drop";

// Compliance build specific Environment variables
export const directoriesNamesToIgnore = "[Tool.Guardian]complianceIgnoreDirectories";
export const filesPerCredScanCall = "[Tool.Guardian]complianceFilesPerCredScanCall";
export const filesPerPolicheckCall = "[Tool.Guardian]complianceFilesPerPolicheckCall";
const logLevel = "[Tool.Guardian]complianceLogLevel";
const enabledTools = "[Tool.Guardian]enabledTools";

// Export config file directory for tools to use
@@public
export const guardianConfigFileDirectory = Context.getNewOutputDirectory("guardianconfiguration");
export const complianceLogLevel : GuardianLogLevel = Environment.hasVariable(logLevel) ? Environment.getStringValue(logLevel) as GuardianLogLevel : "Warning";

const autoGenerateBaselines = Environment.getFlag(guardianGenerateBaselines);
const autoGenerateSuppressions = !autoGenerateBaselines && Environment.getFlag(guardianGenerateSuppressions);
const complianceBaselineSuppressionLocation = d`${Context.getMount("SourceRoot").path}/.config/buildxl/compliance`;

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

    const supportedTools : Set<string> = Set.create<string>("credscan", "eslint", "psscriptanalyzer", "flawfinder", "policheck");
    let toolsToRun : Set<string> = Set.empty<string>();

    if (Environment.hasVariable(enabledTools)) {
        const tools = Set.create<string>(...Environment.getStringValue(enabledTools).toLowerCase().split(","));

        if (!tools.isSubsetOf(supportedTools)) {
            Contract.fail(`'${enabledTools}' environment variable contains an unsupported tool for the Compliance Build. Supported tools: '${supportedTools.toArray()}', requested tools: '${tools.toArray()}'.`);
        }

        toolsToRun = supportedTools.intersect(tools);
    }
    else {
        toolsToRun = Set.create<string>("credscan");
    }

    const files : File[] = discoverFilesToScan(guardianBuildRoot);
    const guardianResults = [
        ...(toolsToRun.contains("credscan") ? addCredScanCalls(guardianBuildRoot, guardianToolRoot, packageDirectory, guardianDrop, files) : []),
        ...(toolsToRun.contains("eslint") ? addGuardianEsLintCalls(guardianBuildRoot, guardianToolRoot, packageDirectory, guardianDrop, nodeToolRoot, nodeToolExe, files) : [] ),
        ...(toolsToRun.contains("psscriptanalyzer") ? [addPsscriptAnalyzerCalls(guardianBuildRoot, guardianToolRoot, packageDirectory, guardianDrop, files)] : []),
        ...(toolsToRun.contains("flawfinder") ? [addFlawFinderCalls(guardianBuildRoot, guardianToolRoot, packageDirectory, guardianDrop, files)] : []),
        ...(toolsToRun.contains("policheck") ? addPoliCheckCalls(guardianBuildRoot, guardianToolRoot, packageDirectory, guardianDrop, files) : []),
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
 * Generates a Guardian call for the compliance build.
 * Baselines/Suppressions will be picked up from {SourceRoot}/.config/buildxl/compliance automatically.
 * LogLevel is set to Warning in Guardian.
 */
@@public
export function createGuardianCall(
    guardianToolRoot : StaticDirectory,
    guardianPackageDirectory: StaticDirectory,
    guardianDrop : Directory,
    dependencies : Transformer.InputArtifact[],
    baselineName : string,
    workingDirectory : Directory,
    outputSarifFile : RelativePath,
    configFiles : File[],
    environmentVariables: Transformer.EnvironmentVariable[],
    retryExitCodes: number[],
    processRetries: number,
    pathDirectories: Directory[],
    additionalOutputs: Transformer.Output[],
    untrackedPaths: (File | Directory)[],
    untrackedScopes: Directory[],
    allowUndeclaredSourceReads: boolean,
    passThroughEnvironmentVariables: string[])
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
        dependencies: dependencies,
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
        allowUndeclaredSourceReads: allowUndeclaredSourceReads,
        passThroughEnvironmentVariables: passThroughEnvironmentVariables
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
@@public
export function createConfigurationFile(config : Object, destination : Path) : File {
    const options : Json.AdditionalJsonOptions = {
        pathRenderingOption: Context.getCurrentHost().os !== "win" ? "escapedBackSlashes" : "forwardSlashes"
    };

    return Json.write(destination, config, "\"", [guardianTag], "Generate Configuration File for Compliance Build", options);
}