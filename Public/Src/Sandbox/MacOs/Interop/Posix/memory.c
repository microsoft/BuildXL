// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "memory.h"

int GetRamUsageInfo(RamUsageInfo *buffer, long bufferSize)
{
    if (sizeof(RamUsageInfo) != bufferSize)
    {
        printf("ERROR: Wrong size of RamUsageInfo buffer; expected %ld, received %ld\n", sizeof(RamUsageInfo), bufferSize);
        return RUNTIME_ERROR;
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

static uint64_t ProcessTreeResidentSize(pid_t pid, const rlim_t max_proc_count, bool *success)
{
    uint64_t memory_usage = 0;
    rusage_info_current rusage;
    *success &= proc_pid_rusage(pid, RUSAGE_INFO_CURRENT, (void **)&rusage) == 0;

    if (*success)
    {
        memory_usage += rusage.ri_resident_size;

        pid_t child_pids[max_proc_count];
        int child_count = proc_listchildpids(pid, child_pids, (int) max_proc_count);
        *success &= child_count >= 0;

        for (int i = 0; (i < child_count) && *success; i++)
        {
            int child_pid = child_pids[i];
            memory_usage += ProcessTreeResidentSize(child_pid, max_proc_count, success);
        }
    }

    return memory_usage;
}

int GetPeakWorkingSetSize(pid_t pid, uint64_t *buffer)
{
    struct rlimit rl;
    bool success = getrlimit(RLIMIT_NPROC, &rl) == 0;

    // We look at the resident size for the complete process tree because we care about physical memory consumption and
    // not about the overall value which is skewed by factors like compressed memory and others. Logic that does
    // resource based cancelation of pips in BuildXL (see ProcessResourceManager.cs) does calculations against the total
    // available system memory and the reported value from this invocation.

    uint64_t mem_usage = ProcessTreeResidentSize(pid, rl.rlim_cur, &success);
    if (!success)
    {
        return RUNTIME_ERROR;
    }

    *buffer = mem_usage;
    return KERN_SUCCESS;
}

int GetMemoryPressureLevel(int *level)
{
    size_t length = sizeof(int);
    return sysctlbyname("kern.memorystatus_vm_pressure_level", level, &length, NULL, 0);
}
