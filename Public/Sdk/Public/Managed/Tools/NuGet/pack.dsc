// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

import * as Deployment from "Sdk.Deployment";
import * as Managed    from "Sdk.Managed.Shared";
import * as Mono       from "Sdk.Mono";
import * as Nuget      from "NuGet.CommandLine";
import * as Xml        from "Sdk.Xml";

@@public
export const tool : Transformer.ToolDefinition = {
    exe: Nuget.Contents.all.getFile(r`tools/NuGet.exe`),
    description: "NuGet pack",
    untrackedDirectoryScopes: [
        d`${Context.getMount("ProgramData").path}/Nuget`,
        ...addIfLazy(Context.isWindowsOS(), () => [d`${Context.getMount("ProgramFilesX86").path}/Nuget`]),
    ],
    dependsOnWindowsDirectories: true,
    dependsOnAppDataDirectory: true,
    prepareTempDirectory: true,
    runtimeDependencies: [
        ...addIfLazy(Context.isWindowsOS(), () => [Nuget.Contents.all.getFile(r`tools/Nuget.pdb`)]),
    ],
};

@@public
export interface Arguments extends Transformer.RunnerArguments {
    /** Deployed files. */
    deployment?: Deployment.Definition;

    /** File names to exclude from the NuGet package */
    filterFiles?: PathAtom[];

    /** Options to use during the deployment. i.e. to not deploy the pdb or xml docs */
    deploymentOptions?: Managed.Deployment.FlattenOptions,

    /** The metadata for the package we generate */
    metadata: PackageMetadata;

    /** Set the minClientVersion attribute for the created package. */
    minClientVersion?: string;

    /** Prevent default exclusion of NuGet package files and files and folders starting with a dot e.g. .svn. */
    noDefaultExcludes?: boolean;

    /** Specify if the command should not run package analysis after building the package. */
    noPackageAnalysis?: boolean;
}

/**
 * Nuget package metadata.
 */
@@public
export interface PackageMetadata {
    /** Id to be specified in metadata. */
    id: string;

    /** Authors to be specified in metadata. */
    authors: string;

    /** Descriptions to be specified in metadata. */
    description?: string;

    /** Owners to be specified in metadata. */
    owners?: string;

    /** Boolean flag indicating required license acceptance to be specified in metadata. */
    requireLicenseAcceptance?: boolean;

    /** License URL to be specified in metadata. */
    licenseUrl?: string;

    /** Project URL to be specified in metadata. */
    projectUrl?: string;

    /** Summary to be specified in metadata. */
    summary?: string;

    /** Title to be specified in metadata. */
    title?: string;

    /** Version to be specified in metadata. */
    version: string;

    /** Copyright to be specified in metadata. */
    copyright?: string;

    /** Tags to be specified in metadata. */
    tags?: string;

    /** Dependencies to be specified in metadata. */
    dependencies?: Dependency[];

    /** ContentFile patterns */
    contentFiles?: ContentFile[];
}

@@public
export interface Dependency {
    /** Name of the dependency. */
    id: string;

    /** Version number of the dependency. */
    version: string;

    /** Target framework */
    targetFramework?: string;
}

@@public
export interface PackageIdentifier {
    /** Name of the Package. */
    id: string,

    /** Version of the Package. */
    version: string,
}

@@public
export interface PackageSpecification {
    /** Identifier of the Package. */
    id: PackageIdentifier,

    /** Assemblies included in this package */
    assemblies: Managed.Assembly[],

    /** Dependencies for this package */
    dependencies?: Dependency[],

    /** Deployment options */
    deploymentOptions?: Managed.Deployment.FlattenOptions,

    /** Additional runtime content to include */
    additionalContent?: Deployment.DeployableItem[],
}

export interface ContentFile {
    /** The location of the file or files to include, subject to exclusions specified by the exclude attribute. The path is relative to the .nuspec file unless an absolute path is specified. The wildcard character * is allowed, and the double wildcard ** implies a recursive folder search. */
    include: string,

    /** A semicolon-delimited list of files or file patterns to exclude from the src location. The wildcard character * is allowed, and the double wildcard ** implies a recursive folder search. */
    exclude?: string,

    /** The build action to assign to the content item for MSBuild, such as Content, None, Embedded Resource, Compile, etc. The default is Compile. */
    buildAction?: "Content" | "None" | "Embedded Resource" | "Compile",

    /** A Boolean indicating whether to copy content items to the build (or publish) output folder. The default is false. */
    copyToOutput?: boolean,

    /** A Boolean indicating whether to copy content items to a single folder in the build output (true), or to preserve the folder structure in the package (false). This flag only works when copyToOutput flag is set to true. The default is false. */
    flatten?: boolean,
}

@@public
export interface PackResult {
    nuPkg: File;
}

@@public
export interface PackageBranding
{
    /** The company name to be used in the package metadata. */
    company: string;

    /** The short product name to be used in the package metadata. */
    shortProductName: string;

    /** The package version to be used in the package metadata. */
    version: string;

    /** The package authors to be used in the package metadata. */
    authors: string;

    /** The package owners to be used in the package metadata. */
    owners: string;

    /** The package copy right to be used in the package metadata. */
    copyright: string;
}

/**
 * Helper to pack assemblies and assert that only the specified BuildXL package references are added as dependencies.
 */
@@public
export function packAssembliesAndAssertDependencies(
    args: PackageSpecification,
    packageSpecifications : PackageSpecification[],
    packageBranding : PackageBranding,
    inferInternalDependencies : boolean,
    allowedDependecies : PackageIdentifier[] ) : File
{
    assertAssemblyPrefix(args);

    let dependencies = getDependencies(args, packageSpecifications, inferInternalDependencies);
    let disallowedDependencies = dependencies
        .filter(dep => dep.id.startsWith("BuildXL"))
        .filter(dep => allowedDependecies.find(allowedDependency => allowedDependency.id === dep.id && allowedDependency.version === dep.version) === undefined);

    if (disallowedDependencies.length > 0) {
        Contract.fail(`Nuget Package '${args.id.id}.${args.id.version}' references the following disallowed dependencies: ${Environment.newLine()}${disallowedDependencies.map(dep => `name: '${dep.id}', targetFramework: '${dep.targetFramework}'`).join(Environment.newLine())}`);    
    }

    return buildNupkg(args, dependencies, packageBranding);
}

/**
 * Helper to pack assemblies. 
 * When 'inferInternalDependencies' is true, BuildXL package references will be automatically added and errors will be thrown if a reference cannot be found.
 */
@@public
export function packAssemblies(
    args: PackageSpecification,
    packageSpecifications : PackageSpecification[],
    packageBranding : PackageBranding,
    inferInternalDependencies : boolean) : File
{
    assertAssemblyPrefix(args);
    let dependencies = getDependencies(args, packageSpecifications, inferInternalDependencies);
    return buildNupkg(args, dependencies, packageBranding);
}

/**
 * Gets a list of dependencies for the the assemblies in this package.
 */
function getDependencies(args: PackageSpecification,
    packageSpecifications : PackageSpecification[],
    inferInternalDependencies : boolean) : Dependency[]
{
    let dependencies : Dependency[] = args
        .assemblies
        .filter(asm => asm !== undefined)
        .mapMany(asm => asm
            .references
            .filter(ref => Managed.isManagedPackage(ref))
            .map(ref => <Managed.ManagedNugetPackage>ref)
            .map(ref => { return {id: ref.name, version: ref.version, targetFramework: asm.targetFramework}; })
            .concat( (args.dependencies || []).map(dep => { return {id: dep.id, version: dep.version, targetFramework: asm.targetFramework }; }) )
        );

    // This step will gather all of the assembly references made by each assembly inside the nuget package with the prefix "BuildXL"
    // and will search our package specifications for a matching assembly. If one is found, it will be added as a dependency.
    // If one is not found, an error will be thrown containing a list of assemblies that are not already in an existing BuildXL package.
    // To fix this, a new package must be produced for the missing binaries.
    // See 'Public/Src/Deployment/NugetPackages.dsc' for instructions on how to add a new package.
    if (inferInternalDependencies) {
        // Gather all the dependencies for each assembly
        let references = args.assemblies
            .filter(asm => asm !== undefined)
            .mapMany(asm => asm.references);

        let assemblyDependencies = references
            .filter(ref => Managed.isAssembly(ref))
            .map(ref => <Managed.Assembly>ref)
            .filter(ref => ref.name.toString().startsWith("BuildXL")); // Filters out non-BuildXL dependencies (e.g. System.*)

        // Verify that all assembly dependencies are declared as dependencies
        let missingDependencies : MutableSet<String> = MutableSet.empty<String>();

        for (let dep of assemblyDependencies) {
            let maybeDependency = packageSpecifications.find(
                spec => spec
                    .assemblies
                    .find(asm => asm.name === dep.name && asm.targetFramework === dep.targetFramework) !== undefined
            );

            if (maybeDependency === undefined) {
                missingDependencies.add(`name: '${dep.name}', targetFramework: '${dep.targetFramework}'`);
            }
            else {
                dependencies = dependencies.push({
                    id: maybeDependency.id.id,
                    version: maybeDependency.id.version,
                    targetFramework: dep.targetFramework
                });
            }
        }

        if (missingDependencies.count() > 0) {
            Contract.fail(`Nuget Package '${args.id.id}.${args.id.version}' references the following assemblies, but a corresponding Nuget package could not be found to be declared as a dependency: ${Environment.newLine()}${missingDependencies.toArray().join(Environment.newLine())}`);
        }
    }

    return dependencies;
}

/**
 * Builds a nupkg given a package specification and its dependencies.
 */
function buildNupkg(args: PackageSpecification, dependencies : Dependency[], packageBranding : PackageBranding) : File {
    // If we ever add support for Mac packages here, we will have a problem because nuget does not
    // support our scenario as of Jan 2020.
    //  * We can't use contentFiles/any/{tfm} pattern because it doesn't support {rid}
    //  * We can't place stuff in runtimes/{rid}/lib/{tfm}/xxxx nor in runtimes/{rid}/native/xxxx beause:
    //        a) packages.config projects don't support the runtimes folder
    //        b) nuget does not copy files on build. So F5 and unittests are broken. One has to hit 'publish'
    //        c) nuget does not copy subfolders under those
    // So the only solution is to include a custom targets file, which is hard to write because now that
    // targets file is responsible for doing the {rid} graph resolution between win10-x64, win10, win-x64 etc.
    // Therefore we will stick to only supporting windows platform and using contentFiles pattern
    let contentFiles : Deployment.DeployableItem[] = args
        .assemblies
        .filter(asm => asm !== undefined && asm.runtimeContent !== undefined)
        .map(asm => <Deployment.NestedDefinition>{
            // Note since all windows tfms have the same content, we are manually
            // if we ever create differences between tmfs, we will have to change the second 
            // any to ${asm.targetFramework}
            subfolder: r`contentFiles/any/any`,
            contents: [
                asm.runtimeContent
            ]
        });

    return pack({
        metadata:  createMetaData({
            id: args.id.id, 
            dependencies: dependencies, 
            copyContentFiles: contentFiles.length > 0,
            packageBranding: packageBranding,
        }),
        deployment: {
            contents: [
                ...args.assemblies.map(asm => createAssemblyLayout(asm)),
                ...contentFiles,
                ...args.additionalContent || [],
            ]
        },
        deploymentOptions: args.deploymentOptions,
        noPackageAnalysis: true,
        noDefaultExcludes: true,
    }).nuPkg;
}

/**
 * Verifies that all packages built by this SDK contain assemblies with the prefix "BuildXL."
 */
function assertAssemblyPrefix(args : PackageSpecification) {
    // Enforce a requirement that all assemblies inside BuildXL packages must start with the 'BuildXL.' prefix 
    // so that the filtering logic for inferring packages below properly picks up BuildXL references.
    for (let asm of args.assemblies) {
        if (asm === undefined) {
            continue;
        }
        
        if (!asm.name.toString().startsWith("BuildXL")) {
            Contract.fail(`Assembly '${asm.name}' for package '${args.id.id}' does not start with the prefix 'BuildXL'. All assemblies being packaged for BuildXL must have this prefix.`);
        }
    }
}

@@public
export function createMetaData(args: {
    id: string,
    dependencies: Dependency[],
    copyContentFiles?: boolean,
    packageBranding : PackageBranding,
}) : PackageMetadata
{
    return {
        id: args.id,
        version: args.packageBranding.version,
        authors: args.packageBranding.authors,
        owners: args.packageBranding.owners,
        copyright: args.packageBranding.copyright,
        tags: `${args.packageBranding.company} ${args.packageBranding.shortProductName} MSBuild Build`,
        description: `${args.packageBranding.shortProductName} is a build engine that comes with a new build automation language. ${args.packageBranding.shortProductName} performs fast parallel incremental builds enabled by fine-grained dataflow dependency information. All build artifacts are cached locally, and eventually shared between different machines. The engine can run on a single machine, and it will perform distributed builds on many machines in a lab or in the cloud.`,
        dependencies: args.dependencies,
        contentFiles: args.copyContentFiles
            ? [{
                include: "**",
                copyToOutput: true,
                buildAction: "None",
                }]
            : undefined,
    };
}

@@public
export function pack(args: Arguments): PackResult {

    const outDir = Context.getNewOutputDirectory("nuget-pack");
    const packName = `${args.metadata.id}.${args.metadata.version}`;
    const nuspecPath = p`${outDir}/${packName + ".nuspec"}`;
    const nupkgPath = p`${outDir}/${packName + ".nupkg"}`;

    // Due to nuspec file not supporting renaming files, we have to compute the dependencies on the fly since we need to copy renames to a temp location with the same name.
    const nuspecData = createNuSpecFile(args.metadata, args.deployment, nuspecPath, args.deploymentOptions, args.filterFiles);

    const arguments: Argument[] = [
        Cmd.argument("pack"),
        Cmd.argument(Artifact.input(nuspecData.nuspec)),
        Cmd.argument("-NonInteractive"),
        Cmd.argument("-NoDefaultExcludes"),
        Cmd.option("-Verbosity ", "detailed"),
        Cmd.argument("-ForceEnglishOutput"),
        Cmd.option("-ConfigFile ", Artifact.input(f`empty.config`)),
        Cmd.option("-OutputDirectory ", Artifact.none(outDir)),
        Cmd.flag("-NoDefaultExcludes", args.noDefaultExcludes),
        Cmd.flag("-NoPackageAnalysis", args.noPackageAnalysis),
        Cmd.option("-MinClientVersion ", args.minClientVersion)
    ];

    let execArgs = <Transformer.ExecuteArguments>{
        tool: args.tool || tool,
        tags: [
            "pack",
            ...(args.tags || []),
        ],
        arguments: arguments,
        workingDirectory: outDir,
        allowUndeclaredSourceReads: Context.getCurrentHost().os !== "win",
        dependencies: nuspecData.dependencies,
        outputs: [
            nupkgPath
        ],
        unsafe: {
            untrackedScopes: [
                ...addIfLazy(Context.getCurrentHost().os === "unix", () => [
                    d`${Context.getMount("UserProfile").path}/.local/share/NuGet`,
                    d`${Context.getMount("UserProfile").path}/.config/share/NuGet`,
                    d`${Context.getMount("UserProfile").path}/.nuget/share/NuGet`,
                ]),
            ],
            untrackedPaths: [
                ...addIfLazy(Context.getCurrentHost().os === "unix", () => [
                    d`${Context.getMount("UserProfile").path}`,
                ]),
            ]
        }
    };

    const executeResult =  Context.getCurrentHost().os === "win"
        ? Transformer.execute(execArgs)
        : Mono.execute(execArgs);

    return {
        nuPkg: executeResult.getOutputFile(nupkgPath)
    };
}

function createNuSpecFile(
    metaData: PackageMetadata, 
    deployment: Deployment.Definition, 
    nuSpecOutput: Path,
    deploymentOptions: Managed.Deployment.FlattenOptions,
    filterFiles?: PathAtom[]) : { nuspec: File, dependencies: (File|OpaqueDirectory)[] } {

    let optionalElement = (element:string, value: string) => String.isUndefinedOrEmpty(value)
        ? undefined
        : Xml.elem(element, value);

    let dependencies : (File|OpaqueDirectory)[] = [];
    let fileElements : Xml.Element[] = [];

    let flattened = Deployment.flatten(deployment, undefined, deploymentOptions);

    // Process the flattened files with one quirck where we have to handle nuspec not supporting renamed files
    for (let flattenedFile of flattened.flattenedFiles.toArray())
    {
        const target = flattenedFile[0];
        let source = flattenedFile[1].file;

        // Skip the file if it is part of the files to filter
        if (filterFiles && filterFiles.indexOf(source.name) !== -1)
        {
            continue;
        }

        if (target.name !== source.path.name) {
            // NuGet.exe does not like if target name is different from the source name.
            //   target: a.txt
            //   source: a.txt.in
            // NuGet will create a.txt\a.txt.in
            source = Transformer.copyFile(source, p`${Context.getNewOutputDirectory("tmp_rename_for_nuget")}/${target.name}`);
        }

        dependencies = dependencies.push(source);
        fileElements = fileElements.push(
            Xml.elem("file",
                Xml.attr("src", source),
                Xml.attr("target", target)
            )
        );
    }

    // Process the flattened opaque directories
    for (let opaque of flattened.flattenedOpaques.toArray())
    {
        dependencies = dependencies.push(opaque[1].opaque);
        fileElements = fileElements.push(
            Xml.elem("file",
                Xml.attr("src", [p`${opaque[1].opaque.path}\${opaque[1].subDirectory || r`.`}`, "\\**"]),
                Xml.attr("target", opaque[0])
            )
        );
    }

    const groupedDependencies = (metaData.dependencies || [])
        .groupBy(dep => dep.targetFramework);

    let packageDependencies : Xml.Element[] = [];
    if (groupedDependencies.length > 0)
    {
        packageDependencies = groupedDependencies.map(group =>
            Xml.elem("group",
                group.key 
                    ? Xml.attr("targetFramework", group.key)
                    : undefined,
                ...group.values.map(d => 
                    Xml.elem("dependency",
                        Xml.attr("id", d.id),
                        Xml.attr("version", d.version),
                        Xml.attr("include", "all")
                    )
                )
            )
        );
    }

    const nuSpecDoc = Xml.doc(
        Xml.elem({ local: "package", namespace: "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd" },
            Xml.elem("metadata",
                optionalElement("id", metaData.id),
                optionalElement("version", metaData.version),
                optionalElement("title", metaData.title),
                optionalElement("authors", metaData.authors),
                optionalElement("owners", metaData.owners),
                optionalElement("licenseUrl", metaData.licenseUrl),
                optionalElement("projectUrl", metaData.projectUrl),
                optionalElement("description", metaData.description),
                optionalElement("summary", metaData.summary),
                optionalElement("copyright", metaData.copyright),
                optionalElement("tags", metaData.tags),
                optionalElement("requireLicenseAcceptance", metaData.requireLicenseAcceptance ? "true": "false"),
                Xml.elem("dependencies", ...packageDependencies),
                metaData.contentFiles 
                    ? Xml.elem("contentFiles",
                        ...metaData.contentFiles.map(c =>
                            Xml.elem("files",
                                Xml.attr("include", c.include),
                                c.exclude && Xml.attr("exclude", c.exclude),
                                c.buildAction && Xml.attr("buildAction", c.buildAction),
                                c.copyToOutput !== undefined 
                                    ? Xml.attr("copyToOutput", c.copyToOutput ? "true" : "false")
                                    : undefined,
                                c.flatten !== undefined
                                    ? Xml.attr("flatten", c.flatten ? "true" : "false")
                                    : undefined
                            )
                        )
                    )
                    : undefined
            ),
            Xml.elem("files", ...fileElements)
        )
    );

    let nuspec = Xml.write(nuSpecOutput, nuSpecDoc);

    return {
        nuspec: nuspec,
        dependencies: dependencies,
    };
}

function pathToStr(p: Path | RelativePath | Directory): string {
    let parent = p.parent;
    return parent === undefined
        ? (p.name ? p.name.toString() : "")
        : pathToStr(parent) + "\\" + p.name.toString();
}

const emptyFile = Transformer.writeAllText({
    outputPath: p`${Context.getNewOutputDirectory("nugetTmp")}/_._`, 
    text: ""
});

@@public
export function createAssemblyLayout(assembly: Managed.Assembly, useRuntime?: boolean) : Deployment.Definition {
    // When the assembly is undefined, return empty deployment.
    if (assembly === undefined) {
        return {
            contents: []
        };
    }
    
    let contents = [];

    // TODO: have to figure out how to check targetruntime when we add more windows versions.
    if (!useRuntime || assembly.targetRuntime === "win-x64")
    {
        // The default is lib. but if we deplyo for multiple targetruntimes
        // we have to use rumtimes/{rid}/lib/{tfm} pattern. Unfortunately this
        // doesn't work in the old c++ C# project system. So we still have to
        // populate the lib folder. Since the c++ project system doesn't support
        // mac we hae to filter to windows only to ensure no double-writes.
        contents = contents.push({
            subfolder: r`lib/${assembly.targetFramework}`,
            contents: [
                assembly.runtime || emptyFile,
            ]
        });
    }

    if (useRuntime && assembly.targetRuntime)
    {
        // For target runtime specialization we have to use the runtimes pattern.
        // Unfortunatley we often still have to double deploy the windows, see comment above
        // for details.
        contents = contents.push({
            subfolder: r`runtimes/${assembly.targetRuntime}/lib/${assembly.targetFramework}`,
            contents: [
                assembly.runtime || emptyFile,
            ]
        });
    }

    // if we have a ref assembly
    if (assembly.runtime !== assembly.compile)
    {
        // nuget does not support ref assemblies per runtime, so hack by only doing windows
        if (!useRuntime || assembly.targetRuntime === "win-x64")
        {
            contents = contents.push({
                subfolder: r`ref/${assembly.targetFramework}`,
                contents: [
                    assembly.compile || emptyFile,
                ]
            });
        }
    }

    return {
        contents: contents
    };
}

@@public
export function createAssemblyLayoutWithSpecificRuntime(assembly: Managed.Assembly, runtime: string, includeInRef: boolean) : Deployment.Definition {
    // When the assembly is undefined, return empty deployment.
    if (assembly === undefined) {
        return {
            contents: []
        };
    }

    return {
        contents: [
            {
                subfolder: r`runtimes/${runtime}/lib/${assembly.targetFramework}`,
                contents: [
                    assembly.runtime || emptyFile,
                ]
            },
            ... includeInRef ? [
                {
                    subfolder: r`ref/${assembly.targetFramework}`,
                    contents: [
                        assembly.compile || emptyFile,
                    ]
                }
            ] : []
        ]
    };
}