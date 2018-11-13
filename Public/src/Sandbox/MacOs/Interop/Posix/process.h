//
//  process.h
//  Interop
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef process_h
#define process_h

#include "Dependencies.h"

typedef struct {
    double startTime;
    double exitTime;
    unsigned long systemTime;
    unsigned long userTime;
} ProcessTimesInfo;

int GetResourceUsage(pid_t pid, rusage_info_current *buffer);
int GetProcessTimes(pid_t pid, ProcessTimesInfo *buffer);

#endif /* process_h */
