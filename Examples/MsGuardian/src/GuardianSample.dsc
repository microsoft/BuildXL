// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Guardian from "BuildXL.Tools.Guardian";

const guardianPkg : StaticDirectory = importFrom("Microsoft.Guardian.Cli").Contents.all;
const logDirectory = d`${Context.getMount("Deployment").path}/Guardian/Logs`;
const configDirectory = d`${Context.getMount("SourceRoot").path}/.config/guardian`;
const packageDirectory = d`${Context.getMount("Deployment").path}/Guardian/packages`;
const workingDirectory = d`${Context.getMount("SourceRoot").path}/src`;

// Log files
const guardianExportFile = f`${logDirectory.path}/export.sarif`;
const guardianStdOut = f`${logDirectory.path}/guardian.log`;

// Config files
const guardianConfig = f`${configDirectory.path}/buildxl.gdnconfig`;

// Baselines
const guardianBaselines = glob(configDirectory, "*.gdnbaselines");

// Suppressions
const guardianSuppressions = glob(configDirectory, "*.gdnsuppressions");

// Files to scan - credscan will scan everything in the working directory
const files = globR(workingDirectory, "*");

const guardianArgs : Guardian.GuardianArguments = {
    guardianToolRootDirectory: guardianPkg,
    guardianConfigFile: guardianConfig,
    guardianResultFile: guardianExportFile,
    guardianPackageDirectory: packageDirectory,
    guardianToolWorkingDirectory: workingDirectory,
    filesToBeScanned: files,
    loggerPath: guardianStdOut,
    // baselineFiles: guardianBaselines // Uncomment this line to baseline the CredScan violation for a successful build
};

const guardianRun = Guardian.runGuardian(guardianArgs);