// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

const guardianEslintConfigFile = createConfigurationFile(eslintConfiguration(), p`${guardianConfigFileDirectory.path}/eslintConfiguration.gdnconfig`);


/**
 * Adds an eslint guardian call for each package.json file discovered under the rootDirectory.
 */
export function addGuardianEsLintCalls(rootDirectory : Directory, guardianToolRoot : StaticDirectory, packageDirectory : StaticDirectory, guardianDrop : Directory, nodeToolRoot : OpaqueDirectory, nodeToolExe : File, files : File[]) : Transformer.ExecuteResult[] {
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
            [...scannedFiles, eslintExclusionFile === undefined ? dummyEsLintExclusionFile : eslintExclusionFile, nodeToolRoot, node_modules],
            `eslint_${packageIndex}`,
            workingDirectory,
            a`EsLint_${packageIndex.toString()}.sarif`,
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
        dependencies: [nodeToolRoot],
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