// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import {CoreRT}                     from "Sdk.MacOS";

import * as Csc from "Sdk.Managed.Tools.Csc";
import * as Branding from "BuildXL.Branding";
import * as Deployment from "Sdk.Deployment";

import * as Managed from "Sdk.Managed";
import * as Shared from "Sdk.Managed.Shared";
import * as XUnit from "Sdk.Managed.Testing.XUnit";
import * as QTest from "Sdk.Managed.Testing.QTest";
import * as Frameworks from "Sdk.Managed.Frameworks";
import * as Net451 from "Sdk.Managed.Frameworks.Net451";
import * as Net461 from "Sdk.Managed.Frameworks.Net461";
import * as Net472 from "Sdk.Managed.Frameworks.Net472";

import * as ResXPreProcessor from "Sdk.BuildXL.Tools.ResXPreProcessor";
import * as LogGenerator from "Sdk.BuildXL.Tools.LogGenerator";
import * as ScriptSdkTestRunner from "Sdk.TestRunner";
import * as Contracts from "Tse.RuntimeContracts";

@@public
export * from "Sdk.Managed";

@@public
export const NetFx = qualifier.targetFramework === "net472" ?
                        Net472.withQualifier({targetFramework: "net472"}).NetFx :
                        qualifier.targetFramework === "net461" ?
                            Net461.withQualifier({targetFramework: "net461"}).NetFx :
                            Net451.withQualifier({targetFramework: "net451"}).NetFx;

const testTag = "test";
export const publicKey = "0024000004800000940000000602000000240000525341310004000001000100BDD83CF6A918814F5B0395F20B6AA573B872FCDDB8B121F162BDD7D5EB302146B2EA6D7E6551279FF9D62E7BEA417ACAE39BADC6E6DECFE45BA7B3AD70AF432A1AA587343AA67647A4D402A0E2D011A9758AAB9F0F8D1C911D554331E8176BE34592BADC08BC94BBD892AF7BCB72AC613F37E4B57A6E18599535211FEF8A7EBA";

const envVarNamePrefix = "[Sdk.BuildXL]";
const redisConnectionStringEnvVarName = "CloudStoreRedisConnectionString";

const brandingDefines = [
    { key: "ShortProductName", value: Branding.shortProductName},
    { key: "LongProductName", value: Branding.longProductName},
    { key: "ShortScriptName", value: Branding.shortScriptName},
    { key: "MainExecutableName", value: Branding.mainExecutableName},
];

@@public
export interface Arguments extends Managed.Arguments {
    /** Provide switch to turn skip tool that adds GetTypeInfo() calls to generated resource code, so the tool can be compiled */
    skipResourceTranslator?: boolean;

    /** Allows projects that should be added as default references to skip adding these to avoid cycles */
    skipDefaultReferences?: boolean;

    /** If true, StyleCop.Analyzers are enabled. */
    enableStyleCopAnalyzers?: boolean;

    /** Root namespace.  If undefined, the value of the "assemblyName" field is used." */
    rootNamespace?: string;

    /** Whether to run LogGen. */
    generateLogs?: boolean;
    
    /** Whether we can use the fast lite version of loggen. Defaults to true. */
    generateLogsLite?: boolean;

    /** Disables assembly signing with the BuildXL key. */
    skipAssemblySigning?: boolean;

    /** Configures which asserts should be checked at runtime. All by default.*/
    contractsLevel?: Contracts.ContractsLevel;

    /** The assemblies that are internal visible for this assembly */
    internalsVisibleTo?: string[],

    /** Temporary workaround for old names */
    cacheOldNames?: {
        namespace: string,
        factoryClass: string,
    }[]
}

@@public
export interface Result extends Managed.Assembly {
}

@@public
export interface TestArguments extends Arguments, Managed.TestArguments {
    standaloneTestFolder?: PathAtom;
}

@@public
export interface TestResult extends Managed.TestResult {
    adminTestResults?: TestResult;
}

/**
 * Returns if the current qualifier is targeting .NET Core
 */
@@public
export const isDotNetCoreBuild : boolean = qualifier.targetFramework === "netcoreapp3.0" || qualifier.targetFramework === "netstandard2.0";

@@public
export const isFullFramework : boolean = qualifier.targetFramework === "net451" || qualifier.targetFramework === "net461" || qualifier.targetFramework === "net472";

@@public
export const isTargetRuntimeOsx : boolean = qualifier.targetRuntime === "osx-x64";

/** Only run unit tests for one qualifier and also don't run tests which target macOS on Windows */
@@public
export const restrictTestRunToDebugNet461OnWindows =
    qualifier.configuration !== "debug" ||
    // Running tests for .NET Core App 3.0 and 4.7.2 frameworks only.
    (qualifier.targetFramework !== "netcoreapp3.0" && qualifier.targetFramework !== "net472") ||
    (Context.isWindowsOS() && qualifier.targetRuntime === "osx-x64");

/***
* Whether drop tooling is included with the BuildXL deployment
*/
@@public
export const isDropToolingEnabled = Flags.isMicrosoftInternal && isFullFramework;

namespace Flags {
    export declare const qualifier: {};

    @@public
    export const isMicrosoftInternal = Environment.getFlag("[Sdk.BuildXL]microsoftInternal");

    @@public
    export const isVstsArtifactsEnabled = isMicrosoftInternal;

    /***
    * Whether tests are configured to run with QTest. Not whether QTest gets bundled with the BuildXL deployment
    */
    @@public
    export const isQTestEnabled = isMicrosoftInternal && Environment.getFlag("[Sdk.BuildXL]useQTest");

    /**
     * Whether we are generating VS solution.
     * We are using this flag to filter out some deployment items that can cause race in the generated VS project files.
     */
    @@public
    export const genVSSolution = Environment.getFlag("[Sdk.BuildXL]GenerateVSSolution");

    /**
     * Temporary flag to exclude building BuildXL.Explorer.
     * BuildXL.Explorer is broken but building it can take a long time in CB environment.
     */
    @@public
    export const excludeBuildXLExplorer = Environment.getFlag("[Sdk.BuildXL]ExcludeBuildXLExplorer");

    /**
     * Build tests that require admin privilege in VM.
     */
    @@public
    export const buildRequiredAdminPrivilegeTestInVm = Environment.getFlag("[Sdk.BuildXL]BuildRequiredAdminPrivilegeTestInVm");
}

@@public
export const devKey = f`BuildXL.DevKey.snk`;

@@public
export const cacheRuleSet = f`BuildXl.Cache.ruleset`;

@@public
export const dotNetFramework = isDotNetCoreBuild
? qualifier.targetRuntime
: qualifier.targetFramework;

/**
 * Builds a BuildXL library project, resulting in a DLL.
 * Does so by invoking `build` specifying `library` as the target type.
 */
@@public
export function library(args: Arguments): Managed.Assembly {
    let csFiles : File[] = undefined;

    if (args.cacheOldNames)
    {
        csFiles = args.cacheOldNames.map(cacheOldName =>
            Transformer.writeAllLines({
                outputPath: p`${Context.getNewOutputDirectory("oldcache")}/cache.g.cs`,
                lines: [
                    `namespace Cache.${cacheOldName.namespace}`,
                    `{`,
                    `    /// <nodoc />`,
                    `    public class ${cacheOldName.factoryClass} : BuildXL.Cache.${cacheOldName.namespace}.${cacheOldName.factoryClass}`,
                    `    {`,
                    `    }`,
                    `}`,
                ]
            })
        );

        args = args.merge<Arguments>({
            sources: csFiles,
        });
    }

    args = processArguments(args, "library");
    let result = Managed.library(args);

    if (args.cacheOldNames)
    {
        let assemblyWithOldName = library({
            assemblyName: args.assemblyName.replace("BuildXL.", ""),
            sources: csFiles,
            references: [
                result
            ],
        });

        result = result.merge<Managed.Assembly>({
            runtimeContent: {
                contents: [
                    assemblyWithOldName.runtime.binary,
                ]
            }
        });
    }

    return result;
}

@@public
export function nativeExecutable(args: Arguments): CoreRT.NativeExecutableResult {
    if (Context.getCurrentHost().os !== "macOS") {
        const asm = executable(args);
        return asm.override<CoreRT.NativeExecutableResult>({
            getExecutable: () => asm.runtime.binary
        });
    }

    /** Override framework.applicationDeploymentStyle to make sure we don't use apphost */
    args = args.override<Arguments>({
        framework: (args.framework || Frameworks.framework).override<Shared.Framework>({
            applicationDeploymentStyle: "frameworkDependent"
        })
    });

    /** Compile to MSIL */
    const asm = executable(args);

    /** Compie to native */
    return CoreRT.compileToNative(asm);
}

/**
 * Builds a BuildXL executable project, resulting in an EXE.
 * Does so by invoking `build` specifying `exe` as the target type.
 */
@@public
export function executable(args: Arguments): Managed.Assembly {
    args = processArguments(args, "exe");
    args = args.merge({
        // Add standard assembly binding redirects to all BuildXL binaries
        assemblyBindingRedirects: [
            {
                name: "Newtonsoft.Json",
                publicKeyToken: "30ad4fe6b2a6aeed",
                culture: "neutral",
                oldVersion: "0.0.0.0-11.0.0.0",
                newVersion: "11.0.0.0",
            },
            {
                name: "Microsoft.VstsContentStore",
                publicKeyToken: "1055fbdf2d8b69e0",
                culture: "neutral",
                oldVersion: "0.0.0.0-1.3.0.0",
                newVersion: "1.3.0.0",
            },
            {
                name: "System.Collections.Immutable",
                publicKeyToken: "b03f5f7f11d50a3a",
                culture: "neutral",
                oldVersion: "0.0.0.0-1.5.0.0",
                newVersion: "1.2.3.0",
            },
            {
                name: "Microsoft.ContentStoreInterfaces",
                publicKeyToken: "1055fbdf2d8b69e0",
                culture: "neutral",
                oldVersion: "0.0.0.0-15.1280.0.0",
                newVersion: "1.0.0.0",
            },
            {
                name: "Microsoft.MemoizationStoreInterfaces",
                publicKeyToken: "1055fbdf2d8b69e0",
                culture: "neutral",
                oldVersion: "0.0.0.0-15.1280.0.0",
                newVersion: "1.0.0.0",
            }
        ],
        tools: {
            csc: {
                platform: <"x64">"x64",
                win32Icon: Branding.iconFile
            },
        },
    });

    return Managed.executable(args);
}

@@public
export function assembly(args: Arguments, targetType: Csc.TargetType) : Managed.Assembly {
    args = processArguments(args, targetType);
    return Managed.assembly(args, targetType);
}

/**
 * Builds and runs an xunit test
 */
@@public
export function test(args: TestArguments) : TestResult {
    args = processTestArguments(args);
    let result = Managed.test(args);

    if (!args.skipTestRun) {
        StandaloneTest.deploy(
            result.testDeployment,
            args.testFramework,
            /* deploymentOptions:    */ args.deploymentOptions,
            /* subfolder:            */ args.standaloneTestFolder,
            /* parallelCategories:   */ args.runTestArgs && args.runTestArgs.parallelGroups,
            /* limitCategories:      */ args.runTestArgs && args.runTestArgs.limitGroups,
            /* skipCategories:       */ args.runTestArgs && args.runTestArgs.skipGroups,
            /* untrackTestDirectory: */ args.runTestArgs && args.runTestArgs.untrackTestDirectory);

        if (Flags.buildRequiredAdminPrivilegeTestInVm) {
            // QTest doesn't really work when the limit categories filter out all the tests.
            // Basically, the logic below follows standalone test runner.
            const untrackedFramework = importFrom("Sdk.Managed.Testing.XUnit.UnsafeUnDetoured").framework;
            const trackedFramework = importFrom("Sdk.Managed.Testing.XUnit").framework;
            const untracked = args.testFramework && args.testFramework.name.endsWith(untrackedFramework.name);
            const framework = untracked ? untrackedFramework : trackedFramework;
            args = args.merge({
                testFramework: framework,
                runTestArgs: {
                    privilegeLevel: <"standard"|"admin">"admin",
                    limitGroups: ["RequiresAdmin"],
                    parallelGroups: undefined,
                    tags: ["RequiresAdminTest"],
                }
            });
            const adminResult = Managed.runTestOnly(
                args, 
                /* compileArguments: */ true,
                /* testDeployment:   */ result.testDeployment);
            result = result.override<TestResult>({ adminTestResults: adminResult });
        }
    }

    return result;
}

/**
 * Builds and runs an xunit test
 */
@@public
export function cacheTest(args: TestArguments) : TestResult {
    args = Object.merge<Managed.TestArguments>({
        // Cache tests don't use QTest because QTest doesn't support skipGroups and skipGroups is needed because cache tests fail otherwise.
        testFramework: XUnit.framework,
        runTestArgs: {
            skipGroups: [ "QTestSkip", "Performance", "Simulation", ...(isDotNetCoreBuild ? [ "SkipDotNetCore" ] : []) ],
            tools: {
                exec: {
                    environmentVariables: Environment.hasVariable(envVarNamePrefix + redisConnectionStringEnvVarName) ? [ {name: redisConnectionStringEnvVarName, value: Environment.getStringValue(envVarNamePrefix + redisConnectionStringEnvVarName)}] : []
                }
            }
        },
    }, args);

    return test(args);
}


/**
 * Used in the DScript tests to determine which Xunit to run the test
 */
@@public
export function sdkTest(testFiles:ScriptSdkTestRunner.TestArguments): Managed.TestResult {
    return ScriptSdkTestRunner.test(testFiles);
}

export const assemblyInfo: Managed.AssemblyInfo = {
    productName: Branding.longProductName,
    company: Branding.company,
    copyright: Branding.copyright,
    neutralResourcesLanguage: "en-US",
    version: Branding.Managed.assemblyVersion,
    configuration: qualifier.configuration,
    fileVersion: Branding.Managed.safeFileVersion, // we only rev the fileversion of the main executable to maintain incremental builds.
};

function processArguments(args: Arguments, targetType: Csc.TargetType) : Arguments {
    Contract.requires(
        args !== undefined,
        "BuildXLSdk arguments must not be undefined."
    );

    let framework = Frameworks.framework;
    let assemblyName = args.assemblyName || Context.getLastActiveUseNamespace();
    let title = `${assemblyName}.${targetType === "exe" ? "exe" : "dll"}`;
    let rootNamespace = args.rootNamespace || assemblyName;

    args = Contracts.withRuntimeContracts(args, args.contractsLevel);

    args = Object.merge<Arguments>(
        {
            framework: framework,
            assemblyInfo: Object.merge(assemblyInfo, {title: title}, args.assemblyInfo),
            defineConstants: [
                "DEFTEMP",

                ...addIf(isDotNetCoreBuild,
                    "FEATURE_CORECLR",
                    "FEATURE_SAFE_PROCESS_HANDLE",
                    "DISABLE_FEATURE_VSEXTENSION_INSTALL_CHECK",
                    "DISABLE_FEATURE_HTMLWRITER",
                    "DISABLE_FEATURE_EXTENDED_ENCODING"
                ),
                ...addIf(!Flags.isMicrosoftInternal || isDotNetCoreBuild,
                    "DISABLE_FEATURE_BOND_RPC"
                ),
                ...addIf(isFullFramework,
                    "FEATURE_MICROSOFT_DIAGNOSTICS_TRACING"
                ),
                ...addIf(Flags.isMicrosoftInternal,
                    "FEATURE_ARIA_TELEMETRY"
                ),
                ...addIf(isTargetRuntimeOsx,
                    "PLATFORM_OSX",
                    "FEATURE_THROTTLE_EVAL_SCHEDULER"
                ),
            ],
            references: [
                ...(args.skipDefaultReferences ? [] : [
                    ...(isDotNetCoreBuild ? [] : [
                        NetFx.System.Threading.Tasks.dll,
                        importFrom("Microsoft.Diagnostics.Tracing.EventSource.Redist").pkg,
                        ...(qualifier.targetFramework === "net472")
                            ? [
                                importFrom("System.Threading.Tasks.Dataflow").pkg,
                            ]
                            : [
                                // 472 doesn't need an explicit reference to ValueTuple
                                Managed.Factory.createBinary(importFrom("System.ValueTuple").Contents.all, r`lib/portable-net40+sl4+win8+wp8/System.ValueTuple.dll`),
                                // 472 has its own version of TPL Data flow types
                                importFrom("Microsoft.Tpl.Dataflow").pkg,
                            ],
                    ]),
                    ...(qualifier.targetFramework === "netstandard2.0" ? [] : [
                        importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
                    ]),
                    ...(qualifier.targetFramework !== "net451" ? [] : [
                        importFrom("BuildXL.Utilities").System.FormattableString.dll
                    ]),
                    ...(args.generateLogs ? [
                        importFrom("BuildXL.Utilities.Instrumentation").Tracing.dll,

                        ...addIfLazy(isFullFramework && Flags.isMicrosoftInternal, () =>
                            importFrom("Microsoft.Applications.Telemetry.Desktop").pkg.compile
                        ),
                    ] : []),
                ]),
            ],
            allowUnsafeBlocks: args.allowUnsafeBlocks || args.generateLogs, // When we generate logs we must add /unsafe since we generate unsafe code
            tools: {
                csc: {
                    noWarnings: [1701, 1702],
                    warningLevel: "level 4",
                    subSystemVersion: "6.00",

                    // TODO: Make analyzers supported in regular references by undestanding the structure in nuget packages
                    analyzers: getAnalyzers(args),

                    // Need to turn on an experiemtal feature of the compiler in order to use some of the analyzers.
                    features: args.enableStyleCopAnalyzers ? ['IOperation'] : undefined,
                    codeAnalysisRuleset: args.enableStyleCopAnalyzers ? f`BuildXL.ruleset` : undefined,
                    additionalFiles: args.enableStyleCopAnalyzers ? [f`stylecop.json`] : [],
                    keyFile: args.skipAssemblySigning ? undefined : devKey,
                }
            },
        },
        args);

    // If there are any resX files in the arguments, we want to preprocess them so we can
    // parameterize the product name.
    if (args.embeddedResources) {
        args = args.override({
            embeddedResources: args.embeddedResources.map(resource => {
                if (resource.resX) {
                    return resource.override({
                        resX: ResXPreProcessor
                            .withQualifier(Managed.TargetFrameworks.currentMachineQualifier)
                            .preProcess({
                                resX: resource.resX,
                                defines: brandingDefines,
                            })
                            .resX
                    });
                }

                return resource;
            })
        });
    }

    if (args.generateLogs) {
        let compileClosure = args.generateLogsLite === false
            ? Managed.Helpers.computeCompileClosure(framework, args.references)
            : [
                importFrom("BuildXL.Utilities.Instrumentation").Tracing.dll.compile,
                importFrom("BuildXL.Utilities.Instrumentation").Common.dll.compile,
                ...(isDotNetCoreBuild ? [] : 
                    importFrom("Microsoft.Diagnostics.Tracing.EventSource.Redist").pkg.compile
                ),
                ...Managed.Helpers.computeCompileClosure(framework, framework.standardReferences),
            ];
        
        let sources = args.generateLogsLite === false
            ? args.sources
            : args.sources.filter(f => f.parent.name === a`Tracing`);

        let extraSourceFile = LogGenerator.generate({
            references: compileClosure,
            sources: sources,
            outputFile: "log.g.cs",
            generationNamespace: rootNamespace,
            defines: args.defineConstants,
            aliases: brandingDefines,
            targetFramework: qualifier.targetFramework,
            targetRuntime: qualifier.targetRuntime,
        });
        
        args = args.merge({
            sources: [
                extraSourceFile
            ],
        });
    }
    
    // Handle internalsVisibleTo
    if (args.internalsVisibleTo) {
        const internalsVisibleToFile = Transformer.writeAllLines({
            outputPath: p`${Context.getNewOutputDirectory("internalsvisibleto")}/AssemblyInfo.InternalsVisibleTo.g.cs`,
            lines: [
                "using System.Runtime.CompilerServices;",
                "",
                ...args.internalsVisibleTo.map(assemblyName =>
                    `[assembly: InternalsVisibleTo("${assemblyName}, PublicKey=${publicKey}")]`)
            ]
        });

        args = args.merge({
            sources: [
                internalsVisibleToFile
            ]
        });
    }

    return args;
}

/** Generates a csharp file with an attribute that turns on BuildXL-specific Xunit extension. */
const testFrameworkOverrideAttribute = Transformer.writeAllLines({
    outputPath: Context.getNewOutputDirectory("TestFrameworkOverride").combine("TestFrameworkOverride.g.cs"),
    lines: [
        '[assembly: Test.BuildXL.TestUtilities.XUnit.Extensions.TestFrameworkOverride]'
    ]
});


function processTestArguments(args: Managed.TestArguments) : Managed.TestArguments {
    args = processArguments(args, "library");

    let xunitSemaphoreLimit = Environment.hasVariable(envVarNamePrefix + "xunitSemaphoreCount") ? Environment.getNumberValue(envVarNamePrefix + "xunitSemaphoreCount") : 8;
    let useQTest = Flags.isQTestEnabled && qualifier.targetFramework !== "netcoreapp3.0";
    let testFramework = args.testFramework || (useQTest ? QTest.getFramework(XUnit.framework) : XUnit.framework);

    args = Object.merge<Managed.TestArguments>({
        testFramework: testFramework,
        skipDocumentationGeneration: true,
        sources: [
            ...(qualifier.targetFramework === "net451" ? [] : [
                testFrameworkOverrideAttribute,
            ]),
        ],
        references: [
            ...(qualifier.targetFramework === "net451" ? [] : [
                importFrom("BuildXL.Utilities.UnitTests").TestUtilities.dll,
                importFrom("BuildXL.Utilities.UnitTests").TestUtilities.XUnit.dll,
            ]),
            ...(isDotNetCoreBuild ? [] : [
                importFrom("Microsoft.Diagnostics.Tracing.EventSource.Redist").pkg,
                importFrom("System.Runtime.Serialization.Primitives").pkg,
            ]),
        ],
        skipTestRun: Context.isWindowsOS() && qualifier.targetRuntime === "osx-x64",
        runTestArgs: {
            // TODO: When BuildXL has proper threadsafe logging infrastructure we can go back to the default or parallel.
            parallel: "none",
            tools: {
                acquireSemaphores: [
                    {name: "BuildXL.xunit_semaphore", incrementBy: 1, limit: xunitSemaphoreLimit}
                ],
                exec: {
                    acquireSemaphores: [
                        {name: "BuildXL.xunit_semaphore", incrementBy: 1, limit: xunitSemaphoreLimit}
                    ],
                    errorRegex: " \b",
                }
            }
        },
        runtimeContentToSkip: [
            // Don't deploy the branding manifest for unittest so that updating the version number does not affect the unittests.
            importFrom("BuildXL.Utilities").Branding.brandingManifest,
        ]
    }, args);

    return args;
}
