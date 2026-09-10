// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

// Empty facade used when the Microsoft-internal JSON-only model package is unavailable.
namespace Contents {
    export declare const qualifier: {
    };

    @@public
    export const all: StaticDirectory = Transformer.sealPartialDirectory(d`.`, []);
}

@@public
export const pkg: NugetPackage = {contents: Contents.all, dependencies: []};
