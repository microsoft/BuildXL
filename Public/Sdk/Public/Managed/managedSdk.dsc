// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as Shared     from "Sdk.Managed.Shared";
import * as Frameworks from "Sdk.Managed.Frameworks";
import * as Csc        from "Sdk.Managed.Tools.Csc";
import * as ResGen     from "Sdk.Managed.Tools.ResGen.Lite";
import * as AppPatcher from "Sdk.Managed.Tools.AppHostPatcher";
import * as Xml        from "Sdk.Xml";
import * as Crossgen   from "Sdk.Managed.Tools.Crossgen";

@@public
export * from "Sdk.Managed.Shared";

export declare const qualifier : Shared.TargetFrameworks.All;

/**
 * The define constants to conditionalize managed code based on target runtime platform to compile
 */
const targetRuntimeDefines = getTargetRuntimeDefines();

@@public
export interface Template {
    managedLibrary?: Arguments;
    managedExecutable?: Arguments;
    managedTest?: TestArguments;
    managedAssembly?: Arguments;
}

/** Builds a managed library. */
@@public
export function library(args: Arguments): Result {
    args = Object.merge(Context.getTemplate<Template>().managedLibrary, args);
    return assembly(args, "library");
}

/** Builds an managed executable. */
@@public
export function executable(args: Arguments): Result {
    args = Object.merge(Context.getTemplate<Template>().managedExecutable, args);
    return assembly(args, "exe");
}

@@public
export function assembly(args: Arguments, targetType: Csc.TargetType) : Result {
    args = Object.merge(Context.getTemplate<Template>().managedAssembly, args);

    // For now we help inferring the framework from qualifier if not passed in. This will cause the downloading all possible frameworks when using this.
    // We'll create a base sdk and one that has all frameworks as default for the hello world convenience case.
    let framework = args.framework || Frameworks.framework;
    if (!framework)
    {
        Contract.fail("You must specify a .NET framework. For exmple: 'importFrom(\"Sdk.Managed.Frameworks.Net472\").framework' ");
    }
    if (framework.targetFramework !== qualifier.targetFramework)
    {
        Contract.fail(`The specified framework does not match the given qualifier. Your project uses targetFramework '${qualifier.targetFramework}' where the specified framework is '${framework.targetFramework}'.`);
    }

    // Check if we need to update or create the App.config file for assembly binding redirects.
    let appConfig = processAppConfigAndBindingRedirects(args, framework);

    args = processDeploymentDefaults(args, targetType, framework, appConfig);

    let name = args.assemblyName || Context.getLastActiveUseNamespace();
    let compileClosure = Helpers.computeCompileClosure(framework, args.references);

    // assemblyinfo
    let assemblyInfo = generateAssemblyInfoFile(args.assemblyInfo, name, framework);

    // Process resources
    let resourceResult = processResources(args, name);


    // Adding helper tags that allow building only a subset of the codebase.
    // For instance, bxl CompileDebugNet472 will only compile all the sources and target net472
    // and bxl CompileWin will compile sources for two key qualifiers for Windows - for net472 and for .net core app.
    let helperTags = [
        ...addIf(qualifier.targetRuntime === "win-x64" && qualifier.targetFramework === "net472" && qualifier.configuration === "debug", "CompileDebugNet472", "CompileWin"),
        ...addIf(qualifier.targetRuntime === "win-x64" && qualifier.targetFramework === "netstandard2.0" && qualifier.configuration === "debug", "CompileNetStandard20", "CompileWin"),
        ...addIf(qualifier.targetRuntime === "win-x64" && qualifier.targetFramework === "netcoreapp3.1" && qualifier.configuration === "debug", "CompileDebugNetCoreWin", "CompileWin"),
        ...addIf(qualifier.targetRuntime === "win-x64" && qualifier.targetFramework === "net5.0" && qualifier.configuration === "debug", "CompileDebugNet5Win", "CompileWin"),
        ...addIf(qualifier.targetRuntime === "win-x64" && qualifier.targetFramework === "net6.0" && qualifier.configuration === "debug", "CompileDebugNet6Win", "CompileWin"),
        
        ...addIf(qualifier.targetRuntime === "osx-x64" && qualifier.targetFramework === "netcoreapp3.1" && qualifier.configuration === "debug", "CompileOsx"),
        ...addIf(qualifier.targetRuntime === "osx-x64" && qualifier.targetFramework === "net5.0" && qualifier.configuration === "debug", "CompileNet5Osx", "CompileOsx"),
        ...addIf(qualifier.targetRuntime === "osx-x64" && qualifier.targetFramework === "net6.0" && qualifier.configuration === "debug", "CompileNet6Osx", "CompileOsx"),
        
        ...addIf(qualifier.targetRuntime === "linux-x64" && qualifier.targetFramework === "netcoreapp3.1" && qualifier.configuration === "debug", "CompileLinux"),
        ...addIf(qualifier.targetRuntime === "linux-x64" && qualifier.targetFramework === "net5.0" && qualifier.configuration === "debug", "CompileNet5Linux", "CompileLinux"),
        ...addIf(qualifier.targetRuntime === "linux-x64" && qualifier.targetFramework === "net6.0" && qualifier.configuration === "debug", "CompileNet6Linux", "CompileLinux")];

    // csc
    let outputFileName = name + targetTypeToFileExtension(targetType, args.deploymentStyle);

    let debugType: Csc.DebugType = args.embedPdbs ? "embedded" : (framework.requiresPortablePdb ? "portable" : "full");
    let cscArgs : Csc.Arguments = {
        sources: [
            assemblyInfo,
            ...resourceResult.sources,
            ...(args.sources || []),
        ],
        sourceFolders: args.sourceFolders,
        references: compileClosure,
        targetType: targetType,
        linkResources: resourceResult.linkResources,
        resourceFiles: resourceResult.resourceFiles,
        treatWarningsAsErrors: qualifier.configuration === "release",
        optimize: qualifier.configuration === "release",
        checked: true,
        doc: args.skipDocumentationGeneration === true ? undefined : name + ".xml",
        out: outputFileName,
        pdb: name + ".pdb",
        debugType: debugType,
        allowUnsafeBlocks: args.allowUnsafeBlocks || false,
        appConfig: appConfig,
        implicitSources: args.implicitSources,
        noConfig: args.noConfig || true,
        defines: [
            ...(qualifier.configuration === "debug" ? ["DEBUG"] : []),
            "TRACE",
            ...framework.conditionalCompileDefines,
            ...targetRuntimeDefines,
            ...(args.defineConstants || []),
            ...(Environment.getFlag("[Sdk.BuildXL]microsoftInternal") ? ["MICROSOFT_INTERNAL"] : [])
        ],
        nullable: args.nullable,
        nullabilityContext: args.nullabilityContext,
        tags: helperTags
    };

    const references = [
        ...(args.references || []),
        ...(args.runtimeReferences || []),
        ...framework.standardReferences,
    ];

    if (args.tools && args.tools.csc) {
        cscArgs = Object.merge(args.tools.csc, cscArgs);
    }

    let cscResult =  Csc.compile(cscArgs);

    // Run crossgen if specified and the framework/deployment style allows for it.
    // An additional condition is that cross-targeting is not supported by ReadyToRun, so we can only compile on the given target, 
    // see https://docs.microsoft.com/en-us/dotnet/core/whats-new/dotnet-core-3-0#cross-platformarchitecture-restrictions
    if (args.runCrossgenIfSupported && 
        Shared.supportsCrossgen(args.deploymentStyle, framework) && 
        qualifier.targetRuntime === Shared.TargetFrameworks.MachineQualifier.current.targetRuntime) {
        
        // crossgen needs the runtime assemblies, not the compile ones
        const referenceClosure = Helpers.computeTransitiveClosure(args.references, args.runtimeContentToSkip, /*compile*/ false);

        const nativeImage = Crossgen.crossgen({
            inputBinary: cscResult.binary,
            references: referenceClosure,
            targetRuntime: qualifier.targetRuntime,
            targetFramework: framework,
        });

        // Replace the binary with the native image. The reference assembly is still the original one
        cscResult = {binary: nativeImage, reference: cscResult.reference};
    }

    let runtimeConfigFiles = undefined;
    const compileBinary = cscResult.reference || cscResult.binary;
    const runtimeBinary = cscResult.binary;

    if (args.deployRuntimeConfigFile)
    {
        runtimeConfigFiles = RuntimeConfigFiles.createFiles(framework, args.deploymentStyle, name, runtimeBinary.binary.name, references, args.runtimeContentToSkip, appConfig);
    }

    let deploymentResult = processDeploymentStyle(args, targetType, framework, cscResult);

    // TODO: Add version
    return {
        name: a`${name}`,
        targetFramework: framework.targetFramework,
        targetRuntime: qualifier.targetRuntime,
        compile: compileBinary,
        runtime: runtimeBinary,
        references: references,
        runtimeConfigFiles: runtimeConfigFiles,
        runtimeContent: deploymentResult.runtimeContent,
        runtimeContentToSkip: args.runtimeContentToSkip,
        deploy: deploymentResult.deployFunction,
    };
}

function processResources(args: Arguments, name: string) : { sources: File[], linkResources: Shared.LinkResource[], resourceFiles: File[] }
{
    let rootNamespace = args.rootNamespace || name;
    let sources : File[] = [];
    let resources : Shared.LinkResource[] = args.resources || [];
    if (args.embeddedResources) {
        for (let resource of args.embeddedResources) {
            if (resource.resX) {
                /**
                 * The strongly typed class description to use for the generated source file.
                 * Unlike the C# counterpart, this field becomes required. Otherwise we have to generate strings representing
                 * namespace and classname from file name, and that should be forbidden due to case sensitivity.
                 */

                const generatedClass : ResGen.StronglyTypedResourceClass = resource.generatedClassMode === "explicit" ? undefined : {
                    language: "cs",
                    className: resource.resX.nameWithoutExtension.toString(),
                    nameSpace: rootNamespace,
                    fileName: `${resource.resX.nameWithoutExtension}.g.cs`,
                };

                const logicalName = rootNamespace + "." + resource.resX.nameWithoutExtension + a`.resources`;
                let result = ResGen.generate({
                    inputFile: resource.resX,
                    logicalName: logicalName,
                    isPublic: resource.generatedClassMode === "implicitPublic",
                    publicClass: resource.generatedClassMode === "implicitPublic",
                    generatedClass: generatedClass
                });

                resources = resources.push(result.resourceFile);
                if (result.sourceFile) {
                    sources = sources.push(result.sourceFile);
                }
            }
            else if (resource.linkedContent)
            {
                for (let resourceFile of resource.linkedContent)
                {
                    const logicalName = computeNamespace(resource.logicalName, rootNamespace, resourceFile) + "." + resourceFile.name;

                    resources = resources.push({
                        file: resourceFile,
                        logicalName: logicalName,
                        isPublic: false
                    });
                }
            }
        }
    }

    return {
        linkResources: resources.filter(r => r.file !== undefined && r.logicalName !== undefined),
        resourceFiles: resources.filter(r => r.file !== undefined && r.logicalName === undefined).map(r => r.file),
        sources: sources,
    };
}

/**
 * Checks the type of application an sets the deployment option defaults for that
 * type. We pass the default as the first argument to merge so applications can always override.
 */
function processDeploymentDefaults(args: Arguments, targetType: Csc.TargetType, framework: Shared.Framework, appConfig: File) : Arguments
{
    switch (targetType)
    {
        case "exe":
            // For executables we set the default deployment options.cscResult
            return Object.merge<Arguments>(
                {
                    deploymentStyle: framework.defaultApplicationDeploymentStyle,
                    deployRuntimeConfigFile: true,
                },
                args
            );
        case "library":
            // For libraries we deploy the runtime config file if we have an explicit appconfig.
            args = Object.merge<Arguments>(
                {
                    deployRuntimeConfigFile: appConfig !== undefined,
                },
                args
            );
        default:
            return args;

    }
}

function processAppConfigAndBindingRedirects(args: Arguments, framework: Shared.Framework) : File
{
    let appConfig = args.appConfig;
    if (args.assemblyBindingRedirects) {

        let bindigRedirectElements = args.assemblyBindingRedirects.map(bindingRedirect =>
            Xml.elem("dependentAssembly",
                Xml.elem("assemblyIdentity",
                    Xml.attr("name", bindingRedirect.name),
                    bindingRedirect.publicKeyToken && Xml.attr("publicKeyToken", bindingRedirect.publicKeyToken),
                    bindingRedirect.culture && Xml.attr("culture", bindingRedirect.culture)
                ),
                Xml.elem("bindingRedirect",
                    Xml.attr("oldVersion", bindingRedirect.oldVersion),
                    Xml.attr("newVersion", bindingRedirect.newVersion)
                )
            ));

        // TODO: Figure out how to fail with good error if appConfig is not a sourcefile.
        let configuration = appConfig
            ? Xml.read(f`${appConfig}`).nodes.filter(n => Xml.isElement(n) && Xml.nameEquals(n.name, "configuration"))[0] as Xml.Element
            : Xml.elem("configuration",
                Xml.elem("startup",
                    Xml.elem("supportedRuntime",
                    Xml.attr("version", framework.supportedRuntimeVersion),
                    Xml.attr("sku", framework.assemblyInfoTargetFramework)
                    )
                )
              );

        let patchedConfiguration = Xml.updateOrAddChildElement(
            configuration,
            "runtime",
            runtime =>  Xml.updateOrAddChildElement(
                runtime,
                Xml.name("assemblyBinding", "urn:schemas-microsoft-com:asm.v1"),
                assemblyBinding =>
                    Xml.addNodes(assemblyBinding, bindigRedirectElements)
            )
        );

        let updatedAppConfigPath = p`${Context.getNewOutputDirectory("assemblyBindingRedirects")}/App.config`;
        appConfig = Xml.write(updatedAppConfigPath, Xml.doc(patchedConfiguration));
    }

    return appConfig;
}

function processDeploymentStyle(args: Arguments, targetType: Csc.TargetType, framework: Shared.Framework, cscResult: Csc.Result) : {
    deployFunction: Deployment.FlattenForDeploymentFunction,
    runtimeContent: Deployment.Definition
}
{
    let deployFunction : Deployment.FlattenForDeploymentFunction = Shared.Deployment.flattenAssembly;
    let runtimeContent = args.runtimeContent;

    if (args.deploymentStyle === "selfContained")
    {
        const frameworkRuntimeFilenameOverrides = Set.create<PathAtom>(...(args.deploymentOptions && args.deploymentOptions.ignoredSelfContainedRuntimeFilenames || []));
        let frameworkRuntimeFiles = framework.runtimeContentProvider(qualifier.targetRuntime);

        // Remove from the framework runtime files the ones that are explicitly flagged to be ignored
        // In this way assemblies in the ignore list which are explicitly provided will take precedence over the
        // the framework runtime ones
        if (frameworkRuntimeFilenameOverrides.count() > 0) {
            frameworkRuntimeFiles = frameworkRuntimeFiles.filter(file => !frameworkRuntimeFilenameOverrides.contains(file.name));
        }

        const frameworkRuntimeFileSet = Set.create<File>(...frameworkRuntimeFiles);

        const patchResult = AppPatcher.withQualifier(Shared.TargetFrameworks.MachineQualifier.current).patchBinary({
            binary: cscResult.binary.binary,
            // Workaround an evaluation issue that is happening for mac builds when qualifier.targetRuntime is passed.
            targetRuntimeVersion: Context.getCurrentHost().os === "win" ? qualifier.targetRuntime : Shared.TargetFrameworks.MachineQualifier.current.targetRuntime
        });

        runtimeContent = [
            ...(runtimeContent || []),
            // Self-Contained .NET Core deployments need a runtime and a patched application host container to be able to run on the target OS
            ...frameworkRuntimeFiles,
            ...patchResult.contents,
        ];

        // When deploying self-contained dotNetCore executables we prefer to deploy the binaries that come with
        // the runtime over the ones that come from nuget. We do so by providing a deploy function that customizes
        // the handleDuplicate function to prefer the runtime file.
        deployFunction = (
            assembly: Shared.Assembly,
            targetFolder: RelativePath,
            handleDuplicate: Deployment.HandleDuplicateFileDeployment,
            currentResult: Deployment.FlattenedResult,
            deploymentOptions?: Object,
            provenance?: Deployment.Diagnostics.Provenance): Deployment.FlattenedResult => {

            const customHandleDuplicate : Deployment.HandleDuplicateFileDeployment = (targetFile: RelativePath, sourceA: Deployment.DeployedFileWithProvenance, sourceB: Deployment.DeployedFileWithProvenance, message?: string) : Deployment.DeployedFileAction => {
                if (frameworkRuntimeFileSet.contains(sourceA.file)) {
                    return "takeA";
                }

                if (frameworkRuntimeFileSet.contains(sourceB.file)) {
                    return "takeB";
                }

                return handleDuplicate(targetFile, sourceA, sourceB, message);
            };

            return Shared.Deployment.flattenAssembly(assembly, targetFolder, customHandleDuplicate, currentResult, deploymentOptions, provenance);
        };
    }

    return {
        deployFunction: deployFunction,
        runtimeContent: runtimeContent ? { contents: runtimeContent } : undefined,
    };
}

@@public
export interface Result extends Shared.Assembly {
}

@@public
export interface Arguments {
    /** Assembly name. If unspecified, the last used variable name in the context is the name. */
    assemblyName?: string;

    /** The root namespace, the assemblyname will be used if not specified. */
    rootNamespace?: string;

    /** The dotnet framework to compile for */
    framework?: Shared.Framework;

    /** Sources. */
    sources?: File[];

    /** Source Folders */
    sourceFolders?: StaticDirectory[];

    /** References. */
    references?: Shared.Reference[];

    /** References not used at compile-time but deployed alongside assembly and include in runtime dependencies */
    runtimeReferences?: Shared.Reference[];

    /** Defined constants. */
    defineConstants?: string[];

    /** Embedded resource to include in the assembly, specified as .resx files paired with optional linked content. */
    embeddedResources?: EmbeddedResource[];

    /** Resources to include in the assembly. */
    resources?: Shared.LinkResource[];

    /** Implicit sources. */
    implicitSources?: File[];

    /** Whether unsafe blocks are allowed. */
    allowUnsafeBlocks?: boolean;

    /** Whether this transformer is allowed to skip document generation. */
    skipDocumentationGeneration?: boolean;

    /** AppConfig file. */
    appConfig?: File;

    /** Platform to build. */
    platform?: Csc.Platform;

    /** Specify nullable context option enable|disable. */
    nullable?: boolean;

    /**
     * Whether to embed pdbs into the assemblies or not.
     * True by default until the project opts out this feature.
     */
    embedPdbs?: boolean;

    /**
     * Whether to embed sources into pdbs.
     */
    embedSources?: boolean;
    
    /*
     * If false then the file with non-nullable attributes won't be added to the project even when nullable flag is set to true.
     * 
     * For non-dotnet-core project specifying nullable flag forces the SDK to automatically include a special file
     * with non-nullable attributes like [MaybeNull], [NonNullWhen] etc.
     * 
     * This is a good default behavior but it causes compilation warnings in the following case:
     * Project A specifies nullable flag and has internals visibility with Project B.
     * It means that the Project B can access all the internal members in Project A including all the non-nullability attributes.
     * Adding the same set of attributes in Project B will cause compilation warnings and to avoid them 
     * Project B needs to specify addNotNullAttributeFile: false.
     * 
     * The default is true if 'nullable' is true.
     */
    addNotNullAttributeFile?: boolean;

    /** Specify nullable context option enable|disable|safeonly|warnings|safeonlywarnings.*/
    nullabilityContext?: Csc.NullabilityContext;

    noConfig?: boolean;

    /** Extra content/files to be deployed with the assembly when running. i.e. native dlls that are p-invoked, config files etc. */
    runtimeContent?: Deployment.DeployableItem[];

    /**
     * List of deployable items to skip when deploying the dependencies of this assembly.
     * This is useful for when you take a dependency on an assembly or a package but it comes with files or nuget packages
     * that conflict with other dependencies.
     */
    runtimeContentToSkip?: Deployment.DeployableItem[];

    /** Optional assembly information */
    assemblyInfo?: AssemblyInfo;

    /** Optional set of assembly binding redirects. If there is an existing App.config file, it will be merged with it, else when these are present one will be emitted. */
    assemblyBindingRedirects?: AssemblyBindingRedirect[];

    /** Whether to create a runtime config file like xxxx.deps.json, runtimesettings.json, xxx.exe.config, etc */
    deployRuntimeConfigFile?: boolean;

    /** How to deploy this application. This only applies to dotnet core deployments */
    deploymentStyle?: Shared.ApplicationDeploymentStyle;

    /** Settings for nested tools */
    tools?: {
        /** Csc default args. */
        csc?: Csc.Arguments;

        /** ResGen default args */
        resgen?: ResGen.Arguments;
    };

    /** Options that control how this compiled assembly gets deployed */
    deploymentOptions?: Deployment.DeploymentOptions;

    /** Whether to run crossgen tool on the produced assembly (if the target framework allows for it) */
    runCrossgenIfSupported?: boolean;
}

@@public
export interface EmbeddedResource {
    /** The .resx file containing the definitions of resources to embed, such as strings and links to files. */
    resX?: File;

    /** Content files referenced by the resX's. */
    linkedContent?: File[];

    /** The logical name of the embedded resource. */
    logicalName?: string;

    /** Specifies how strongly typed resource classes are generated for embedded resources. */
    generatedClassMode?: StronglyTypedResourceClassMode;
}

@@public
export type StronglyTypedResourceClassMode =
    /** No strongly typed resource class is generated unless explicitly specified. */
    "explicit" |

    /** Automatically generate an internal class. */
    "implicitInternal" |

    /** Automatically generate a public class. */
    "implicitPublic"
;

/**
 * Information to generate assembly binding redirects for desktop CLR
 * See https://docs.microsoft.com/en-us/dotnet/framework/configure-apps/redirect-assembly-versions for details
 */
@@public
export interface AssemblyBindingRedirect {
    /**
     * The name of the assembly
     */
    name: string,

    /**
     * The public key if the assembly to redirect is signed
     */
    publicKeyToken?: string,

    /**
     * The culture of the assembly to redirect. Typically this is 'neutral'.
     */
    culture?: string,

    /**
     * The old version. This can be a range i.e. '0.0.0.0-1.3.0.0'
     */
    oldVersion: string,

    /**
     * The new version. i.e. '1.3.0.0'
     */
    newVersion: string,
}


function targetTypeToFileExtension(targetType: Csc.TargetType, deploymentStyle: Shared.ApplicationDeploymentStyle): string {
    switch (targetType) {
        case "exe":
        case "winexe":
        case "appcontainerexe":
            return deploymentStyle === "selfContained" ? ".dll" : ".exe";
        case "library":
            return ".dll";
        case "winmdobj":
        case "module":
        default:
            return "." + targetType.toString();
    }
}

function computeNamespace(logicalName: string, rootNamespace: string, resourcePath: File) : string {
    if (logicalName !== undefined && logicalName !== "") {
        return concatNamespaceFragments(rootNamespace, logicalName);
    }
    else {
        let projectRoot = Context.getLastActiveUsePath().parent;
        if (resourcePath.isWithin(projectRoot)) {
            return concatFragments(projectRoot.path, resourcePath.parent.path, rootNamespace);
        }
        else {
            // otherwise, just use rootNamespace
            return rootNamespace;
        }
    }
}

function concatFragments(parentPath: Path, path: Path, resultSoFar: string): string {
    if (path === parentPath) {
        return resultSoFar;
    }

    return concatNamespaceFragments(
        concatFragments(parentPath, path.parent.path, resultSoFar),
        path.name.toString()
    );
}

/**
 * Safe namespace concatenation: if either is undefined ==> use the other one; else join them with ".".
 */
function concatNamespaceFragments(parent: string, child: string): string {
    if (parent === undefined || parent === "") return child;
    if (child === undefined || child === "") return parent;
    return parent + "." + child;
}

/**
 * The define constants to conditionalize managed code based on target runtime platform to compile
 */
function getTargetRuntimeDefines() : string[] {
    switch (qualifier.targetRuntime)
    {
        case "win-x64":
            return ["PLATFORM_WIN", "PLATFORM_X64"];
        case "osx-x64":
            return ["PLATFORM_OSX", "PLATFORM_X64"];
        case "linux-x64":
            return ["PLATFORM_LINUX", "PLATFORM_X64"];
        default:
            Contract.fail("Unexpected targetRuntime");
    }
}
