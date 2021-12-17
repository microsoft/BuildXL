// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer, Tool} from "Sdk.Transformers";

const root = d`.`;
const dynamicCodeCovString = "DynamicCodeCov";

// QTest have its own logic to enfore the timeout. 
// The internal QTest timeout = qTestTimeoutSec * qTestAttemptCount
// The maximum value of qTestTimeoutSec is 600(10 mins) and qTestAttemptCount is 100. 
// So the maxium runtime of QTest is 1000 mins.
// BuildXL, therefore setting the timeout to 1005 mins, so QTest could have 5 mins for cleaning up.
@@public
export const qtestDefaultTimeoutInMilliseconds = 60300000; // 1005 mins

@@public
export const qTestTool: Transformer.ToolDefinition = {
    exe: f`${root}/bin/DBS.QTest.exe`,
    description: "CloudBuild QTest",
    runtimeDependencies: globR(d`${root}/bin`, "*"),
    untrackedDirectoryScopes: addIfLazy(Context.getCurrentHost().os === "win", () => [
        d`${Context.getMount("ProgramData").path}`,
        d`${Context.getMount("ProgramFilesX86").path}`,
        d`${Context.getMount("ProgramFiles").path}`,
        d`${Context.getMount("AppData").path}`,
        d`${Context.getMount("LocalAppData").path}`
    ]),
    dependsOnWindowsDirectories: true,
    dependsOnAppDataDirectory: true,
    prepareTempDirectory: true,
    timeoutInMilliseconds: qtestDefaultTimeoutInMilliseconds,
};
const defaultArgs: QTestArguments = {
    testAssembly: undefined,
    qTestType: undefined,
    useVsTest150: true,
    qTestPlatform: QTestPlatform.unspecified,
    qTestDotNetFramework: QTestDotNetFramework.unspecified,
    tags: [
        'test',
        'telemetry:QTest'
    ]
};

const enum CoverageOptions {
    None,
    DynamicFull,
    DynamicChangeList,
    Custom
}

function getCodeCoverageOption(args: QTestArguments): CoverageOptions {
    // With admin privilege, currently we cannot upload test result.
    // Dynamic code coverage may include uploading coverage result. So, to be on the safe side,
    // currently we force the coverage option to none when admin privilege is required.
    if (args.privilegeLevel === "admin") return CoverageOptions.None;

    // Allow disabling code coverage collection to override the global value
    if (args.qTestDisableCodeCoverage === true)
    {
        //Debug.writeLine("Coverage Disabled");
         return CoverageOptions.None;
    }

    if (Environment.hasVariable("[Sdk.BuildXL.CBInternal]CodeCoverageOption")) {
        switch (Environment.getStringValue("[Sdk.BuildXL.CBInternal]CodeCoverageOption")) {
            case CoverageOptions.DynamicChangeList.toString():
                return CoverageOptions.DynamicChangeList;
            case CoverageOptions.DynamicFull.toString():
                return CoverageOptions.DynamicFull;
            case CoverageOptions.Custom.toString():
                return CoverageOptions.Custom;
            default:
                return CoverageOptions.None;
        }
    }

    return CoverageOptions.None;
}

function getQCodeCoverageEnumType(coverageOption: CoverageOptions): string {
    switch(coverageOption) {
        case CoverageOptions.DynamicChangeList:
        case CoverageOptions.DynamicFull:
            return dynamicCodeCovString;
        default:
            return coverageOption.toString();
    }
 }

function qTestTypeToString(args: QTestArguments) : string {
    switch (args.qTestType) {
        case QTestType.msTest_retail:
            if (args.qTestMsTestPlatformRootPath)
                return "MsTest_Retail";
            else
                Contract.fail("qTestMsTestPlatformRootPath must be set for msTest_retail type.");
        case QTestType.msTest_latest:
            return args.useVsTest150 ? "MsTest_150" : "MsTest_Latest";
        case QTestType.Gradle:
            return "Gradle";
        case QTestType.Console:
            return "Console";
        default:
            Contract.fail("Invalid value specified for macro QTestType");
    };
}
function qTestPlatformToString(qTestPlatform: QTestPlatform) : string {
    switch (qTestPlatform) {
        case QTestPlatform.x86:
            return "X86";
        case QTestPlatform.x64:
            return "X64";
        case QTestPlatform.arm:
            return "Arm";
        case QTestPlatform.unspecified:
        default:
            return "Unspecified";
    };
}
function qTestDotNetFrameworkToString(qTestDotNetFramework: QTestDotNetFramework) : string {
    switch (qTestDotNetFramework) {
        case QTestDotNetFramework.framework40:
            return "Framework40";
        case QTestDotNetFramework.framework45:
            return "Framework45";
        case QTestDotNetFramework.framework46:
            return "Framework46";
        case QTestDotNetFramework.frameworkCore10:
            return "FrameworkCore10";
        case QTestDotNetFramework.frameworkCore20:
            return "FrameworkCore20";
        case QTestDotNetFramework.frameworkCore21:
            return "FrameworkCore21";
        case QTestDotNetFramework.frameworkCore22:
            return "FrameworkCore22";
        case QTestDotNetFramework.frameworkCore30:
            return "FrameworkCore30";
        case QTestDotNetFramework.netcoreApp:
            return "netcoreApp";    
        case QTestDotNetFramework.unspecified:
        default:
            return "Unspecified";
    };
}
function validateArguments(args: QTestArguments): void {
    if (args.qTestDirToDeploy && args.qTestInputs) {
        Contract.fail("Do not specify both qTestDirToDeploy and qTestInputs. Specify your inputs using only one of these arguments");
    }
}

/**
 * Find Flaky Supression file from the .config directory of source code
 */
function findFlakyFile(): File {
    let configDir = d`${Context.getMount("SourceRoot").path}/.config`;
    let flakyDir = d`${configDir}/flakytests`;
    let flakyFileName = a`CloudBuild.FlakyTests.json`;

    return File.exists(f`${flakyDir}/${flakyFileName}`)
        ?  f`${flakyDir}/${flakyFileName}`
        : (File.exists(f`${configDir}/${flakyFileName}`) ? f`${configDir}/${flakyFileName}` : undefined);
}

/**
 * Create a Manifest of input files for QTest to populate the test sandbox.
 */
function createInputsManifest(args: QTestArguments) : File {
    let inputsArray: Array<Path | RelativePath | PathAtom> = undefined;
    if (args.qTestInputs) {
        inputsArray = args.qTestInputs.mapMany(f => [f.path, f.name]);
    } else if (args.qTestDirToDeploy) {
        inputsArray = args.qTestDirToDeploy.contents.mapMany(f => [f.path, args.qTestDirToDeploy.path.getRelative(f.path)]);
    }

    if (inputsArray && inputsArray.length > 0) {
        let manifestTempDir = Context.getTempDirectory("inputsManifestDirectory");
        return Transformer.writeFile(p`${manifestTempDir}/QTestInputsManifestFile.txt`, inputsArray);
    }
    return undefined;
}

/**
 * Get context info file. Microsoft internal cloud service use only
 */
function getContextInfoFile(args: QTestArguments) : File {

    // If the privilege level is admin, QTest will most likely be executed in VM. However, currently running QTest in VM can result in
    // the following exeception:
    //
    // System.Security.Cryptography.CryptographicException: Key not valid for use in specified state.
    //  at System.Security.Cryptography.ProtectedData.Unprotect(Byte[] encryptedData, Byte[] optionalEntropy, DataProtectionScope scope)
    //  at DBS.Common.SecretStore.FileSystemSecretStoreBase.ReadBinarySecret(String secretKey)
    //  at DBS.Common.Rpc.partials.QSecretHelper.ReadFromSecretStore[T](ISecretStore secretStore, String secretKeyName)
    //  at VSTS.Common.AuthTokenUtils.GetDecryptedAuthToken(String authToken, String jobId, ISecretStore secretStore)
    //  at VSTS.Common.VssConnectionUtil.GetVssCredentials(VstsRequestInfo message, String optionalSecretStoreRoot)
    //  at DBS.QTest.VstsUtils.QTestVstsTestResultUploader.<GetVstsTestUploaderInstance>d__27.MoveNext()
    //  ---    End of stack trace from previous location where exception was thrown ---
    //  at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    //  at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task)
    //  at DBS.QTest.VstsUtils.QTestVstsTestResultUploader.<CreateAbortedTestRunAndUploadLogs>d__18.MoveNext()
    //  ---    End of stack trace from previous location where exception was thrown ---
    //  at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()
    //  at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task)
    //  at System.Runtime.CompilerServices.TaskAwaiter`1.GetResult()
    //  at DBS.QTest.Program.Main(String[] args)
    //
    // This is because, if the build is launched from Vsts, then the "VstsRequestInfo" in the context info file
    // generated by CB runner is non-null. Somehow, this setting makes QTest try to upload the test result, which
    // for sure cannot be done from the VM.
    //
    // QTest team should investigate this.

    // TODO: Renaming the internal flag passing from GBR, will remove the old one when the new one roll out from GBR
    return (args.privilegeLevel === "admin")
        ? undefined
        : (Environment.getFileValue("[Sdk.BuildXL.CBInternal]qtestContextInfo")
            || Environment.getFileValue("[Sdk.BuildXL]qtestContextInfo"));
}

/**
 * Evaluate (i.e. schedule) QTest runner with specified arguments.
 */
@@public
export function runQTest(args: QTestArguments): Result {
    args = Object.merge<QTestArguments>(defaultArgs, args);
    validateArguments(args);

    let logDir = args.qTestLogs || Context.getNewOutputDirectory("qtestlogs");
    let consolePath = p`${logDir}/qtest.stdout`;

    // If QTestArguments does not provide the Flaky Suppression File,
    // attempt to find the file at the source root.
    let flakyFile = args.qTestFlakySuppressionFile ? args.qTestFlakySuppressionFile : findFlakyFile();

    let qTestContextInfoFile = getContextInfoFile(args);
    let jsProject = args.javaScriptProject;
    let isJSProject = jsProject !== undefined;
    let changeAffectedInputListWrittenFileArg = {};

    let sandboxDir = isJSProject
        ? jsProject.projectFolder
        : Context.getNewOutputDirectory("sandbox");

    let qCodeCoverageEnumType : string = CoverageOptions.None.toString();
    let codeCoverageOption = getCodeCoverageOption(args);
    let changeAffectedInputListWrittenFile = undefined;
    if (codeCoverageOption === CoverageOptions.DynamicChangeList) {
        const parentDir = d`${logDir}`.parent;
        const leafDir = d`${logDir}`.nameWithoutExtension;
        const dir = d`${parentDir}/changeAffectedInput/${leafDir}`;
        changeAffectedInputListWrittenFile = p`${dir}/fileWithImpactedTargets.txt`;
        changeAffectedInputListWrittenFileArg = {changeAffectedInputListWrittenFile : changeAffectedInputListWrittenFile};
    }

    qCodeCoverageEnumType = getQCodeCoverageEnumType(codeCoverageOption);

    // Keep this for dev build. Office has a requirement to run code coverage for dev build and open the result with VS.
    qCodeCoverageEnumType = Environment.hasVariable("[Sdk.BuildXL]qCodeCoverageEnumType") ? Environment.getStringValue("[Sdk.BuildXL]qCodeCoverageEnumType") : qCodeCoverageEnumType;

    let commandLineArgs: Argument[] = [
        Cmd.option("--testBinary ", args.testAssembly),
        Cmd.option(
            "--runner ",
            qTestTypeToString(args)
        ),
        Cmd.option(
            "--qTestLogsDir ",
            isJSProject
            ? Artifact.sharedOpaqueOutput(logDir)
            : Artifact.output(logDir)
        ),
        Cmd.option(
            "--qtestAdapterPath ",
            Artifact.input(args.qTestAdapterPath)
        ),
        Cmd.option(
            "--qtestPlatform ",
            qTestPlatformToString(args.qTestPlatform)
        ),
        Cmd.option(
            "--buildPlatform ",
            qTestPlatformToString(args.qTestPlatform)
        ),
        Cmd.option(
            "--qtestDotNetFramework ",
            qTestDotNetFrameworkToString(args.qTestDotNetFramework)
        ),
        Cmd.option(
            "--qTestRetryOnFailureMode ",
            args.qTestRetryOnFailureMode !== undefined
            ? args.qTestRetryOnFailureMode
            : args.qTestRetryOnFailure ? "Full" : undefined
        ),
        Cmd.option("--qTestAttemptCount ", args.qTestAttemptCount),
        Cmd.option("--qTestTimeoutSec ", args.qTestTimeoutSec),
        Cmd.option(
            "--qTestRawArgFile ",
            Artifact.input(args.qTestRawArgFile)
        ),
        // Zip sandbox only when inside CloudBuild or when environment variable is specifically set for local build.
        Cmd.flag(
            "--zipSandbox",
            Environment.hasVariable("BUILDXL_IS_IN_CLOUDBUILD") ||
            (Environment.hasVariable("[Sdk.BuildXL]qTestZipSandbox") && Environment.getBooleanValue("[Sdk.BuildXL]qTestZipSandbox"))
        ),
        Cmd.flag("--debug", Environment.hasVariable("[Sdk.BuildXL]debugQTest")),
        Cmd.flag("--qTestIgnoreQTestSkip", args.qTestIgnoreQTestSkip),
        Cmd.option("--qTestAdditionalOptions ", args.qTestAdditionalOptions),
        Cmd.option("--qTestContextInfo ", Artifact.none(qTestContextInfoFile)),
        Cmd.option("--qTestBuildType ", args.qTestBuildType || "unset"),
        Cmd.option("--testSourceDir ", args.testSourceDir),
        Cmd.option("--buildSystem ", "BuildXL"),
        Cmd.option("--qTestExcludeCcTargetsFile ", Artifact.input(args.qTestExcludeCcTargetsFile)),
        Cmd.option("--QTestFlakyTestManagementSuppressionFile ", Artifact.none(flakyFile)),
        Cmd.flag("--doNotFailForZeroTestCases", args.qTestUnsafeArguments && args.qTestUnsafeArguments.doNotFailForZeroTestCases),
        Cmd.flag("--qTestHonorTrxResultSummary", args.qTestHonorTrxResultSummary),
        Cmd.option("--qTestParserType ", args.qTestParserType),
        Cmd.option("--qCodeCoverageEnumType ", qCodeCoverageEnumType),
        Cmd.option("--QTestCcTargetsFile  ", changeAffectedInputListWrittenFile),
        Cmd.option("--AzureDevOpsLogUploadMode ", args.qTestAzureDevOpsLogUploadMode),
        Cmd.flag("--EnableBlameCollector", args.qTestEnableBlameCollector),
        Cmd.flag("--EnableMsTestTraceLogging", args.qTestEnableMsTestTraceLogging),
        Cmd.option("--VstestConsoleLoggerOptions ", args.qTestVstestConsoleLoggerOptions),
        Cmd.option("--msBuildToolsRoot ", Artifact.input(args.qTestMsTestPlatformRootPath))
    ];

    if (isJSProject) {
        let jsProjectArguments: Argument[] = [
            Cmd.option(
                "--sandbox ",
                Artifact.none(sandboxDir)
            ),
            Cmd.flag("--inPlaceTestExecution", true),
            Cmd.flag("--isManaged", false)
        ];

        commandLineArgs = commandLineArgs.concat(jsProjectArguments);
    } else {
        // When invoked to run multiple attempts, QTest makes copies of sandbox
        // for each run. To ensure the sandbox does not throw access violations,
        // actual sandbox is designed to be a folder inside sandboxDir
        let qtestSandboxInternal = p`${sandboxDir}/qtest`;

        // Files passed through qTestDirToDeploy or qTestInputs argument are recorded in
        // a manifest file that QTest will then use to generate fresh sandboxes for new runs.
        let inputsFile = createInputsManifest(args);

        let dotnetProjectArguments: Argument[] = [
            Cmd.option(
                "--sandbox ",
                Artifact.none(qtestSandboxInternal)
            ),
            Cmd.option(
                "--qTestInputsManifestFile ",
                Artifact.input(inputsFile)
            ),
            Cmd.option(
                "--copyToSandbox ",
                // Use CopyToSandbox in case qTestDirToDeploy is passed but does not have any contents.
                Artifact.input(inputsFile ? undefined : args.qTestDirToDeploy)
            ),
            Cmd.option(
                "--vstestSettingsFile ",
                qCodeCoverageEnumType === dynamicCodeCovString && args.vstestSettingsFileForCoverage !== undefined
                    ? Artifact.input(args.vstestSettingsFileForCoverage)
                    : Artifact.input(args.vstestSettingsFile)
            ),
        ];

        commandLineArgs = commandLineArgs.concat(dotnetProjectArguments);
    }

    let unsafeOptions = {
        hasUntrackedChildProcesses: args.qTestUnsafeArguments && args.qTestUnsafeArguments.doNotTrackDependencies,
        untrackedPaths: [
            ...addIf(qTestContextInfoFile !== undefined, qTestContextInfoFile),
            ...addIf(flakyFile !== undefined, flakyFile),
            ...addIfLazy(args.qTestUntrackedPaths !== undefined, () => args.qTestUntrackedPaths)
        ],
        untrackedScopes: [
            // Untracking Recyclebin here to primarily unblock user scenarios that
            // deal with soft-delete and restoration of files from recycle bin.
            d`${sandboxDir.pathRoot}/$Recycle.Bin`,
            ...(args.qTestUntrackedScopes || []),
            // Untrack logDir to keep logs intact between multiple retries.
            // This also means that logDir will not be cached between build sessions.
            ...addIf(isJSProject, logDir),
            // To ensure that dmps are generated during crashes, QTest now includes procdmp.exe
            // However, this tool reads dbghelp.dll located in the following directory in CloudBuild machines
            // This should technically be part of the tool definition, but we want to make sure
            // that this scope does not get overridden when customers override qtesttool.
            d`C:/Debuggers`
        ],
        requireGlobalDependencies: true,
        passThroughEnvironmentVariables: isJSProject ? jsProject.passThroughEnvironmentVariables : undefined,
    };

    let envVars = [
        ...(args.qTestEnvironmentVariables || []),
        ...(Environment.hasVariable("__CLOUDBUILD_DOTNETCORE_DEPLOYMENT_PATH__") ? [{name: "__CLOUDBUILD_DOTNETCORE_DEPLOYMENT_PATH__", value: Environment.getStringValue("__CLOUDBUILD_DOTNETCORE_DEPLOYMENT_PATH__")}] : [])
    ];

    let outputs = undefined;
    if (isJSProject)
    {
        //TODO: Frontend should be filtering the TEMP variables. We can remove this filter after or in that PR.
        envVars = envVars.concat(jsProject.environmentVariables.filter(o => o.name !== "TEMP" && o.name !== "TMP"));
        outputs = jsProject.outputs.map(o => typeof o === "Directory" ? {directory: o, kind: "shared"} : {artifact: o, existence: "optional"});
    }

    let result = Transformer.execute(
        Object.merge<Transformer.ExecuteArguments>(
            args.tools && args.tools.exec,
            {
                tool: args.qTestTool ? args.qTestTool : qTestTool,
                tags: args.tags,
                description: args.description,
                arguments: commandLineArgs,
                consoleOutput: consolePath,
                workingDirectory: sandboxDir,
                tempDirectory: isJSProject ? jsProject.tempDirectory : Context.getTempDirectory("qtestRunTemp"),
                weight: args.weight,
                environmentVariables: envVars,
                disableCacheLookup: Environment.getFlag("[Sdk.BuildXL]qTestForceTest"),
                additionalTempDirectories : isJSProject ? [] : [sandboxDir],
                privilegeLevel: args.privilegeLevel,
                dependencies: [
                    //When there are test failures, and PDBs are looked up to generate the stack traces,
                    //the original location of PDBs is used instead of PDBs in test sandbox. This is
                    //a temporary solution until a permanent fix regarding the lookup is identified
                    ...(args.qTestInputs || (args.qTestDirToDeploy ? args.qTestDirToDeploy.contents : [])),
                    ...(args.qTestRuntimeDependencies || []),
                    ...(isJSProject ? jsProject.inputs : []),
                ],
                unsafe: unsafeOptions,
                retryExitCodes: [2, 42],
                acquireSemaphores: args.qTestAcquireSemaphores,
                retryAttemptEnvironmentVariable: "QTEST_RETRIES_EXECUTED",
                processRetries: (args.qTestAttemptCount ? args.qTestAttemptCount : 0) + 3,
                allowUndeclaredSourceReads: isJSProject,
                enforceWeakFingerprintAugmentation: isJSProject ? true : undefined,
                outputs: outputs,
            },
            changeAffectedInputListWrittenFileArg
        )
    );

    const qTestLogsDir: StaticDirectory = result.getOutputDirectory(logDir);

    // If code coverage is enabled, schedule a pip that will perform coverage file upload.
    if (qCodeCoverageEnumType === dynamicCodeCovString || qCodeCoverageEnumType === CoverageOptions.Custom.toString()) {
        const parentDir = d`${logDir}`.parent;
        const leafDir = d`${logDir}`.nameWithoutExtension;
        const coverageLogDir = d`${parentDir}/CoverageLogs/${leafDir}`;
        const coverageConsolePath = p`${coverageLogDir}/coverageUpload.stdout`;
        let qtestCodeCovUploadTempDirectory = Context.getTempDirectory("qtestCodeCovUpload");

        const commandLineArgsForUploadPip: Argument[] = [
            Cmd.option("--qTestLogsDir ", isJSProject? Artifact.none(coverageLogDir) : Artifact.output(coverageLogDir)),
            Cmd.option("--qTestContextInfo ", Artifact.none(qTestContextInfoFile)),
            Cmd.option("--coverageDirectory ", Artifact.input(qTestLogsDir)),
            Cmd.option("--qTestBuildType ", args.qTestBuildType || "Unset"),
            Cmd.flag("--logging", true),
            Cmd.option("--buildPlatform ", qTestPlatformToString(args.qTestPlatform)),
            Cmd.option("--qCodeCoverageEnumType ", qCodeCoverageEnumType)
        ];

        Transformer.execute({
            tool: args.qTestTool ? args.qTestTool : qTestTool,
            tags: [
                "test",
                "telemetry:qtest", 
                ...(args.tags || []),
            ],
            description: "QTest Coverage Upload",
            arguments: commandLineArgsForUploadPip,
            consoleOutput: coverageConsolePath,
            workingDirectory: qtestCodeCovUploadTempDirectory,
            disableCacheLookup: true,
            unsafe: unsafeOptions,
            retryExitCodes: [2]
        });
    }

    return <Result>{
        console: result.getOutputFile(consolePath),
        qTestLogs: qTestLogsDir,
        executeResult: result
    };
}

/**
 * Specifies the type of runner that need to be used to execute tests
 */
@@public
export const enum QTestType {
    /** Uses VsTest 12.0 to execute tests */
    @@Tool.option("--runner MsTest_Latest")
    msTest_latest = 1,
    @@Tool.option("--runner Gradle")
    Gradle = 2,
    @@Tool.option("--runner Console")
    Console = 3,
    /** Uses vstest from the provided Microsoft.TestPlatform package to execute tests. Microsoft.TestPlatform above 15.0.0 is supported. */
    @@Tool.option("--runner MsTest_Retail")
    msTest_retail = 4
}

/**
 * Specifies the Platform that need to be used to execute tests
 */
@@public
export const enum QTestPlatform {
    @@Tool.option("--qtestPlatform unspecified")
    unspecified = 1,
    @@Tool.option("--qtestPlatform x86")
    x86,
    @@Tool.option("--qtestPlatform x64")
    x64,
    @@Tool.option("--qtestPlatform arm")
    arm,
}

/**
 * Specifies the Framework version that need to be used to execute tests
 */
@@public
export const enum QTestDotNetFramework {
    @@Tool.option("--qtestDotNetFramework unspecified")
    unspecified = 1,
    @@Tool.option("--qtestDotNetFramework framework40")
    framework40,
    @@Tool.option("--qtestDotNetFramework framework45")
    framework45,
    @@Tool.option("--qtestDotNetFramework framework46")
    framework46,
    @@Tool.option("--qtestDotNetFramework frameworkCore10")
    frameworkCore10,
    @@Tool.option("--qtestDotNetFramework frameworkCore20")
    frameworkCore20,
    @@Tool.option("--qtestDotNetFramework frameworkCore21")
    frameworkCore21,
    @@Tool.option("--qtestDotNetFramework frameworkCore22")
    frameworkCore22,
    @@Tool.option("--qtestDotNetFramework frameworkCore30")
    frameworkCore30,
    @@Tool.option("--qtestDotNetFramework netcoreApp")
    netcoreApp
}

/**
 * Arguments of DBS.QTest.exe
 */
// @@toolName("DBS.QTest.exe")
@@public
export interface QTestArguments extends Transformer.RunnerArguments {
    /** Option to specify the location of the the qtest executable. */
    qTestTool?: Transformer.ToolDefinition,
    /** The assembly built from test projects that contain the unit tests. */
    testAssembly: Artifact | Path;
    /** Directory that includes all necessary artifacts to run the test, will be copied to sandbox by QTest */
    qTestDirToDeploy?: StaticDirectory;
    /** Explicit specification of all inputs instead of using qTestDirToDeploy, this file will be copied to sandbox by QTest */
    qTestInputs?: File[];
    /** Explicit specification of extra run time dependencies, will not be copied to sandbox */
    qTestRuntimeDependencies ?: Transformer.InputArtifact[];
    /** Describes the runner to launch tests */
    qTestType?: QTestType;
    /** This makes DBS.QTest.exe use custom test adapters for vstest from a given path in the test run. */
    qTestAdapterPath?: StaticDirectory;
    /** Platform that need to be used to execute tests */
    qTestPlatform?: QTestPlatform;
    /** Framework version that need to be used to execute tests */
    qTestDotNetFramework?: QTestDotNetFramework;
    /** Optional directory where all QTest logs can be written to */
    qTestLogs?: Directory;
    /** Specifies to automatically retry failing tests */
    qTestRetryOnFailure?: boolean;
    /** Specifies to the mode under which to automatically retry failing tests:
     *      'Full': Retry full test target.
     *      'Failed': Retry only the failed test cases from the test target.
     *      'None': Do not retry.
    */
    qTestRetryOnFailureMode?: "Full" | "Failed" | "None";
    /** Executes tests for specified number of times. A test is considered as passed So t
     * only when all attempts pass. Maximum allowed value is 100.*/
    qTestAttemptCount?: number;
    /** Raw arguments that are passed as it is to the underlying test runner */
    qTestRawArgFile?: File;
    /** Maximum runtime allowed for QTests in seconds. Cannot exceed maximum of 600 seconds. */
    qTestTimeoutSec?: number;
    /** Helps ignore the QTestSkip test case filter */
    qTestIgnoreQTestSkip?: boolean;
    /** Helps to use VsTest 15.0 instead of default VsTest 12.0 */
    useVsTest150?: boolean;
    /** Optional arguments that will be passed on to the corresponding test runner. */
    qTestAdditionalOptions?: string;
    /** Path to runsettings file that will be passed on to vstest.console.exe. */
    vstestSettingsFile?: File;
    /** vstestSettingsFileForCoverage instead of vstestSettingsFile will be passed on to vstest.console.exe when code coverage is enabled.*/
    vstestSettingsFileForCoverage?: File;
    /** Optionally override to increase the weight of test pips that require more machine resources */
    weight?: number;
    /** Privilege level required by this process to execute. */
    privilegeLevel?: "standard" | "admin";
    /** Specifies the build type */
    qTestBuildType?: string;
    /** Specifies the environment variables to forward to qtest */
    qTestEnvironmentVariables?: Transformer.EnvironmentVariable[];
    /** Specify the path relative to enlistment root of the sources from which the test target is built */
    testSourceDir?: RelativePath;
    /** File which contains a list of target file names excluded for code coverage processing*/
    qTestExcludeCcTargetsFile?: File;
    /** File where Flaky Test Management stores suppression data*/
    qTestFlakySuppressionFile? : File;
    /** Unsafe arguments for QTest. */
    qTestUnsafeArguments?: QTestUnsafeArguments;
    /** Semaphores to acquire */
    qTestAcquireSemaphores?: Transformer.SemaphoreInfo[];
    /** Overrides global setting to disable code coverage collection on this test binary */
    qTestDisableCodeCoverage?: boolean;
    /** Paths out of scope of the build or test project where unsafe file access needs to be permitted */
    qTestUntrackedPaths?: (File | Directory)[];
    /** Directories and their recursive content that are out of scope of the build or test project where unsafe file access needs to be permitted */
    qTestUntrackedScopes?: Directory[];
    /** Specifies whether to fail if vstest platform reports a failure in the trx in the absence of failed test cases. */
    qTestHonorTrxResultSummary?: boolean;
    /** Encapsulates execution details needed to run tests for JavaScript based projects. */
    javaScriptProject?: JavaScriptProject;
    /** Specifies what parser to use to parse QTest results */
    qTestParserType?: string;
    /** Specifies the upload behavior to Azure DevOps for QTest logs. Default mode is OnlyFailedTargets.*/
    qTestAzureDevOpsLogUploadMode?: "AllTargets" | "OnlyFailedTargets" | "None"; 
    /** When true, additional options are passed to enable blame collector for collecting dmp files.*/
    qTestEnableBlameCollector?: boolean;
    /** When true, the DBS.QTest.exe invokes VsTest with diagnostic tracing.*/
    qTestEnableMsTestTraceLogging?: boolean;
    /** Allows additional options to be appended to console logger.*/
    qTestVstestConsoleLoggerOptions?: string;
    /** Specifies the path for tools/net451 directory from Microsoft.TestPlatform package used to acquire vstest.console.exe */
    qTestMsTestPlatformRootPath?: StaticDirectory;
    /** Nested tool options */
    tools?: {
        /** 
         * Options for tool execution
         * */
        exec?: Transformer.ExecuteArgumentsComposible;
        wrapExec?: (exec: Transformer.ExecuteArguments) => Transformer.ExecuteArguments;
    };
}

/**
 * Unsafe arguments for QTest.
 */
@@public
export interface QTestUnsafeArguments {
    doNotFailForZeroTestCases: boolean;
    doNotTrackDependencies: boolean;
}

/**
 * Test results from a vstest.console.exe run
 */
@@public
export interface Result {
    /** Console output from the test run. */
    console: DerivedFile;
    /** Location of the QTestLogs directory to consume any other outputs of QTest */
    qTestLogs: StaticDirectory;
    /** Transformer.ExecuteResult object itself. */
    executeResult: Transformer.ExecuteResult;
}
