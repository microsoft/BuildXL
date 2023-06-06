// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

export function getAnalyzerDlls(contents: StaticDirectory): Managed.Binary[] {
    // Getting dlls from the 'cs' folder.
    // This is not 100% safe but good enough.

    return contents
        .getContent()
        .filter(file => file.extension === a`.dll` && file.parent.name === a`cs`)
        .map(file => Managed.Factory.createBinary(contents, file));
}

/** Returns analyzers dlls used by the BuildXL team. */
export function getAnalyzers(args: Arguments) : Managed.Binary[] {
    return [
        ...getAnalyzerDlls(importFrom("AsyncFixer").Contents.all),
        ...getAnalyzerDlls(importFrom("ErrorProne.NET.CoreAnalyzers").Contents.all),
        ...getAnalyzerDlls(importFrom("protobuf-net.BuildTools").Contents.all),
		...getAnalyzerDlls(importFrom("Microsoft.CodeAnalysis.NetAnalyzers").Contents.all),
        ...getAnalyzerDlls(importFrom("Microsoft.CodeAnalysis.BannedApiAnalyzers").Contents.all),
        ...addIfLazy(Flags.isMicrosoftInternal, () => [ ...getAnalyzerDlls(importFrom("Microsoft.Internal.Analyzers").Contents.all) ]),
    ];
}
