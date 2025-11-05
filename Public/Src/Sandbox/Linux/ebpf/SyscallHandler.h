// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#ifndef BUILDXL_SANDBOX_EBPF_SYSCALL_HANDLER_H
#define BUILDXL_SANDBOX_EBPF_SYSCALL_HANDLER_H

#include <cstdint>
#include <semaphore.h>
#include <atomic>
#include <thread>

#include "EventRingBuffer.hpp"
#include "ebpfcommon.h"
#include "bxl_observer.hpp"
#include "SandboxEvent.h"

#define MAKE_HANDLER_FN_NAME(syscallName) Handle##syscallName
#define MAKE_HANDLER_FN_DEF(syscallName) void MAKE_HANDLER_FN_NAME(syscallName) (BxlObserver *bxl, ebpf_event *event)

namespace buildxl {
namespace linux {
namespace ebpf {

class SyscallHandler {
public:
    // The active ring buffer is passed so we can log stats right after the last exit event is sent.
    SyscallHandler(
        BxlObserver* bxl, 
        pid_t root_pid,
        pid_t runner_pid,
        const char* root_filename, 
        std::atomic<buildxl::linux::ebpf::EventRingBuffer *>* active_ringbuffer,
        int stats_per_pip_map_fd);
    ~SyscallHandler();
    bool HandleSingleEvent(const ebpf_event *event);
    bool HandleSingleEvent(const ebpf_event_cpid *event);
    bool HandleDoubleEvent(const ebpf_event_double *event);
    bool HandleDebugEvent(const ebpf_event_debug *event);
    bool HandleExecEvent(const ebpf_event_exec *event);
    // When diagnostics are enabled, a diagnostics event is expected to arrive right before each actual event (for each CPU).
    bool HandleDiagnosticsEvent(const ebpf_diagnostics *event);
    
    void LogDebugEvent(ebpf_event *event);

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
        
        return sem_timedwait(&m_no_active_pids_semaphore, &ts);
    }

private:
    inline static bool IsEventCacheable(const ebpf_event *event);
    static void CreateAndReportAccess(BxlObserver *bxl, SandboxEvent& event, bool check_cache = true);
    static void ReportFirstAllowWriteCheck(BxlObserver *bxl, operation_type operation_type, const std::string& path, mode_t mode, pid_t pid);
    static bool TryCreateFirstAllowWriteCheck(BxlObserver *bxl, operation_type operation_type, const std::string& path, mode_t mode, pid_t pid, SandboxEvent &event);
    static void SendInitForkEvent(BxlObserver *bxl, pid_t pid, pid_t ppid, const char *file);
    /**
    * Whether a path is rooted (i.e. starts with a '/')
    */
    static bool IsPathRooted(const std::string& path) { return !path.empty() && path[0] == '/'; }

    /** 
     * Decodes an incremental event into a full path.
     */
    const std::string DecodeIncrementalEvent(const ebpf_event_metadata* metadata, const char* src_path, bool for_logging);

    /**
     * Internal handler for single path events.
     */
    bool HandleSingleEventInternal(const ebpf_event *event, pid_t child_pid, int error, std::string& final_path);

    /**
     * Resolves symlinks in the given path based on the specified resolution strategy.
     */
    void ResolveSymlinksIfNeeded(std::string &path, path_symlink_resolution resolution);

    /**
     * Retrieves the last diagnostics that arrived for a given CPU, if available.
     * Returns nullptr if no diagnostics are available for that CPU.
     */
    std::shared_ptr<ebpf_diagnostics> RetrieveDiagnosticsIfAvailable(const ebpf_event_metadata& metadata) const;

    /**
     * Similar to RetrieveDiagnosticsIfAvailable, but returns the kernel_function enum.
     * If no diagnostics are available for that CPU, returns KERNEL_FUNCTION(unknown).
     */
    inline kernel_function RetrieveKernelFunctionIfAvailable(const ebpf_event_metadata& metadata) const;
    
    /**
     * Converts an ebpf_mode to a mode_t.
     * Keep in sync with ebpfutilities.h::to_ebpf_mode
     */
    inline static mode_t FromEBPFMode(ebpf_mode mode);

    // Sends general stats of the runner execution.
    // Heads up this should be sent before the runner exit event, otherwise the managed side may not be able to read it.
    // TODO: For now this method just prints info messages on the bxl main log. Consider plumbing through this info via
    // ExecutionResult.PerformanceInformation / Logger.Log.ProcessPipExecutionInfo (so the event gets logged in the orchestrator
    // and general perf counters can also be surfaced properly)
    void SendStats();
    void RemovePid(pid_t pid);
    void InjectMessagesForTests();

    std::unordered_set<pid_t> m_active_pids;
    pid_t m_root_pid;
    pid_t m_runner_pid;
    sem_t m_no_active_pids_semaphore;
    BxlObserver *m_bxl;
    bool m_runner_exit_sent;
    const char* m_root_filename;
    std::atomic<EventRingBuffer *>* m_active_ringbuffer;
    int m_stats_per_pip_map_fd;
    std::unordered_map<int, std::string> m_last_paths_per_cpu;
    std::unordered_map<int, std::shared_ptr<ebpf_diagnostics>> m_diagnostics_per_cpu;
    long m_bytes_saved_incremental;
    long m_bytes_submitted;
    long m_event_count;
    // Let's count diagnostics stats separately
    long m_diagnostics_event_count;
    long m_diagnostics_bytes_submitted;
};

} // ebpf
} // linux
} // buildxl
    

#endif