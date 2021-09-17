// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

@@public
export function createPublicDotNetRuntime(v3Runtime : StaticDirectory, v2Runtime: StaticDirectory) : StaticDirectory {
    // We create a package that has dotnet executable and related SDK, plus the V2 SDK
    const dotNetRuntimeRoot = Context.getNewOutputDirectory("DotNet-Runtime");

    if (v3Runtime === undefined && v2Runtime === undefined)
    {
        return Transformer.sealDirectory(dotNetRuntimeRoot, []);
    }

    let dotNetRuntimeV3 : SharedOpaqueDirectory = undefined;
    let dotNetRuntimeV2 : SharedOpaqueDirectory = undefined;

    // When the final package has to be constructed from two sources, we need to build the final layout in an intermediate 
    // location and then do a final copy directory action since downstream consumers make static directory assertions on the resulting output
    // directory, and composite opaques are not supported for those yet
    const singleSource = v3Runtime === undefined || v2Runtime === undefined;
    const intermediateRoot = singleSource ? dotNetRuntimeRoot : Context.getNewOutputDirectory("DotNet-Runtime-Temp");

    if (v3Runtime !== undefined) {
        dotNetRuntimeV3 = Transformer.copyDirectory({
            sourceDir: v3Runtime.root,
            targetDir: intermediateRoot, 
            recursive: true, 
            dependencies: [v3Runtime]});
    }

    if (v2Runtime !== undefined) {
        dotNetRuntimeV2 = Transformer.copyDirectory({
            sourceDir: d`${v2Runtime.root}/shared`, 
            targetDir: d`${intermediateRoot}/shared`, 
            recursive: true,
            dependencies: [v2Runtime]});
    }

    if (singleSource) {
        return dotNetRuntimeV3 !== undefined 
            ? dotNetRuntimeV3 
            : dotNetRuntimeV2;
    }

    return Transformer.copyDirectory({
        sourceDir: intermediateRoot,
        targetDir: dotNetRuntimeRoot,
        recursive: true,
        dependencies: [dotNetRuntimeV3, dotNetRuntimeV2]
    });
}
