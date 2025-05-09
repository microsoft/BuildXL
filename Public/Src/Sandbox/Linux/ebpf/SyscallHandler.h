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
    static bool HandleSingleEvent(BxlObserver *bxl, const ebpf_event *event);
    static bool HandleDoubleEvent(BxlObserver *bxl, const ebpf_event_double *event);
    static bool HandleDebugEvent(BxlObserver *bxl, const ebpf_event_debug *event);
    static bool HandleExecEvent(BxlObserver *bxl, const ebpf_event_exec *event);

    private:
    static bool IsEventCacheable(const ebpf_event *event);
    static void CreateAndReportAccess(BxlObserver *bxl, SandboxEvent& event, bool check_cache = true);
    static void ReportFirstAllowWriteCheck(BxlObserver *bxl, operation_type operation_type, const char *path, mode_t mode, pid_t pid);
    static bool TryCreateFirstAllowWriteCheck(BxlObserver *bxl, operation_type operation_type, const char *path, mode_t mode, pid_t pid, SandboxEvent &event);
};

} // ebpf
} // linux
} // buildxl
    

#endif