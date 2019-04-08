// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

namespace Detours.Include {
    export const includes: StaticDirectory = Transformer.sealSourceDirectory(d`.`);
}
