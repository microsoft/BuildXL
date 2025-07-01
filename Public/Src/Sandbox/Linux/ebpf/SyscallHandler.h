// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#ifndef BUILDXL_SANDBOX_EBPF_SYSCALL_HANDLER_H
#define BUILDXL_SANDBOX_EBPF_SYSCALL_HANDLER_H

#include <cstdint>
#include "ebpfcommon.h"
#include "bxl_observer.hpp"
#include "SandboxEvent.h"
#include <semaphore.h>
#include <atomic>

#define MAKE_HANDLER_FN_NAME(syscallName) Handle##syscallName
#define MAKE_HANDLER_FN_DEF(syscallName) void MAKE_HANDLER_FN_NAME(syscallName) (BxlObserver *bxl, ebpf_event *event)

namespace buildxl {
namespace linux {
namespace ebpf {

class SyscallHandler {
public:
    // ring_buffer_min_available_space is periodically updated by the ring buffer monitoring thread in the runner
    SyscallHandler(BxlObserver* bxl, pid_t root_pid, const char* root_filename, std::atomic<size_t>* ring_buffer_min_available_space);
    ~SyscallHandler();
    bool HandleSingleEvent(const ebpf_event *event);
    bool HandleDoubleEvent(const ebpf_event_double *event);
    bool HandleDebugEvent(const ebpf_event_debug *event);
    bool HandleExecEvent(const ebpf_event_exec *event);
    
    /** Blocks until there are no more active processes or the timeout is hit
     * @param timeoutMs The maximum time to wait in milliseconds
     * @return 0 if there are no active processes, -1 if the timeout was hit, or an error code if sem_timedwait failed
    */
    int WaitForNoActiveProcesses(int timeoutMs) {
        struct timespec ts;
        clock_gettime(CLOCK_REALTIME, &ts);
        // Split timeoutMs into seconds and nanoseconds
        time_t seconds = timeoutMs / 1000;
        long nanoseconds = (timeoutMs % 1000) * 1000000L;

        ts.tv_sec += seconds;
        ts.tv_nsec += nanoseconds;
        if (ts.tv_nsec >= 1000000000L) {
            ts.tv_sec += 1;
            ts.tv_nsec -= 1000000000L;
        }
        
        return sem_timedwait(&m_noActivePidsSemaphore, &ts); 
    }

private:
    static bool IsEventCacheable(const ebpf_event *event);
    static void CreateAndReportAccess(BxlObserver *bxl, SandboxEvent& event, bool check_cache = true);
    static void ReportFirstAllowWriteCheck(BxlObserver *bxl, operation_type operation_type, const char *path, mode_t mode, pid_t pid);
    static bool TryCreateFirstAllowWriteCheck(BxlObserver *bxl, operation_type operation_type, const char *path, mode_t mode, pid_t pid, SandboxEvent &event);
    static void SendInitForkEvent(BxlObserver *bxl, pid_t pid, pid_t ppid, const char *file);
    // Sends the ring buffer minimum available space throughout the runner execution.
    // Heads up this should be sent before the runner exit event, otherwise the managed side may not be able to read it.
    void SendRingBufferStats();
    void RemovePid(pid_t pid);

    std::unordered_set<pid_t> m_activePids;
    pid_t m_root_pid;
    sem_t m_noActivePidsSemaphore;
    BxlObserver *m_bxl;
    bool m_runnerExitSent;
    const char* m_root_filename;
    std::atomic<size_t>* m_ring_buffer_min_available_space;
};

} // ebpf
} // linux
} // buildxl
    

#endif