// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <dispatch/dispatch.h>
#include <inttypes.h>
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <sys/sysctl.h>
#include <unistd.h>

#include "process.h"

int GetProcessTimes(pid_t pid, ProcessTimesInfo *buffer, long bufferSize, bool includeChildProcesses)
{
    if (sizeof(ProcessTimesInfo) != bufferSize)
    {
        printf("ERROR: Wrong size of ProcessTimesInfo buffer; expected %ld, received %ld\n", sizeof(ProcessTimesInfo), bufferSize);
        return GET_RUSAGE_ERROR;
    }

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
    double factor = (((double)numer) / denom) / NSEC_PER_SEC;

    buffer->startTime = ((long)rusage.ri_proc_start_abstime - (long)absoluteTime) * factor;

    buffer->exitTime = rusage.ri_proc_exit_abstime != 0 ?
        (((long)rusage.ri_proc_exit_abstime - (long)absoluteTime) * factor) : 0;

    buffer->systemTime = rusage.ri_system_time;
    buffer->userTime = rusage.ri_user_time;

    if (includeChildProcesses)
    {
        buffer->systemTime += rusage.ri_child_system_time;
        buffer->userTime += rusage.ri_child_user_time;
    }

    return KERN_SUCCESS;
}

static CoreDumpConfiguration *dump_config = NULL;

static bool AdjustCoreDumpSizeResourceLimit(unsigned long long limit)
{
    struct rlimit coreLimit;

    coreLimit.rlim_cur = limit;
    coreLimit.rlim_max = limit;

    return setrlimit(RLIMIT_CORE, &coreLimit) == 0;
}

void TeardownProcessDumpsInternal(bool cleanExit)
{
    // Try disabling automatic process core dump creation on normal exit
    if (cleanExit) AdjustCoreDumpSizeResourceLimit(0);

    if (dump_config != NULL && dump_config->outputPath != NULL)
    {
        free(dump_config->outputPath);
        dump_config->outputPath = NULL;
    }

    if (dump_config != NULL)
    {
        free(dump_config);
        dump_config = NULL;
    }
}

void TeardownProcessDumps()
{
    TeardownProcessDumpsInternal(true);
}

void DumpThreadState()
{
    if (dump_config == NULL)
    {
        return;
    }

    do
    {
        thread_array_t threadList;
        mach_msg_type_number_t threadCount;
        mach_port_t port = mach_task_self();

        kern_return_t result = task_threads(port, &threadList, &threadCount);
        if (result != KERN_SUCCESS)
        {
            break;
        }

        FILE *outputFile = fopen(dump_config->outputPath, "w");

        char output_path[1024] = { '\0' };
        FILE *sysOutputFile = sprintf(output_path, KERN_COREFILE_DEFAULT_PATH, getpid()) > 0 ? fopen(output_path, "w") : NULL;

        if (outputFile == NULL)
        {
            goto freeThreadList;
        }

        for (int i = 0; i < threadCount; i++)
        {
            thread_identifier_info_data_t threadInfo;
            mach_msg_type_number_t identifierCount = THREAD_IDENTIFIER_INFO_COUNT;

            result = thread_info(threadList[i], THREAD_IDENTIFIER_INFO, (thread_info_t) &threadInfo, &identifierCount);
            if(result == KERN_SUCCESS)
            {
                fprintf(outputFile, "setsostid %" PRIXLEAST64 " %x\n", threadInfo.thread_id, (i + 1));

                if (sysOutputFile != NULL)
                {
                    fprintf(sysOutputFile, "setsostid %" PRIXLEAST64 " %x\n", threadInfo.thread_id, (i + 1));
                }
            }
        }

        fclose(outputFile);
        if (sysOutputFile != NULL) fclose(sysOutputFile);

    freeThreadList:

        for (int i = 0; i < threadCount; i++)
        {
            mach_port_deallocate(port, threadList[i]);
        }

        vm_deallocate(port, (vm_address_t) threadList, threadCount * sizeof(thread_act_t));
    }
    while(false);
}

static void sigCrashHandler(int sig)
{
    DumpThreadState();
    TeardownProcessDumpsInternal(false);

    // Restore defaults and raise same signal again to get OS default handling e.g. crash report write out etc. after
    // we have got our thread mapping and cleaned up some book keeping.
    signal(sig, SIG_DFL);
    raise(sig);
}

bool CheckIfCoreDumpPathIsAccessible(char *path, size_t len)
{
    // Find the first '/' as the core dump path is a formatted expression, e.g. /cores/%N.%P and extract the path
    char *search = &path[len-1];
    while(path != search && *(search - 1) != '/') search--;

    // Path is malformed
    if (path == search) return false;

    // Adjust path to end after the last '/', dropping the format specifier
    *search = '\0';

    return access(path, R_OK) == 0;
}

bool SetupProcessDumps(const char *logsDirectory, /*out*/ char *buffer, size_t bufsiz)
{
    // Try enabling automatic process core dump creation
    if (AdjustCoreDumpSizeResourceLimit(RLIM_INFINITY))
    {
        do
        {
            dump_config = (CoreDumpConfiguration *) malloc(sizeof(CoreDumpConfiguration));
            if (dump_config == NULL)
            {
                break;
            }

            size_t outputPathLength = strlen(logsDirectory) + strlen(THREAD_TID_MAPPING_FILE) + 2; // '/' + '\0'
            dump_config->outputPath = calloc(outputPathLength, sizeof(char));
            if (dump_config->outputPath == NULL)
            {
                break;
            }

            if (snprintf(dump_config->outputPath, outputPathLength, "%s/%s", logsDirectory, THREAD_TID_MAPPING_FILE) < 0)
            {
                break;
            }

            // Install signal handlers for the all unexpected failure conditions of interest, this helps debugging
            // unexpected errors and crashes that can't be caught by the CoreCLR
            RegisterSignalHandlers();
            
            size_t len;
            if (sysctlbyname(SYSCTL_KERN_COREFILE, NULL, &len, NULL, 0) != 0 || len > bufsiz)
            {
                break;
            }

            if (sysctlbyname(SYSCTL_KERN_COREFILE, buffer, &len, NULL, 0) != 0)
            {
                break;
            }

            return CheckIfCoreDumpPathIsAccessible(buffer, len);
        }
        while(false);

        TeardownProcessDumps();
    }

    return false;
}

void RegisterSignalHandlers()
{
    // Ignore default signal handlers for the signals we are interested in
    struct sigaction action = { 0 };
    action.sa_handler = SIG_IGN;

    int signals[] = { SIGBUS, SIGILL, SIGHUP, SIGABRT, SIGSEGV };
    int signalsLength = sizeof(signals) / sizeof(signals[0]);

    for (int i = 0; i < signalsLength; i++)
    {
        int sig = signals[i];
        sigaction(sig, &action, NULL);

        dispatch_source_t source = dispatch_source_create(DISPATCH_SOURCE_TYPE_SIGNAL, sig, 0, dispatch_get_global_queue(0, 0));
        dispatch_source_set_event_handler(source, ^()
        {
            sigCrashHandler(sig);
        });

        dispatch_resume(source);
    }
}
