// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Managed from "Sdk.Managed";

// This is an empty facade for a Microsoft internal package.

namespace Contents {
    export declare const qualifier: {
    };

    @@public
    export const all: StaticDirectory = Transformer.sealPartialDirectory(d`.`, []);
}

@@public
export const pkg: Managed.ManagedNugetPackage =
    Managed.Factory.createNugetPackage(
        "Drop.App.Core",
        "0.0.0",
        Contents.all,
        [],
        [],
        []
    );