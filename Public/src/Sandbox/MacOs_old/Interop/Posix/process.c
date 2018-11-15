//
//  process.c
//  Interop
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include "process.h"

int GetResourceUsage(pid_t pid, rusage_info_current *buffer)
{
    if (proc_pid_rusage(pid, RUSAGE_INFO_CURRENT, (void **)&buffer) != 0)
    {
        return GET_RUSAGE_ERROR;
    }
    
    return KERN_SUCCESS;
}

int GetProcessTimes(pid_t pid, ProcessTimesInfo *buffer)
{
    mach_timebase_info_data_t timebase;
    kern_return_t ret = mach_timebase_info(&timebase);
    uint32_t numer = 1, denom = 1;
    
    if (ret == KERN_SUCCESS)
    {
        numer = timebase.numer;
        denom = timebase.denom;
    }
    
    rusage_info_current rusage;
    if (proc_pid_rusage(pid, RUSAGE_INFO_CURRENT, (void **)&rusage) != 0)
    {
        return GET_RUSAGE_ERROR;
    }
    
    uint64_t absoluteTime = mach_absolute_time();
    double factor = (((double)numer) / denom) / 1000000000;
    
    buffer->startTime = ((long)rusage.ri_proc_start_abstime - (long)absoluteTime) * factor;

    buffer->exitTime = rusage.ri_proc_exit_abstime != 0 ?
        (((long)rusage.ri_proc_exit_abstime - (long)absoluteTime) * factor) : 0;

    buffer->systemTime = rusage.ri_system_time;
    buffer->userTime = rusage.ri_user_time;
    
    return KERN_SUCCESS;
}
