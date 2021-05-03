// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import {Assert, Testing} from "Sdk.Testing";
import * as Guardian from "BuildXL.Tools.Guardian";

namespace Sdk.Tests {
    @@Testing.unitTest()
    export function runGuardian() {
        addMounts();

        const guardianToolDirectory = Transformer.sealPartialDirectory({
            root: d`path/to/guardian`,
            files: [],
        });

        const guardianTest = Guardian.runGuardian({
            guardianToolRootDirectory: guardianToolDirectory,
            guardianConfigFile: f`path/to/guardian/config/file`,
            guardianResultFile: f`out/result/file`,
            filesToBeScanned: [f`file1`, f`file2`],
            guardianPackageDirectory: d`path/to/guardian/packages`,
            guardianToolWorkingDirectory: d`working/directory`,
            baselineFiles: [f`baseLineFile1`, f`baseLineFile2`],
            noSuppressions: true,
            policy: "microsoft"
        });
    }

    @@Testing.unitTest()
    export function runGuardianWithBadInputs() {
        addMounts();
        const guardianToolDirectory = Transformer.sealPartialDirectory({
            root: d`path/to/guardian`,
            files: [],
        });

        Testing.expectFailure(
            () => Guardian.runGuardian(undefined),
            {
                code: 7503,
                content: "Guardian arguments cannot be undefined",
            }
        );

        Testing.expectFailure(
            () => Guardian.runGuardian({
                guardianToolRootDirectory: undefined,
                guardianConfigFile: f`path/to/guardian/config/file`,
                guardianResultFile: f`out/result/file`,
                filesToBeScanned: [f`file1`, f`file2`],
                guardianPackageDirectory: d`path/to/guardian/packages`,
                guardianToolWorkingDirectory: d`working/directory`,
                baselineFiles: [f`baseLineFile1`, f`baseLineFile2`],
                noSuppressions: true,
                policy: "microsoft"
            }),
            {
                code: 7503,
                content: "Guardian root must be set.",
            }
        );

        Testing.expectFailure(
            () => Guardian.runGuardian({
                guardianToolRootDirectory: guardianToolDirectory,
                guardianConfigFile: f`path/to/guardian/config/file`,
                guardianResultFile: f`out/result/file`,
                filesToBeScanned: [f`file1`, f`file2`],
                guardianPackageDirectory: undefined,
                guardianToolWorkingDirectory: d`working/directory`,
                baselineFiles: [f`baseLineFile1`, f`baseLineFile2`],
                noSuppressions: true,
                policy: "microsoft"
            }),
            {
                code: 7503,
                content: "Guardian tool package install directory must be set.",
            }
        );
    }

    @@Testing.unitTest()
    export function runGuardianWithConflictingInputs() {
        addMounts();
        const guardianToolDirectory = Transformer.sealPartialDirectory({
            root: d`path/to/guardian`,
            files: [],
        });

        Testing.expectFailure(
            () =>
            {
                Guardian.runGuardian({
                    guardianToolRootDirectory: guardianToolDirectory,
                    guardianConfigFile: f`path/to/guardian/config/file`,
                    guardianResultFile: f`out/result/file`,
                    filesToBeScanned: [f`file1`, f`file2`],
                    guardianPackageDirectory: d`path/to/guardian/packages`,
                    guardianToolWorkingDirectory: d`working/directory`,
                    baselineFiles: [f`baseLineFile1`, f`baseLineFile2`],
                    noBaseline: true,
                    policy: "microsoft"
                });
            },
            {
                code: 7501,
                content: "noBaseline and baselineFile cannot be specified together.",
            }
        );

        Testing.expectFailure(
            () =>
            {                
                Guardian.runGuardian({
                    guardianToolRootDirectory: guardianToolDirectory,
                    guardianConfigFile: f`path/to/guardian/config/file`,
                    guardianResultFile: f`out/result/file`,
                    filesToBeScanned: [f`file1`, f`file2`],
                    guardianPackageDirectory: d`path/to/guardian/packages`,
                    guardianToolWorkingDirectory: d`working/directory`,
                    suppressionFiles: [f`baseLineFile1`, f`baseLineFile2`],
                    noSuppressions: true,
                    policy: "microsoft"
                });
            },
            {
                code: 7501,
                content: "noSuppressions and suppressionFiles/suppressionSets cannot be specified together.",
            }
        );

        Testing.expectFailure(
            () =>
            {                
                Guardian.runGuardian({
                    guardianToolRootDirectory: guardianToolDirectory,
                    guardianConfigFile: f`path/to/guardian/config/file`,
                    guardianResultFile: f`out/result/file`,
                    filesToBeScanned: [f`file1`, f`file2`],
                    guardianPackageDirectory: d`path/to/guardian/packages`,
                    guardianToolWorkingDirectory: d`working/directory`,
                    fast: true,
                    baselineFiles: [f`baseLineFile1`, f`baseLineFile2`],
                    policy: "microsoft"
                });
            },
            {
                code: 7501,
                content: "The --fast argument is incompatible with the output baseline file argument, as this will require a full run of guardian break to generate all the results.",
            }
        );
    }

    function addMounts() {
        Testing.setMountPoint({
            name: a`GuardianRoot`,
            path: p`path/to/guardian`,
            isReadable: true,
            isWritable: true,
            isSystem: false,
            isScrubbable: true,
            trackSourceFileChanges: true,
        });
        Testing.setMountPoint({
            name: a`SourceRoot`,
            path: p`path/to/src`,
            isReadable: true,
            isWritable: true,
            isSystem: false,
            isScrubbable: true,
            trackSourceFileChanges: true,
        });
        Testing.setMountPoint({
            name: a`ProgramData`,
            path: p`path/to/ProgramData`,
            isReadable: true,
            isWritable: true,
            isSystem: true,
            isScrubbable: false,
            trackSourceFileChanges: true,
        });
        Testing.setMountPoint({
            name: a`ProgramFilesX86`,
            path: p`path/to/ProgramFilesX86`,
            isReadable: true,
            isWritable: true,
            isSystem: true,
            isScrubbable: false,
            trackSourceFileChanges: true,
        });
        Testing.setMountPoint({
            name: a`ProgramFiles`,
            path: p`path/to/ProgramFiles`,
            isReadable: true,
            isWritable: true,
            isSystem: true,
            isScrubbable: false,
            trackSourceFileChanges: true,
        });
        Testing.setMountPoint({
            name: a`AppData`,
            path: p`path/to/AppData`,
            isReadable: true,
            isWritable: true,
            isSystem: true,
            isScrubbable: false,
            trackSourceFileChanges: true,
        });
        Testing.setMountPoint({
            name: a`LocalAppData`,
            path: p`path/to/LocalAppData`,
            isReadable: true,
            isWritable: true,
            isSystem: true,
            isScrubbable: false,
            trackSourceFileChanges: true,
        });
        Testing.setMountPoint({
            name: a`UserProfile`,
            path: p`UserProfile`,
            isReadable: true,
            isWritable: true,
            isSystem: true,
            isScrubbable: false,
            trackSourceFileChanges: true,
        });
    }
}