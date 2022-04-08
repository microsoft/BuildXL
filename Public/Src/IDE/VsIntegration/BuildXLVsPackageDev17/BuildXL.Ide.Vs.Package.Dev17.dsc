// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Branding from "BuildXL.Branding";
import * as Contracts from "Tse.RuntimeContracts";

namespace BuildXLVsPackageDev17 {
    @@public
    export const dll = !BuildXLSdk.Flags.isMicrosoftInternal ? undefined : BuildXLSdk.library({
        assemblyName: "BuildXLVsPackageDev17",
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
                    f`extension.vsixmanifest`,
                ],
            },
        ],
        references: [
            NetFx.Microsoft.Build.dll,
            NetFx.Microsoft.Build.Engine.dll,
            NetFx.Microsoft.Build.Framework.dll,
            NetFx.System.ComponentModel.Composition.dll,
            NetFx.System.Drawing.dll,
            NetFx.System.Windows.dll,
            NetFx.System.Windows.Forms.dll,
            NetFx.System.Xml.dll,
            NetFx.System.Xml.Linq.dll,
            importFrom("Microsoft.Internal.VisualStudio.Interop").pkg,
            importFrom("Microsoft.VisualStudio.ComponentModelHost").pkg,
            importFrom("Microsoft.VisualStudio.Shell.Framework").pkg,
            importFrom("Microsoft.VisualStudio.Interop").pkg,
            importFrom("Microsoft.VisualStudio.ProjectAggregator").pkg,
            importFrom("Microsoft.VisualStudio.Shell.15.0").pkg,
            importFrom("Microsoft.VisualStudio.Threading").pkg,
            importFrom("Microsoft.VisualStudio.ProjectSystem").pkg,
            importFrom("Microsoft.VisualStudio.Composition").pkg,
            importFrom("System.Collections.Immutable.ForVBCS").pkg,
        ],
        defineConstants: [ "Dev17" ],
    });

    const deployment: Deployment.Definition = {
        contents: [
            f`extension.vsixmanifest`,
            f`../BuildXLVsPackageShared/Resources/[Content_Types].xml`,
            f`BuildXLVsPackageDev17.pkgdef`,
            dll.runtime,
            {
                subfolder: PathAtom.create("Resources"),
                contents: [Branding.iconFile],
            },
        ],
    };

    @@public
    export const vsix = CreateZipPackage.zip({
        outputFileName: "BuildXL.vs.Dev17.vsix",
        useUriEncoding: true,
        inputDirectory: Deployment.deployToDisk({
            definition: deployment,
            targetDirectory: Context.getNewOutputDirectory("BuildXLVsPackageDev17Vsix"),
        }).contents,
    });
}
