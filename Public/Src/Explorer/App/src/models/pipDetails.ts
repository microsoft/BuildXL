// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as References from "./References";

export interface PipDetails extends References.PipRefWithDetails {
    shortDescription: string,
    longDescription?: string,

    tags: References.TagRef[],

    dependencies?: References.PipRefWithDetails[],
    dependents?: References.PipRefWithDetails[],
}
