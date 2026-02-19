// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// xUnit v3 test framework for BuildXL.
//
// xunit v3 requires test projects to be executables with entry points.
// BuildXLSdk.test() detects v3 frameworks (by name containing "XUnitV3") and
// automatically builds as executable and injects the boilerplate source.
//
// Two usage modes:
//   - Per-project: BuildXLSdk.test({ useXUnitV3: true }) — QTest wrapping controlled globally
//   - Explicit:    BuildXLSdk.test({ testFramework: XUnitV3.framework }) — opt out of QTest
//
// TODO: Port the v2 custom TestFramework features to v3. The v2 TestFrameworkOverride
// (TestUtilities.XUnit/Extensions/) provides per-test timeouts, MTA thread support,
// contract violation fail-fast, hash-based test distribution, console stream isolation,
// and SynchronizationContext clearing. Most of these can be implemented via a custom
// FactAttribute with a default timeout, BeforeAfterTestAttribute, and a base test class
// rather than a full framework override.
//
// TODO: Create a TestUtilities.XUnitV3 assembly that ports XunitBuildXLTest,
// TemporaryStorageTestBase, XAssert, and the portable parts of TestUtilities.XUnit
// to xunit v3 (namespace change from Xunit.Abstractions to Xunit, drop Extensions/).

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";
import {isDotNetCore, DotNetCoreVersion, Framework} from "Sdk.Managed.Shared";
import * as Managed      from "Sdk.Managed";
import * as Shared       from "Sdk.Managed.Shared";
import * as Deployment   from "Sdk.Deployment";

export declare const qualifier : Managed.TargetFrameworks.All;

// xunit v3 compile references
const xunitV3References : Managed.Reference[] = [
    importFrom("xunit.v3.assert").pkg,
    importFrom("xunit.v3.extensibility.core").pkg,
    importFrom("xunit.v3.common").pkg,
];

// The v3 test adapter package (aliased in config.dsc to avoid conflicts with v2)
const v3AdapterPackage = importFrom("xunit.runner.visualstudio.v3").Contents.all;

// xunit v3 boilerplate source file (entry point, Microsoft Testing Platform registration, runner reporters).
// Injected into test projects by BuildXLSdk.test() when a v3 framework is detected.
@@public
export const boilerplateSource = f`XunitV3Boilerplate.cs`;

/**
 * v3 framework that runs tests directly via the in-process console runner.
 * Mirrors XUnit.framework for v2. Use directly to opt out of QTest,
 * or let BuildXLSdk.test() wrap with QTest automatically when useXUnitV3 is set.
 */
@@public
export const framework : Managed.TestFramework = {
    compileArguments: processArguments,
    additionalRuntimeContent: additionalRuntimeContent,
    runTest: runStandaloneV3,
    name: "XUnitV3",
};

function processArguments(args: Managed.TestArguments): Managed.TestArguments {
    return Object.merge<Managed.TestArguments>(
        {
            references: [
                ...xunitV3References,
                importFrom("xunit.v3.runner.inproc.console").pkg,
                importFrom("xunit.v3.runner.common").pkg,
                importFrom("xunit.v3.core.mtp-v1").pkg,
                importFrom("xunit.v3.mtp-v1").pkg,
                // xunit.abstractions is needed by the v3 test adapter at runtime.
                // Added as a managed reference (not loose file) to avoid deployment
                // conflicts when v2 projects reference a v3 test assembly.
                importFrom("xunit.abstractions").pkg,
                importFrom("Microsoft.Testing.Platform").pkg,
                importFrom("Microsoft.Testing.Platform.MSBuild").pkg,
                importFrom("Microsoft.Testing.Extensions.Telemetry").pkg,
                importFrom("Microsoft.Testing.Extensions.TrxReport.Abstractions").pkg,
                importFrom("Microsoft.TestPlatform.ObjectModel").pkg,
                importFrom("Microsoft.TestPlatform.TestHost").pkg,
                importFrom("Microsoft.NET.Test.Sdk").pkg,
            ],
        },
        isDotNetCore(qualifier.targetFramework)
            ? {
                deployRuntimeConfigFile: true,
                deploymentStyle: "selfContained",
            } 
            : {
                // v3 packages target netstandard2.0, so net472 needs the netstandard facade
                // and System.Collections.Immutable (transitive dependency of xunit v3)
                references: [
                    importFrom("Sdk.Managed.Frameworks.Net472").NetFx.Netstandard.dll,
                    importFrom("System.Collections.Immutable").pkg,
                ],
                // xunit v3 transitively references System.Collections.Immutable 6.0.0.0,
                // but the deployed version is 9.0.0.0. The global binding redirect only
                // covers up to 1.5.0.0, so we need a wider redirect here.
                assemblyBindingRedirects: [
                    {
                        name: "System.Collections.Immutable",
                        publicKeyToken: "b03f5f7f11d50a3a",
                        culture: "neutral",
                        oldVersion: "0.0.0.0-9.0.0.0",
                        newVersion: "9.0.0.0",
                    },
                ],
            },
        args);
}

function additionalRuntimeContent(args: Managed.TestArguments) : Deployment.DeployableItem[] {
    if (isDotNetCore(qualifier.targetFramework)) {
        return [
            // v3 test adapter for vstest discovery and execution
            v3AdapterPackage.getFile(r`build/net8.0/xunit.runner.visualstudio.testadapter.dll`),
            // Runner utility DLL needed at runtime (from xunit.v3.core.mtp-v1)
            importFrom("xunit.v3.core.mtp-v1").Contents.all.getFile(r`_content/runners/netcore/xunit.v3.runner.utility.netcore.dll`),
        ];
    }

    return [
        // v3 test adapter for vstest discovery and execution (net472)
        v3AdapterPackage.getFile(r`build/net472/xunit.runner.visualstudio.testadapter.dll`),
        // Runner utility DLL needed at runtime (from xunit.v3.core.mtp-v1)
        importFrom("xunit.v3.core.mtp-v1").Contents.all.getFile(r`_content/runners/netfx/xunit.v3.runner.utility.netfx.dll`),
    ];
}

// =============================================================================
// Standalone runner (v3 in-process console runner)
// =============================================================================

function runStandaloneV3(args : Managed.TestRunArguments) : File[] {
    // Handle parallelGroups by splitting into separate test runs per group
    if (args.parallelGroups && args.parallelGroups.length > 0) {
        if (args.limitGroups && args.limitGroups.length > 0) {
            Contract.fail("XUnit runner does not support combining parallel runs with restricting or skipping test groups");
        }
        return runMultipleStandaloneV3(args);
    }

    let logFolder = Context.getNewOutputDirectory('xunit-logs');
    let xmlResultFile = p`${logFolder}/xunit.results.xml`;

    let testDeployment = args.testDeployment;

    // In v3, the test exe IS the runner. For .NET Core, the primary file is a DLL
    // that gets executed via the native host (.exe) or via dotnet exec.
    const tool : Transformer.ToolDefinition = Managed.Factory.createTool({
        exe: testDeployment.contents.getFile(testDeployment.primaryFile.name),
        dependsOnCurrentHostOSDirectories: true,
        timeoutInMilliseconds: 600 * 1000 * (args.timeoutMultiplier ? args.timeoutMultiplier : 1),
    });

    const testMethod = Environment.getStringValue("[UnitTest]Filter.testMethod");
    const testClass  = Environment.getStringValue("[UnitTest]Filter.testClass");
    const runningInLinux = Context.getCurrentHost().os === "unix";

    // Default trait exclusions on non-Windows (matching v2 xunit runner behavior)
    let skipTraits : string[] = [
        ...(args.skipGroups || []),
    ];
    if (Context.getCurrentHost().os !== "win") {
        skipTraits = [
            "WindowsOSOnly",
            "QTestSkip",
            "Performance",
            "SkipDotNetCore",
            ...addIf(runningInLinux, "SkipLinux"),
            ...skipTraits,
        ];
    }

    // Build command line arguments for xunit v3 in-process runner
    let arguments : Argument[] = [
        Cmd.option("-xml ", Artifact.output(xmlResultFile)),
        Cmd.option("-parallel ", "none"),
        Cmd.option("-method ", testMethod),
        Cmd.option("-class ", testClass),
    ];

    // Trait inclusion filters (limitGroups)
    if (args.limitGroups) {
        for (let group of args.limitGroups) {
            arguments = [...arguments, Cmd.option("-trait ", `Category=${group}`)];
        }
    }

    // Trait exclusion filters (skipGroups + platform defaults)
    for (let group of skipTraits) {
        arguments = [...arguments, Cmd.option("-trait- ", `Category=${group}`)];
    }

    let passthroughEnvVars : string[] = [];

    let unsafeArgs: Transformer.UnsafeExecuteArguments = {
        untrackedScopes: [
            ...addIf(args.untrackTestDirectory === true, testDeployment.contents.root),
            ...((args.unsafeTestRunArguments && args.unsafeTestRunArguments.untrackedScopes) || []),
        ],
        untrackedPaths : (
            args.unsafeTestRunArguments && 
            args.unsafeTestRunArguments.untrackedPaths && 
            args.unsafeTestRunArguments.untrackedPaths.map(path => typeof(path) === "File" 
                ? <File>path 
                : File.fromPath(testDeployment.contents.root.combine(<RelativePath>path)))) 
        || [],
        // Some EBPF-related test infra makes decisions based on whether we are running on ADO or not
        passThroughEnvironmentVariables: [...passthroughEnvVars, "TF_BUILD"],
        childProcessesToBreakawayFromSandbox: addIfLazy(!Context.isWindowsOS(), () => [a`bxl-ebpf-runner`]),
    };

    const enableLinuxEBPF = Environment.getStringValue(Managed.TestEnvironment.EnableLinuxEBPFSandboxForTestsEnvVar);

    let execArguments : Transformer.ExecuteArguments = {
        tool: tool,
        tags: [
            "test",
            ...(args.tags || [])
        ],
        arguments: arguments,
        environmentVariables: [
            ...(args.envVars || []), {
                name: Managed.TestEnvironment.EnableLinuxEBPFSandboxForTestsEnvVar, 
                value: enableLinuxEBPF === undefined ? "0" : enableLinuxEBPF
            }],
        dependencies: args.untrackTestDirectory ? testDeployment.contents.contents : [ testDeployment.contents, ...(testDeployment.targetOpaques || []) ],
        warningRegex: "^(?=a)b",
        workingDirectory: testDeployment.contents.root,
        retryExitCodes: Environment.getFlag("RetryXunitTests") ? [1, 3] : [],
        processRetries: Environment.hasVariable("NumXunitRetries") ? Environment.getNumberValue("NumXunitRetries") : undefined,
        unsafe: unsafeArgs,
        privilegeLevel: args.privilegeLevel,
        weight: args.weight,
    };

    // Non-Windows: additional environment and untracked paths
    if (Context.getCurrentHost().os !== "win") {
        execArguments = execArguments.merge<Transformer.ExecuteArguments>({
            environmentVariables: [
                {name: "COMPlus_DefaultStackSize", value: "200000"},
            ],
            unsafe: {
                untrackedPaths: addIf(!Context.isWindowsOS(),
                    f`${Environment.getDirectoryValue("HOME")}/.CFUserTextEncoding`,
                    f`${Environment.getDirectoryValue("HOME")}/.sudo_as_admin_successful`
                ),
                untrackedScopes: addIfLazy(!Context.isWindowsOS(), () => [ d`/mnt`, d`/init`, d`/usr`, d`/tmp/.dotnet/shm/global` ]),
                passThroughEnvironmentVariables: [
                    "HOME",
                    "TMPDIR",
                    "USER"
                ]
            },
        });
    }

    // Wrap in dotnet exe for .NET Core
    const targetFramework = qualifier.targetFramework;
    if (isDotNetCore(targetFramework)) {
        execArguments = importFrom("Sdk.Managed.Frameworks").Helpers.wrapInDotNetExeForCurrentOs(targetFramework, execArguments);
    }

    execArguments = Managed.TestHelpers.applyTestRunExecutionArgs(execArguments, args);

    const result = Transformer.execute(execArguments);

    // Copy results to log directory for preservation
    const qualifierRelative = r`${qualifier.configuration}/${qualifier.targetFramework}/${qualifier.targetRuntime}`;
    const parallelRelative = args.parallelBucketIndex !== undefined ? `${args.parallelBucketIndex}` : `0`;
    const privilege = args.privilegeLevel || "standard";
    const xunitLogDir = d`${Context.getMount("LogsDirectory").path}/XUnit/${Context.getLastActiveUseModuleName()}/${Context.getLastActiveUseName()}/${qualifierRelative}/${privilege}/${parallelRelative}`;
    result.getOutputFiles().map(f => Transformer.copyFile(f, p`${xunitLogDir}/${f.name}`));

    return [
        xmlResultFile && result.getOutputFile(xmlResultFile),
    ];
}

/**
 * Runs tests split by parallelGroups: one run per group (trait-filtered),
 * plus a final run excluding all groups. Mirrors v2 runMultipleConsoleTests.
 */
function runMultipleStandaloneV3(args : Managed.TestRunArguments) : File[] {
    // Run tests for each parallel group (filtered by trait)
    for (let testGroup of args.parallelGroups) {
        runStandaloneV3(args.override<Managed.TestRunArguments>({
            parallelGroups: undefined,
            limitGroups: [testGroup],
        }));
    }

    // Final run: all tests NOT in any of the parallel groups
    return runStandaloneV3(args.override<Managed.TestRunArguments>({
        parallelGroups: undefined,
        skipGroups: [...(args.skipGroups || []), ...args.parallelGroups],
    }));
}
