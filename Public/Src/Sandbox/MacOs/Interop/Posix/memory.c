// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "memory.h"

int GetRamUsageInfo(RamUsageInfo *buffer, long bufferSize)
{
    if (sizeof(RamUsageInfo) != bufferSize)
    {
        printf("ERROR: Wrong size of RamUsageInfo buffer; expected %ld, received %ld\n", sizeof(RamUsageInfo), bufferSize);
        return 1;
    }

    vm_size_t page_size;
    kern_return_t error;
    
    error = host_page_size(mach_host_self(), &page_size);
    if(error != KERN_SUCCESS)
    {
        return GET_PAGE_SIZE_ERROR;
    }
    
    natural_t count = HOST_VM_INFO64_COUNT;
    struct vm_statistics64 stats;
    
    error = host_statistics64(mach_host_self(), HOST_VM_INFO64, (host_info64_t)&stats, &count);
    if (error != KERN_SUCCESS)
    {
        return GET_VM_STATS_ERROR;
    }
    
    buffer->active = stats.active_count * page_size;
    buffer->inactive = stats.inactive_count * page_size;
    buffer->wired = stats.wire_count * page_size;
    buffer->speculative = stats.speculative_count * page_size;
    buffer->free = stats.free_count * page_size;
    buffer->purgable = stats.purgeable_count * page_size;
    buffer->file_backed = stats.external_page_count * page_size;
    buffer->compressed = stats.compressor_page_count * page_size;
    buffer->internal = stats.internal_page_count * page_size;
    
    return KERN_SUCCESS;
}

int GetPeakWorkingSetSize(pid_t pid, uint64_t *buffer)
{
    rusage_info_current rusage;
    if (proc_pid_rusage(pid, RUSAGE_INFO_CURRENT, (void **)&rusage) != 0)
    {
        return GET_RUSAGE_ERROR;
    }
    
    *buffer = rusage.ri_lifetime_max_phys_footprint;
    
    return KERN_SUCCESS;
}
