//
//  memory.h
//  Interop
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef memory_h
#define memory_h

#include "Dependencies.h"

#define GET_PAGE_SIZE_ERROR 101
#define GET_VM_STATS_ERROR 102

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
} RamUsageInfo;

int GetRamUsageInfo(RamUsageInfo *buffer);
int GetPeakWorkingSetSize(pid_t pid, uint64_t *buffer);

#endif /* memory_h */
