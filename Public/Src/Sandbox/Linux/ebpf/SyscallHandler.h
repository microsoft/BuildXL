// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#ifndef BUILDXL_SANDBOX_EBPF_SYSCALL_HANDLER_H
#define BUILDXL_SANDBOX_EBPF_SYSCALL_HANDLER_H

#include <cstdint>
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
    static SyscallHandler* GetInstance();
    bool HandleSingleEvent(BxlObserver *bxl, const ebpf_event *event);
    static bool HandleDoubleEvent(BxlObserver *bxl, const ebpf_event_double *event);
    static bool HandleDebugEvent(BxlObserver *bxl, const ebpf_event_debug *event);
    static bool HandleExecEvent(BxlObserver *bxl, const ebpf_event_exec *event);
    
    /** 
     * Collection of pids (for the current pip) which are active (we saw a clone for them, but we still didn't see an exit)
     * Observe this collection can change while being inspected if the corresponding pip is still getting events
    */
    std::unordered_set<int>::const_iterator GetActivePidsBegin() { return m_activePids.cbegin(); }
    std::unordered_set<int>::const_iterator GetActivePidsEnd() { return m_activePids.cend(); }

private:
    SyscallHandler();
    static bool IsEventCacheable(const ebpf_event *event);
    static void CreateAndReportAccess(BxlObserver *bxl, SandboxEvent& event, bool check_cache = true);
    static void ReportFirstAllowWriteCheck(BxlObserver *bxl, operation_type operation_type, const char *path, mode_t mode, pid_t pid);
    static bool TryCreateFirstAllowWriteCheck(BxlObserver *bxl, operation_type operation_type, const char *path, mode_t mode, pid_t pid, SandboxEvent &event);

    std::unordered_set<pid_t> m_activePids;
};

} // ebpf
} // linux
} // buildxl
    

#endif