// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as References from "./references"

export interface Summary extends References.BuildRef {
    startTime: string,
    duration: string,
    state: "",
    pipStats: PipStats,
}

export interface PipStats {
    
    processPipCacheHits: number,
    processPipCacheMisses: number,
    processDelayedBySemaphore: number,
    processPipsSkippedDueToFailedDependencies: number,

    total : PipStatsPerType,
    process : PipStatsPerType,
    writeFile: PipStatsPerType,
    sealDirectory: PipStatsPerType,
    ipc: PipStatsPerType,
    value: PipStatsPerType,
    specFile: PipStatsPerType,
    module: PipStatsPerType,
    hashSourceFile: PipStatsPerType,
}

export interface PipStatsPerType {
    total: number,
    done: number,
    failed: number,
    skipped: number,
    ignored: number,
}
