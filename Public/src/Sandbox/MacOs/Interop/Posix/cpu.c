//
//  cpu.c
//  Interop
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include "cpu.h"

int GetCpuLoadInfo(CpuLoadInfo *buffer)
{
    mach_msg_type_number_t cpuInfoCount;
    processor_info_array_t cpuInfo;
    natural_t numberOfLogicalCores = 0U;
    
    unsigned long totalUserTime = 0;
    unsigned long totalSystemTime = 0;
    unsigned long totalIdleTime = 0;
    
    kern_return_t error = host_processor_info(mach_host_self(), PROCESSOR_CPU_LOAD_INFO, &numberOfLogicalCores, &cpuInfo, &cpuInfoCount);
    if(error != KERN_SUCCESS)
    {
        return error;
    }
    
    // Iterate over all logical cores and aggregate load numbers
    for(natural_t i = 0; i < numberOfLogicalCores; ++i)
    {
        totalUserTime += cpuInfo[(CPU_STATE_MAX * i) + CPU_STATE_USER] + cpuInfo[(CPU_STATE_MAX * i) + CPU_STATE_NICE];
        totalSystemTime += cpuInfo[(CPU_STATE_MAX * i) + CPU_STATE_SYSTEM];
        totalIdleTime += cpuInfo[(CPU_STATE_MAX * i) + CPU_STATE_IDLE];
    }
    
    buffer->systemTime = totalSystemTime;
    buffer->userTime = totalUserTime;
    buffer->idleTime = totalIdleTime;
    
    return KERN_SUCCESS;
}
