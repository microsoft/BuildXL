// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

@@public
export function createPublicDotNetRuntime(v3Runtime : StaticDirectory, v2Runtime: StaticDirectory) : StaticDirectory {
    const netCoreAppV2 = v2Runtime.contents.filter(file => file.path.isWithin(d`${v2Runtime.root}/shared`));

    // We create a package that has dotnet executable and related SDK, plus the V2 SDK
    const dotNetRuntimeRoot = Context.getNewOutputDirectory("DotNet-Runtime");
    
    const sealDirectory = Transformer.sealDirectory(
        dotNetRuntimeRoot, 
        [
            ...v3Runtime.contents.map(file => Transformer.copyFile(file, file.path.relocate(v3Runtime.root, dotNetRuntimeRoot))), 
            ...netCoreAppV2.map(file => Transformer.copyFile(file, file.path.relocate(v2Runtime.root, dotNetRuntimeRoot)))
        ]
    );

    return sealDirectory;
}
