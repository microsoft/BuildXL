//
//  cpu.h
//  Interop
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef cpu_h
#define cpu_h

#include "Dependencies.h"

// CPU load information (unit: ticks)
typedef struct {
    unsigned long systemTime;
    unsigned long userTime;
    unsigned long idleTime;
} CpuLoadInfo;

int GetCpuLoadInfo(CpuLoadInfo *buffer);

#endif /* cpu_h */
