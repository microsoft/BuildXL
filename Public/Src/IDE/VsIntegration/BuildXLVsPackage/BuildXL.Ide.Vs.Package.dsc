// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Branding from "BuildXL.Branding";
import * as Contracts from "Tse.RuntimeContracts";

namespace BuildXLVsPackage {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXLVsPackage",
        rootNamespace: "BuildXL.VsPackage",
        skipDocumentationGeneration: false,
        sources: globR(d`../BuildXLVsPackageShared`, "*.cs"),

        // Disabling runtime contracts to avoid redundant dependency.
        contractsLevel: Contracts.ContractsLevel.disabled,
        embeddedResources: [
            {
                resX: f`../BuildXLVsPackageShared/Resources/MessageDialog.resx`, 
                generatedClassMode: "explicit",
            },
            {
                resX: f`../BuildXLVsPackageShared/Strings.resx`,
            },
            {
                linkedContent: [
                    Branding.iconFile,
                    f`source.extension.vsixmanifest`,
                ],
            },
        ],
        references: [
            NetFx.Microsoft.Build.dll,
            NetFx.Microsoft.Build.Engine.dll,
            NetFx.Microsoft.Build.Framework.dll,

            importFrom("Microsoft.VisualStudio.ComponentModelHost.Legacy").pkg,
            importFrom("Microsoft.VisualStudio.Shell.Framework.Legacy").pkg,
            importFrom("Microsoft.VisualStudio.Shell.Interop.Legacy").pkg,
            importFrom("Microsoft.VisualStudio.Shell.Interop.8.0").pkg,
            importFrom("Microsoft.VisualStudio.Shell.Interop.9.0").pkg,
            importFrom("Microsoft.VisualStudio.Shell.Interop.10.0.Legacy").pkg,
            importFrom("Microsoft.VisualStudio.Shell.Interop.11.0.Legacy").pkg,
            importFrom("Microsoft.VisualStudio.Shell.Interop.12.0.Legacy").pkg,
            importFrom("Microsoft.VisualStudio.Shell.Interop.14.0.DesignTime").pkg,
            importFrom("Microsoft.VisualStudio.TextManager.Interop.Legacy").pkg,
            importFrom("Microsoft.VisualStudio.TextManager.Interop.8.0.Legacy").pkg,
            importFrom("Microsoft.VisualStudio.Shell.Immutable.10.0.Legacy").pkg,
            importFrom("Microsoft.VisualStudio.Shell.Immutable.11.0.Legacy").pkg,
            importFrom("Microsoft.VisualStudio.Shell.Immutable.12.0.Legacy").pkg,
            importFrom("Microsoft.VisualStudio.Shell.Immutable.14.0.Legacy").pkg,
            importFrom("Microsoft.VisualStudio.ProjectAggregator.Legacy").pkg,
            importFrom("EnvDTE.Legacy").pkg,
            importFrom("EnvDTE80.Legacy").pkg,
            importFrom("VSLangProj.Legacy").pkg,
            importFrom("VSLangProj2.Legacy").pkg,
            // The following don't declare the nuget version to use
            Managed.Factory.createBinary(importFrom("Microsoft.VisualStudio.OLE.Interop.Legacy").Contents.all, r`lib/Microsoft.VisualStudio.OLE.Interop.dll`),
            Managed.Factory.createBinary(importFrom("Microsoft.VisualStudio.Shell.14.0").Contents.all, r`lib/Microsoft.VisualStudio.Shell.14.0.dll`),

            NetFx.System.ComponentModel.Composition.dll,
            NetFx.System.Drawing.dll,
            NetFx.System.Windows.dll,
            NetFx.System.Windows.Forms.dll,
            NetFx.System.Xml.dll,
            NetFx.System.Xml.Linq.dll,
            importFrom("System.Collections.Immutable").pkg,
            importFrom("Microsoft.VisualStudio.Threading.Legacy").pkg,
            importFrom("Microsoft.VisualStudio.ProjectSystem.Legacy").pkg,
            importFrom("Microsoft.VisualStudio.Composition").pkg,
        ],
    });
    
    const deployment: Deployment.Definition = {
        contents: [
            f`extension.vsixmanifest`,
            f`../BuildXLVsPackageShared/Resources/[Content_Types].xml`,
            f`BuildXLVsPackage.pkgdef`,
            dll.runtime,
            {
                subfolder: PathAtom.create("Resources"),
                contents: [Branding.iconFile],
            },
        ],
    };
    
    @@public
    export const vsix = CreateZipPackage.zip({
        outputFileName: "BuildXL.vs.vsix",
        useUriEncoding: true,
        inputDirectory: Deployment.deployToDisk({
            definition: deployment,
            targetDirectory: Context.getNewOutputDirectory("BuildXLVsPackageVsix"),
        }).contents,
    });
}
