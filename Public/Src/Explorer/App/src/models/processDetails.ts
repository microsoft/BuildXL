// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as References from "./References";
import { PipDetails } from "./pipDetails";
import {PipData} from "./pipData";

export interface ProcessDetails extends PipDetails {
    executable: References.FileRef,
    arguments: PipData,
    workingDirectory: References.DirectoryRef,
    environmentVariables: EnvironmentVariable[],

    untrackedScopes?: References.DirectoryRef[],
    untrackedFiles?: References.FileRef[],
}

export function isProcess(pip: PipDetails) : pip is ProcessDetails {
    return pip.kind === "process";
}

export interface EnvironmentVariable {
    name: string,
    value: PipData,
    isPassthrough: boolean,
}
