// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Managed from "Sdk.Managed";

export namespace Contents {
    @@public
    export const all: StaticDirectory = Transformer.sealPartialDirectory(d`.`,[]);
}

@@public
export const pkg: Managed.ManagedNugetPackage =
    Managed.Factory.createNugetPackage(
        "Bond.Rpc.NET",
        "3.2.0",
        Contents.all,
        [],
        [],
        []
    );