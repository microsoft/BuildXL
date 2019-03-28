// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

function dlls(contents: StaticDirectory): Managed.Binary[] {
    // Getting dlls from the 'cs' folder.
    // This is not 100% safe but good enough.

    return contents
        .getContent()
        .filter(file => file.extension === a`.dll` && file.parent.name === a`cs`)
        .map(file => Managed.Factory.createBinary(contents, file));
}

/** Returns analyzers dlls used by the BuildXL team. */
export function getAnalyzers(args: Arguments) : Managed.Binary[] {
    let result = [
        ...dlls(importFrom("AsyncFixer").Contents.all),
        ...dlls(importFrom("ErrorProne.NET.CoreAnalyzers").Contents.all)
    ];

    // FxCop analyzers, when we turn them back on we can uncomment these
    // result = [
    //    ...result,
    //    ...dlls(importFrom("Microsoft.CodeQuality.Analyzers").Contents.all),
    //    ...dlls(importFrom("Microsoft.NetCore.Analyzers").Contents.all),
    //    ...dlls(importFrom("Microsoft.NetFramework.Analyzers").Contents.all),
    //];

    if (args.enableStyleCopAnalyzers) {
        result = [
            ...result,
            ...dlls(importFrom("StyleCop.Analyzers").Contents.all),
            ];
    }
    
    return result;
}
