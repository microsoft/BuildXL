// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as Shared     from "Sdk.Managed.Shared";
import * as Frameworks from "Sdk.Managed.Frameworks";
import * as Csc        from "Sdk.Managed.Tools.Csc";
import * as Ilc        from "Sdk.Managed.Tools.ILCompiler";
import * as ResGen     from "Sdk.Managed.Tools.ResGen.Lite";
import * as AppPatcher from "Sdk.Managed.Tools.AppHostPatcher";
import * as Xml        from "Sdk.Xml";

@@public
export * from "Sdk.Managed.Shared";

export declare const qualifier : Shared.TargetFrameworks.All;

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
        Contract.fail("You must specify a .NET framework. For exmple: 'importFrom(\"Sdk.Managed.Frameworks.Net451\").framework' ");
    }
    if (framework.targetFramework !== qualifier.targetFramework)
    {
        Contract.fail(`The specified framework does not match the given qualifier. Your project uses targetFramework '${qualifier.targetFramework}' where the specified framework is '${framework.targetFramework}'.`);
    }

    let name = args.assemblyName || Context.getLastActiveUseNamespace();
    let rootNamespace = args.rootNamespace || name;
    let compileClosure = Helpers.computeCompileClosure(framework, args.references);

    // assemblyinfo
    let assemblyInfo = generateAssemblyInfoFile(framework, name, args.assemblyInfo);

    // Process resources
    let resourceSources : File[] = [];
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
                    resourceSources = resourceSources.push(result.sourceFile);
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

    // Check if we need to update or create the App.Config file for assembly binding redirects.
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

        let updatedAppConfigPath = p`${Context.getNewOutputDirectory("assemblyBindingRedirects")}/app.config`;
        appConfig = Xml.write(updatedAppConfigPath, Xml.doc(patchedConfiguration));
    }

    // csc
    let outputFileName = name + targetTypeToFileExtension(targetType, framework.applicationDeploymentStyle);
    let cscArgs : Csc.Arguments = {
        sources: [
            assemblyInfo,
            ...resourceSources,
            ...(args.sources || []),
        ],
        sourceFolders: args.sourceFolders,
        references: compileClosure,
        targetType: targetType,
        linkResources: resources.filter(r => r.file !== undefined && r.logicalName !== undefined),
        resourceFiles: resources.filter(r => r.file !== undefined && r.logicalName === undefined).map(r => r.file),
        treatWarningsAsErrors: qualifier.configuration === "release",
        optimize: qualifier.configuration === "release",
        checked: true,
        doc: args.skipDocumentationGeneration === true ? undefined : name + ".xml",
        out: outputFileName,
        pdb: name + ".pdb",
        debugType: framework.requiresPortablePdb ? "portable" : "full",
        allowUnsafeBlocks: args.allowUnsafeBlocks || false,
        appConfig: appConfig,
        implicitSources: args.implicitSources,
        noConfig: args.noConfig || true,
        defines: [
            ...(qualifier.configuration === "debug" ? ["DEBUG"] : []),
            "TRACE",
            ...(args.defineConstants || []),
            // Defining a special symbol that can be used in C# code for using new API available in .NET 4.6.1+
            ...(qualifier.targetFramework !== "net451" ? ["NET461Plus"] : []),
        ]
    };

    const references = [
        ...(args.references || []),
        ...framework.standardReferences,
    ];

    if (args.tools && args.tools.csc) {
        cscArgs = Object.merge(args.tools.csc, cscArgs);
    }

    cscArgs = Object.merge(Helpers.patchReferencesForSystemInteractiveAsync(references), cscArgs);

    let cscResult =  Csc.compile(cscArgs);

    let runtimeConfigFiles = undefined;
    let runtimeContent = args.runtimeContent;

    let deployFunction : Deployment.FlattenForDeploymentFunction = Shared.Deployment.flattenAssembly;

    const compileBinary = cscResult.reference || cscResult.binary;
    const runtimeBinary = cscResult.binary;

    if (targetType === "exe")
    {
        runtimeConfigFiles = RuntimeConfigFiles.createFiles(framework, name, runtimeBinary, references, appConfig);
        if (framework.applicationDeploymentStyle === "selfContained")
        {
            const frameworkRuntimeFiles = framework.runtimeContentProvider(qualifier.targetRuntime);
            const frameworkRuntimeFileSet = Set.create<File>(...frameworkRuntimeFiles);

            const patchResult = AppPatcher.withQualifier(Shared.TargetFrameworks.currentMachineQualifier).patchBinary({
                binary: cscResult.binary.binary,
                targetRuntimeVersion: qualifier.targetRuntime
            });

            runtimeContent = [
                ...(runtimeContent || []),
                // Self-Contained .NET Core deployments need a runtime and a patched application host container to be able to run on the target OS
                ...frameworkRuntimeFiles,
                patchResult.binary,
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
    }
    else if (targetType === "library")
    {
        runtimeConfigFiles = RuntimeConfigFiles.createDllAppConfig(framework, name, appConfig);
    }

    // TODO: Add version
    return {
        name: a`${name}`,
        targetFramework: framework.targetFramework,
        compile: compileBinary,
        runtime: runtimeBinary,
        references: references,
        runtimeConfigFiles: runtimeConfigFiles,
        runtimeContent: runtimeContent ? { contents: runtimeContent } : undefined,
        runtimeContentToSkip: args.runtimeContentToSkip,
        deploy: deployFunction,
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

    noConfig?: boolean;

    /** Extra content/files to be deployed with the assembly when running. i.e. native dlls that are p-invoked, config files etc. */
    runtimeContent?: Deployment.DeployableItem[];

    /**
     * List of deployable items to skip when deploying the dependencies of this assembly.
     * This is usefull for when you take a dependency on an assembly or a package but it comes with files or nuget packages
     * that conflict with other dependencies.
     */
    runtimeContentToSkip?: Deployment.DeployableItem[];

    /** Optional assembly information */
    assemblyInfo?: AssemblyInfo;

    /** Optional set of assembly binding redirects. If there is an existing app.config file, it will be merged with it, else when these are present one will be emitted. */
    assemblyBindingRedirects?: AssemblyBindingRedirect[],

    /** Settings for nested tools */
    tools?: {
        /** Csc default args. */
        csc?: Csc.Arguments;

        /** ResGen default args */
        resgen?: ResGen.Arguments;

        /** Ilc default args */
        ilc?: Ilc.Arguments;
    };

    /** Options that control how this compiled assembly gets deployed */
    deploymentOptions?: Deployment.DeploymentOptions
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
