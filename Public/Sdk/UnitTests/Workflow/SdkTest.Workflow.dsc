// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
namespace StandardSdk.Workflow {

    // The SDK works for Windows and Linux builds, but is currently tested only on Windows builds.
    // The workflow SDK has different implementations for Windows and Linux. For example, the script
    // task of the SDK is implemented by calling cmd.exe on Windows and bash on Linux. Also, the task
    // can have different file dependencies and untracked directories on Windows and Linux. To accomodate
    // these differences, we need to have two different sets of lkg files for Windows and Linux. Unfortunately,
    // the current test infrastructure does not have such a support.
    //
    // TODO: Enable the test for Linux builds. https://dev.azure.com/mseng/1ES/_workitems/edit/2075778
    export const workflowTest = Context.getCurrentHost().os === "win" && BuildXLSdk.sdkTest({
        testFiles: [f`Test.Workflow.dsc` ],
        sdkFolders: [ d`${Context.getMount("SdkRoot").path}/Workflow` ],
        autoFixLkgs: false
    });

    // Only run this test on a specific qualifier: Windows x64, .NET 6.0, and debug configuration.
    // This is to avoid the test to fail on other platforms due to the use of absolute paths in the generated nuget.config when running the nuget restore
    // task. The absolute path is sensitive to the qualifier used to run the test.
    const shouldRunNuGetTest = Context.getCurrentHost().os === "win"
        && qualifier.targetRuntime === "win-x64"
        && qualifier.targetFramework === "net6.0"
        && qualifier.configuration === "debug";

    export const nugetTest = shouldRunNuGetTest && BuildXLSdk.sdkTest({
        testFiles: [f`Test.Workflow.NuGet.dsc` ],
        sdkFolders: [ d`${Context.getMount("SdkRoot").path}/Workflow` ],
        autoFixLkgs: false
    });
}
