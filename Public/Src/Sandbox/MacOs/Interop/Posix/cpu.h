// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef cpu_h
#define cpu_h

#include "Dependencies.h"

// CPU load information (unit: ticks)
typedef struct {
    unsigned long systemTime;
    unsigned long userTime;
    unsigned long idleTime;
} CpuLoadInfo;

int GetCpuLoadInfo(CpuLoadInfo *buffer, long bufferSize);

#endif /* cpu_h */
