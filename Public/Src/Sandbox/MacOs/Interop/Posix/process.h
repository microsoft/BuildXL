// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef process_h
#define process_h

#include "Dependencies.h"

typedef struct {
    double startTime;
    double exitTime;
    unsigned long systemTime;
    unsigned long userTime;
} ProcessTimesInfo;

int GetProcessTimes(pid_t pid, ProcessTimesInfo *buffer, long bufferSize, bool includeChildProcesses);

typedef struct {
    char *outputPath;
} CoreDumpConfiguration;

#define THREAD_TID_MAPPING_FILE "thread_tids"
#define SYSCTL_KERN_COREFILE "kern.corefile"
#define KERN_COREFILE_DEFAULT_PATH "/cores/core.%i.tids"

bool SetupProcessDumps(const char *logsDirectory, /*out*/ char *buffer, size_t bufsiz);
void TeardownProcessDumps(void);

void RegisterSignalHandlers(void);

#endif /* process_h */
