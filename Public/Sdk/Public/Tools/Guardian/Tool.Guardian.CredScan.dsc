// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Json from "Sdk.Json";

// Compliance build specific Environment variables
const directoriesNamesToIgnore = "[Tool.Guardian]complianceIgnoreDirectories";
const filesPerCredScanCall = "[Tool.Guardian]complianceFilesPerCredScanCall";
const logLevel = "[Tool.Guardian]complianceLogLevel";

const autoGenerateBaselines = Environment.getFlag(guardianGenerateBaselines);
const autoGenerateSuppressions = !autoGenerateBaselines && Environment.getFlag(guardianGenerateSuppressions);
const complianceBaselineSuppressionLocation = d`${Context.getMount("SourceRoot").path}/.config/buildxl/compliance`;
const guardianConfigFile = createConfigurationFile();
const complianceLogLevel : GuardianLogLevel = Environment.hasVariable(logLevel) ? Environment.getStringValue(logLevel) as GuardianLogLevel : "Warning";

/**
 * Calling this function will create Guardian pips with CredScan only on the entire repository from the guardianBuildRoot directory.
 *  
 * When running on Cloudbuild, it is not necessary to provide a Guardian install location. Instead the Guardian binaries will
 * be from acquired from the Guardian drop.
 */
@@public
export function runCredScanOnEntireRepository(guardianToolRoot : StaticDirectory, guardianBuildRoot : Directory) : Transformer.ExecuteResult[] {
    if (!Environment.hasVariable("TOOLPATH_GUARDIAN")) {
        Contract.fail("Guardian drop root must be provided with the 'TOOLPATH_GUARDIAN' environment variable.");
    }

    return addGuardianPerDirectoryForRepository(guardianBuildRoot, guardianToolRoot, d`${Environment.getPathValue("TOOLPATH_GUARDIAN")}`);
}

/**
 * Goes through each directory under the given root directory and creates CredScan calls per ~500 files.
 */
function addGuardianPerDirectoryForRepository(rootDirectory : Directory, guardianToolRoot : StaticDirectory, guardianDrop : Directory) : Transformer.ExecuteResult[] {
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

    let results : Transformer.ExecuteResult[] = [];
    let files : File[] = glob(rootDirectory).filter(f => !filesToIgnore.contains(f));
    let directories = globFolders(rootDirectory, "*", /*recursive*/false);
    let directoryIndex = 0;

    const minFilesPerCall = Environment.hasVariable(filesPerCredScanCall) ? Environment.getNumberValue(filesPerCredScanCall) : 500;

    while (directoryIndex < directories.length) {
        const directoryAtom = Context.getCurrentHost().os === "win" ? a`${directories[directoryIndex].name.toString().toLowerCase()}` : directories[directoryIndex].name;
        if (directoryAtomsToIgnore.contains(directoryAtom) || directoryPathsToIgnore.contains(directories[directoryIndex])) {
            directoryIndex++;
            continue;
        }

        files = files.concat(glob(directories[directoryIndex], "*")); // Filter is currently not applied here because it's not necessary past the top level directory
        directories = directories.concat(globFolders(directories[directoryIndex], "*", /*recursive*/false));

        if (files.length >= minFilesPerCall || (directoryIndex === directories.length - 1 && files.length > 0)) {
            results.push(createGuardianCall(guardianToolRoot, guardianDrop, files, directoryIndex));
            files = [];
        }

        directoryIndex++;
    }

    return results;
}

/**
 * Writes a JSON configuration file for this Guardian run.
 * Extend this configuration file to add more Guardian tools to the compliance build.
 */
function createConfigurationFile() : File {
    const options : Json.AdditionalJsonOptions = {
        pathRenderingOption: Context.getCurrentHost().os !== "win" ? "escapedBackSlashes" : "forwardSlashes"
    };
    const configDirectory = Context.getNewOutputDirectory("configuration");
    const config = {
        "fileVersion": "1.4",
        "tools": [
            {
                "fileVersion": "1.4",
                "tool": {
                    "name": "CredScan",
                    "version": "latest"
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

    return Json.write(p`${configDirectory.path}/credScanConfiguration.gdnconfig`, config, "\"", [guardianTag], "Generate Configuration File for Compliance Build", options);
}

/**
 * Generates a Guardian call for the CredScan compliance build.
 * Baselines/Suppressions will be picked up from {SourceRoot}/.config/buildxl/compliance automatically.
 * LogLevel is set to Warning in Guardian.
 */
function createGuardianCall(guardianToolRoot : StaticDirectory, guardianDrop : Directory, files : File[], directoryIndex : number) : Transformer.ExecuteResult {
    const exportDir = Context.getNewOutputDirectory("credScan");
    const baselines = glob(complianceBaselineSuppressionLocation, "*.gdnbaselines");
    const suppressions = glob(complianceBaselineSuppressionLocation, "*.gdnsuppress");

    // Generate a TSV file for all files to be scanned
    const tsvFile = Transformer.writeAllLines(p`${exportDir.path}/guardian.TSV`, files.map(file => file.path));
    
    const guardianArgs : GuardianArguments = {
        guardianToolRootDirectory: guardianToolRoot,
        guardianConfigFile: guardianConfigFile, // Pick up default config from drop directory
        guardianResultFile: f`${exportDir.path}/credScanResult.sarif`,
        guardianPackageDirectory: d`${guardianDrop}/packages`,
        guardianToolWorkingDirectory: exportDir, // Set this to pick up the newly generated tsv file automatically
        filesToBeScanned: files,
        additionalDependencies: [tsvFile],
        logLevel: complianceLogLevel, // Display only warnings and errors only to simplify debugging and reduce log file size
        baselineFiles: baselines.length > 0 ? baselines : undefined,
        suppressionFiles: suppressions.length > 0 ? suppressions : undefined,
        autoGeneratedBaselineSuppressionLocation: autoGenerateBaselines || autoGenerateSuppressions
            ? d`${Context.getMount("LogsDirectory").path}/Guardian`
            : undefined,
        baselineFileName: autoGenerateBaselines ? a`${directoryIndex.toString()}.gdnbaselines` : undefined,
        suppressionFileName: autoGenerateSuppressions ? a`${directoryIndex.toString()}.gdnsuppressions` : undefined,
        retryExitCodes: [-9000], //Credscan may fail with -9000 when running concurrent instances due to a bug with credscan (fixed in preview versions), these can be retried
        processRetries: 3,
    };

    return runGuardian(guardianArgs, /*skipInstall*/true);
}