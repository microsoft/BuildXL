// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

config({
    resolvers: [
        {
            kind: "DScript",
            modules: [
                { moduleName: "GuardianBuild", projects: [f`GuardianBuild.dsc`] }
            ]
        },
    ],
    mounts: [
        {
            // This config file may not be in the same directory as the enlistment root
            name: a`EnlistmentRoot`,
            isReadable: true,
            isWritable: true,
            isScrubbable: false,
            path: Environment.getPathValue("BUILDXL_ENLISTMENT_ROOT"),
            trackSourceFileChanges: true
        },
        {
            // The Guardian tool path on Cloudbuild contains the Guardian/GDNP packages, Guardian tool packages, and dotnet binaries
            name: a`GuardianToolPath`,
            isReadable: true,
            isWritable: true,
            isScrubbable: false,
            path: Environment.getPathValue("TOOLPATH_GUARDIAN"),
            trackSourceFileChanges: true
        }
    ]
});