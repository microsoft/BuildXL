// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import {Assert, Testing} from "Sdk.Testing";
import * as Guardian from "Sdk.Guardian";

namespace Sdk.Tests {
    @@Testing.unitTest()
    export function runGuardian() {
        addMounts();
        const guardianToolDirectory = getGuardianDirectory();
        const guardianPackageDirectory = getGuardianPackageDirectory();

        const guardianTest = Guardian.runGuardian({
            guardianCommand: "Run",
            guardianToolRootDirectory: guardianToolDirectory,
            guardianConfigFiles: [f`path/to/guardian/config/file`],
            guardianResultFile: f`out/result/file`,
            dependencies: [f`file1`, f`file2`],
            guardianPackageDirectory: guardianPackageDirectory,
            guardianToolWorkingDirectory: d`working/directory`,
            baselineFiles: [f`baseLineFile1`, f`baseLineFile2`],
            noSuppressions: true,
            policy: "microsoft",
            additionalOutputs: []
        });
    }

    @@Testing.unitTest()
    export function runGuardianWithBadInputs() {
        addMounts();
        const guardianToolDirectory = getGuardianDirectory();
        const guardianPackageDirectory = getGuardianPackageDirectory();

        Testing.expectFailure(
            () => Guardian.runGuardian(undefined),
            {
                code: 7503,
                content: "Guardian arguments cannot be undefined",
            }
        );

        Testing.expectFailure(
            () => Guardian.runGuardian({
                guardianCommand: "Run",
                guardianToolRootDirectory: undefined,
                guardianConfigFiles: [f`path/to/guardian/config/file`],
                guardianResultFile: f`out/result/file`,
                dependencies: [f`file1`, f`file2`],
                guardianPackageDirectory: guardianPackageDirectory,
                guardianToolWorkingDirectory: d`working/directory`,
                baselineFiles: [f`baseLineFile1`, f`baseLineFile2`],
                noSuppressions: true,
                policy: "microsoft",
                additionalOutputs: []
            }),
            {
                code: 7503,
                content: "Guardian root must be set.",
            }
        );

        Testing.expectFailure(
            () => Guardian.runGuardian({
                guardianCommand: "Run",
                guardianToolRootDirectory: guardianToolDirectory,
                guardianConfigFiles: [f`path/to/guardian/config/file`],
                guardianResultFile: f`out/result/file`,
                dependencies: [f`file1`, f`file2`],
                guardianPackageDirectory: undefined,
                guardianToolWorkingDirectory: d`working/directory`,
                baselineFiles: [f`baseLineFile1`, f`baseLineFile2`],
                noSuppressions: true,
                policy: "microsoft",
                additionalOutputs: []
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
        const guardianToolDirectory = getGuardianDirectory();
        const guardianPackageDirectory = getGuardianPackageDirectory();

        Testing.expectFailure(
            () =>
            {
                Guardian.runGuardian({
                    guardianCommand: "Run",
                    guardianToolRootDirectory: guardianToolDirectory,
                    guardianConfigFiles: [f`path/to/guardian/config/file`],
                    guardianResultFile: f`out/result/file`,
                    dependencies: [f`file1`, f`file2`],
                    guardianPackageDirectory: guardianPackageDirectory,
                    guardianToolWorkingDirectory: d`working/directory`,
                    baselineFiles: [f`baseLineFile1`, f`baseLineFile2`],
                    noBaseline: true,
                    policy: "microsoft",
                    additionalOutputs: []
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
                    guardianCommand: "Run",
                    guardianToolRootDirectory: guardianToolDirectory,
                    guardianConfigFiles: [f`path/to/guardian/config/file`],
                    guardianResultFile: f`out/result/file`,
                    dependencies: [f`file1`, f`file2`],
                    guardianPackageDirectory: guardianPackageDirectory,
                    guardianToolWorkingDirectory: d`working/directory`,
                    suppressionFiles: [f`baseLineFile1`, f`baseLineFile2`],
                    noSuppressions: true,
                    policy: "microsoft",
                    additionalOutputs: []
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
                    guardianCommand: "Run",
                    guardianToolRootDirectory: guardianToolDirectory,
                    guardianConfigFiles: [f`path/to/guardian/config/file`],
                    guardianResultFile: f`out/result/file`,
                    dependencies: [f`file1`, f`file2`],
                    guardianPackageDirectory: guardianPackageDirectory,
                    guardianToolWorkingDirectory: d`working/directory`,
                    fast: true,
                    baselineFiles: [f`baseLineFile1`, f`baseLineFile2`],
                    policy: "microsoft",
                    additionalOutputs: []
                });
            },
            {
                code: 7501,
                content: "The --fast argument is incompatible with the output baseline file argument, as this will require a full run of guardian break to generate all the results.",
            }
        );
    }

    function getGuardianDirectory() : StaticContentDirectory {
        return Transformer.sealPartialDirectory({
            root: d`path/to/guardian`,
            files: [f`path/to/guardian/guardian.cmd`],
        });
    }

    function getGuardianPackageDirectory() : StaticDirectory {
        return Transformer.sealPartialDirectory({
            root: d`path/to/guardian/packages`,
            files: [f`path/to/guardian/packages/somepackage`],
        });
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
        Testing.setMountPoint({
            name: a`LocalLow`,
            path: p`LocalLow`,
            isReadable: true,
            isWritable: true,
            isSystem: true,
            isScrubbable: false,
            trackSourceFileChanges: true,
        });
    }
}