// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

import * as Managed    from "Sdk.Managed.Shared";
import * as Deployment from "Sdk.Deployment";
import * as Nuget      from "NuGet.Commandline";
import * as Xml        from "Sdk.Xml";
import * as Mono       from "Sdk.Mono";

@@public
export const tool : Transformer.ToolDefinition = {
    exe: Nuget.Contents.all.getFile(r`tools/nuget.exe`),
    description: "NuGet pack",
    untrackedDirectoryScopes: [
        d`${Context.getMount("ProgramData").path}/Nuget`
    ],
    dependsOnWindowsDirectories: true,
    dependsOnAppDataDirectory: true,
    prepareTempDirectory: true,
};

@@public
export interface Arguments extends Transformer.RunnerArguments {
    /** Deployed files. */
    deployment?: Deployment.Definition;

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
export function pack(args: Arguments): PackResult {

    const outDir = Context.getNewOutputDirectory("nuget-pack");
    const packName = `${args.metadata.id}.${args.metadata.version}`;
    const nuspecPath = p`${outDir}/${packName + ".nuspec"}`;
    const nupkgPath = p`${outDir}/${packName + ".nupkg"}`;

    // Due to nuspec file not supporting renaming files, we have to compute the dependencies on the fly since we need to copy renames to a temp location with the same name.
    const nuspecData = createNuSpecFile(args.metadata, args.deployment, nuspecPath, args.deploymentOptions);

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
        tags: (args.tags || ["package"]),
        arguments: arguments,
        workingDirectory: outDir,
        allowUndeclaredSourceReads: Context.getCurrentHost().os === "macOS",
        dependencies: nuspecData.dependencies,
        outputs: [
            nupkgPath
        ]
    };

    const executeResult =  Context.getCurrentHost().os === "win" ?
        Transformer.execute(execArgs) :
        Mono.execute(execArgs);

    return {
        nuPkg: executeResult.getOutputFile(nupkgPath)
    };
}

function createNuSpecFile(
    metaData: PackageMetadata, 
    deployment: Deployment.Definition, 
    nuSpecOutput: Path,
    deploymentOptions: Managed.Deployment.FlattenOptions) : { nuspec: File, dependencies: (File|OpaqueDirectory)[] } {

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
        dependencies = dependencies.push(opaque[1]);
        fileElements = fileElements.push(
            Xml.elem("file",
                Xml.attr("src", [opaque[1].path, "\\**"]),
                Xml.attr("target", opaque[0])
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
                Xml.elem("dependencies",
                    ...(metaData.dependencies || []).map(d =>
                        Xml.elem("dependency",
                            Xml.attr("id", d.id),
                            Xml.attr("version", d.version)
                        )
                    )
                ),
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
export function createAssemblyLayout(assembly: Managed.Assembly) : Deployment.Definition {
    // When the assembly is undefined, return empty deployment.
    if (assembly === undefined) {
        return {
            contents: []
        };
    }

    return {
        contents: [
            {
                subfolder: r`lib/${assembly.targetFramework}`,
                contents: [
                    assembly.runtime || emptyFile,
                ]
            },
            {
                subfolder: r`ref/${assembly.targetFramework}`,
                contents: [
                    assembly.compile || emptyFile,
                ]
            }
        ]
    };
}