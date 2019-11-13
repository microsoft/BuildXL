// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef memory_h
#define memory_h

#include <sys/sysctl.h>
#include "Dependencies.h"

#define GET_PAGE_SIZE_ERROR     101
#define GET_VM_STATS_ERROR      102

// Memory usage information in bytes
typedef struct {
    uint64_t active;
    uint64_t inactive;
    uint64_t wired;
    uint64_t speculative;
    uint64_t free;
    uint64_t purgable;
    uint64_t file_backed;
    uint64_t compressed;
    uint64_t internal;
} RamUsageInfo;

int GetRamUsageInfo(RamUsageInfo *buffer, long bufferSize);
int GetPeakWorkingSetSize(pid_t pid, uint64_t *buffer);
int GetMemoryPressureLevel(int *level);

#endif /* memory_h */
