// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

const autoGenerateBaselines = Environment.getFlag(guardianGenerateBaselines);
const autoGenerateSuppressions = !autoGenerateBaselines && Environment.getFlag(guardianGenerateSuppressions);
const complianceBaselineSuppressionLocation = Environment.hasVariable("BUILDXL_ENLISTMENT_ROOT") ? d`${Environment.getPathValue("BUILDXL_ENLISTMENT_ROOT")}/.config/buildxl/compliance` : undefined;
const guardianConfigFile = f`${Context.getMount("SourceRoot").path}/guardianBuildConfig.gdnconfig`;

// Compliance build specific Environment variables
const directoriesNamesToIgnore = "[Tool.Guardian]complianceIgnoreDirectories";
const filesPerCredScanCall = "[Tool.Guardian]complianceFilesPerCredScanCall";

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
    let results : Transformer.ExecuteResult[] = [];
    let files : File[] = glob(rootDirectory, "*");
    let directories = globFolders(rootDirectory, "*", /*recursive*/false);
    let directoryIndex = 0;

    // These are directories that are local to a given repository that are not checked in remotely
    const directoryAtomsToIgnore = Set.create<PathAtom>(
        // Defaults
        a`.git`,
        a`.cloudbuild`,
        a`.corext`,
        a`Out`,
        a`node_modules`,
        // User specified
        ...addIfLazy(Environment.hasVariable(directoriesNamesToIgnore), () => {
            const directoryList = Environment.getStringValue(directoriesNamesToIgnore).split(",");

            return directoryList.map(dir => a`${dir}`);
        })
    );
    const directoryPathsToIgnore = Set.create<Directory>(
        d`${Environment.getPathValue("BUILDXL_ENLISTMENT_ROOT")}/common/temp` // well known path for rush install (not part of initially checked out sources)
    );

    const minFilesPerCall = Environment.hasVariable(filesPerCredScanCall) ? Environment.getNumberValue(filesPerCredScanCall) : 500;

    while (directoryIndex < directories.length) {
        if (directoryAtomsToIgnore.contains(directories[directoryIndex].name) || directoryPathsToIgnore.contains(directories[directoryIndex])) {
            directoryIndex++;
            continue;
        }

        files = files.concat(glob(directories[directoryIndex], "*"));
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
        logLevel: "Warning", // Display only warnings and errors only to simplify debugging and reduce log file size
        baselineFiles: baselines.length > 0 ? baselines : undefined,
        suppressionFiles: suppressions.length > 0 ? suppressions : undefined,
        autoGeneratedBaselineSuppressionLocation: autoGenerateBaselines || autoGenerateSuppressions
            ? d`${Context.getMount("LogsDirectory").path}/Guardian`
            : undefined,
        baselineFileName: autoGenerateBaselines ? a`${directoryIndex.toString()}.gdnbaselines` : undefined,
        suppressionFileName: autoGenerateSuppressions ? a`${directoryIndex.toString()}.gdnsuppressions` : undefined,
    };

    return runGuardian(guardianArgs, /*skipInstall*/true);
}