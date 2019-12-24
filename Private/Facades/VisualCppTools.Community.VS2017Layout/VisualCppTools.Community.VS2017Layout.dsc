// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

// This is an dummy facade for the VisualCppTools.Community.VS2017Layout package,
// it's used to avoid downloading the package on non-windows systems

namespace Contents {
    export declare const qualifier: {
    };

    @@public
    export const all: StaticDirectory = Transformer.sealDirectory({
        root: d`.`,
        files: []
    });
}

@@public
export const pkg: NugetPackage = {contents: Contents.all, dependencies: []};