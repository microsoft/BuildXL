// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Shared from "Sdk.Managed.Shared";

@@public
export interface AssemblyInfo {
    /**
     * Title used to generated the AssemblyTitleAttribute.
     *
     * This property is set as the FileDescription for the assembly file.
     */
    title?: string;

    /**
     * Product name used to generated the AssemblyProductAttribute
     *
     * This property is set as the FileDescription for the assembly file.
     */
    productName?: string;

    /**
     * Description used to generated the AssemblyDescriptionAttribute
     */
    description?: string;

    /**
     * Company name used to generated the AssemblyCompanyAttribute
     */
    company?: string;

    /**
     * Copyright message used to generated the AssemblyCopyrightAttribute
     */
    copyright?: string;

    /**
     * Version used to generated the AssemblyVersionAttribute
     */
    version?: string;

    /**
     * FileVersion used to generated the AssemblyFileVersionAttribute
     */
    fileVersion?: string;

    /**
     * Whether ComVisible should be emitted with true or false.
     */
    comVisible?: boolean;

    /**
     * Culture used to generate the NeutralResourcesLanguageAttribute
     */
    neutralResourcesLanguage?: string;

    /**
     * The target framework
     */
    targetFramework?: string;

    /**
     * The displayname of the target framework
     *
     * If this is set the targetFramework field must also be set.
     */
    targetFrameworkDisplayName?: string;

    /**
     * The configuration this assemlby was produced in
     */
    configuration?: string;

    /**
     * The location of theme-specific resources.
     *
     * If this is set the genericDictionaryLocation field must also be set.
     */
    themeDictionaryLocation?: ResourceDictionaryLocation;

    /**
     * The location of generic, not theme-specific, resources.
     *
     * If this is set the themeDictionaryLocation field must also be set.
     */
    genericDictionaryLocation?: ResourceDictionaryLocation;
}

const assemblyInfoTemplate = [
    "//------------------------------------------------------------------------------",
    "// <auto-generated>",
    "//     This code was generated by a tool.",
    "//",
    "//     Changes to this file may cause incorrect behavior and will be lost if",
    "//     the code is regenerated.",
    "// </auto-generated>",
    "//------------------------------------------------------------------------------",
    "",
    "using System.Reflection;",
    "using System.Resources;",
    "using System.Runtime.CompilerServices;",
    "using System.Runtime.InteropServices;",
    "using System.Runtime.Versioning;",
    "",
    "// General Information about an assembly is controlled through the following",
    "// set of attributes. Change these attribute values to modify the information",
    "// associated with an assembly.",
    "",
];

export function generateAssemblyInfoFile(framework: Shared.Framework, assemblyName: string, assemblyInfo: AssemblyInfo) : File {

    const defaultAssemblyInfo = {
        productName: assemblyName,
        version: "1.0.0.0",
        fileVersion: "1.0.0.0",
        targetFramework: framework.assemblyInfoTargetFramework,
        targetFrameworkDisplayName: framework.assemblyInfoFrameworkDisplayName,
    };

    assemblyInfo = defaultAssemblyInfo.merge(assemblyInfo);

    let lines: string[] = [
        ...assemblyInfoTemplate,
        assemblyInfo.title       && `[assembly: AssemblyTitle("${assemblyInfo.title}")]`,
        assemblyInfo.description && `[assembly: AssemblyDescription("${assemblyInfo.description}")]`,
        assemblyInfo.productName && `[assembly: AssemblyProduct("${assemblyInfo.productName}")]`,
        assemblyInfo.company     && `[assembly: AssemblyCompany("${assemblyInfo.company}")]`,
        assemblyInfo.copyright   && `[assembly: AssemblyCopyright("${assemblyInfo.copyright}")]`,
        assemblyInfo.version     && `[assembly: AssemblyVersion("${assemblyInfo.version}")]`,
        assemblyInfo.fileVersion && `[assembly: AssemblyFileVersion("${assemblyInfo.fileVersion}")]`,
        assemblyInfo.configuration && `[assembly: AssemblyConfiguration("${assemblyInfo.configuration}")]`,
        assemblyInfo.neutralResourcesLanguage && `[assembly: NeutralResourcesLanguage("${assemblyInfo.neutralResourcesLanguage}")]`,
        (assemblyInfo.comVisible !== undefined) ? `[assembly: ComVisible(${assemblyInfo.comVisible ? "true" : "false"})]` : undefined,
    ];

    if (!String.isUndefinedOrEmpty(assemblyInfo.targetFrameworkDisplayName))
    {
        if (String.isUndefinedOrEmpty(assemblyInfo.targetFramework))
        {
            Contract.fail("When targetFrameworkDisplayName is set, targetFramework must be set as well.");
        }

        lines = lines.push(
            `[assembly: TargetFramework("${assemblyInfo.targetFramework}", FrameworkDisplayName="${assemblyInfo.targetFrameworkDisplayName}")]`
        );
    }
    else if (!String.isUndefinedOrEmpty(assemblyInfo.targetFramework))
    {
        lines = lines.push(
            assemblyInfo.targetFramework && !String.isUndefinedOrEmpty(assemblyInfo.targetFramework) && `[assembly: TargetFramework("${assemblyInfo.targetFramework}")]`
        );
    }

    if (assemblyInfo.themeDictionaryLocation !== undefined && assemblyInfo.genericDictionaryLocation !== undefined)
    {
        if (assemblyInfo.themeDictionaryLocation !== "None" || assemblyInfo.genericDictionaryLocation !== "None")
        {
            let themeDictionaryLoc:string = assemblyInfo.themeDictionaryLocation;
            let genericDictionaryLoc:string = assemblyInfo.genericDictionaryLocation;
            lines = lines.push(
                `[assembly: System.Windows.ThemeInfo(System.Windows.ResourceDictionaryLocation.${themeDictionaryLoc}, System.Windows.ResourceDictionaryLocation.${genericDictionaryLoc})]`
            );
        }
    }

    lines = lines.filter(x => x !== undefined);

    let assemblyInfoFile = Context.getNewOutputDirectory("AssemblyInfoGen").combine("AssemblyInfo.g.cs");

    return Transformer.writeAllLines({
        outputPath: assemblyInfoFile, 
        lines: lines
    });
}

type ResourceDictionaryLocation = "None" | "ExternalAssembly" | "SourceAssembly";

