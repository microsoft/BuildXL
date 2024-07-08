// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include <assert.h>
#include <fcntl.h>
#include <tuple>

#include "SandboxEvent.h"

namespace buildxl {
namespace linux {

SandboxEvent::SandboxEvent(
    const char *system_call,
    buildxl::linux::EventType event_type,
    const std::string& src_path,
    const std::string& dst_path,
    int src_fd,
    int dst_fd,
    pid_t pid,
    pid_t ppid,
    std::string command_line,
    uint error,
    SandboxEventPathType path_type) :
        event_type_(event_type),
        command_line_(command_line),
        mode_(0),
        error_(error),
        path_type_(path_type),
        required_path_resolution_(RequiredPathResolution::kFullyResolve),
        is_valid_(true),
        is_sealed_(false) {

        system_call_ = system_call;

        // This isn't supposed to happen, but we're seeing it happen with some ptraced processes.
        // For now this mimics behaviour from the old sandbox where we call getpid() to get the pid (even though this might not be the right pid).
        // Bug #2188144
        pid_ = pid <= 0 ? getpid() : pid;
        ppid_ = ppid < 0 ? getppid() : ppid; // ppid can be 0, so we only check for a negative value.

        source_access_report_.path = src_path;
        source_access_report_.fd = src_fd;

        destination_access_report_.path = dst_path;
        destination_access_report_.fd = dst_fd;

        // The following events we can classify immediately.
        // Others will be classified when the access check is performed.
        switch (event_type_) {
            case buildxl::linux::EventType::kProcess:
                source_access_report_.file_operation = buildxl::linux::FileOperation::kProcess;
                break;
            case buildxl::linux::EventType::kExec:
                source_access_report_.file_operation = buildxl::linux::FileOperation::kProcessExec;
                break;
            case buildxl::linux::EventType::kExit:
                source_access_report_.file_operation = buildxl::linux::FileOperation::kProcessExit;
                break;
            case buildxl::linux::EventType::kFirstAllowWriteCheckInProcess:
                source_access_report_.file_operation = buildxl::linux::FileOperation::kFirstAllowWriteCheckInProcess;
                break;
            case buildxl::linux::EventType::kPTrace:
                source_access_report_.file_operation = buildxl::linux::FileOperation::kProcessRequiresPtrace;
                break;
            default:
                // These cases require mode to be set with a resolved path before they can be classfied
                // This will happen when the access check is performed.
                break;
        }
    }

SandboxEvent::SandboxEvent(const SandboxEvent& other) :
    system_call_(other.system_call_),
    event_type_(other.event_type_),
    pid_(other.pid_),
    ppid_(other.ppid_),
    command_line_(other.command_line_),
    mode_(other.mode_),
    error_(other.error_),
    path_type_(other.path_type_),
    required_path_resolution_(other.required_path_resolution_),
    is_valid_(other.is_valid_),
    is_sealed_(other.is_sealed_),
    source_access_report_(other.source_access_report_),
    destination_access_report_(other.destination_access_report_) {
}

// Static Constructors
SandboxEvent SandboxEvent::ForkSandboxEvent(const char *system_call, pid_t pid, pid_t ppid, const char *path) {
    auto event = SandboxEvent(
        /* system_call */ system_call,
        /* event_type */ buildxl::linux::EventType::kProcess,
        /* src_path */ path,
        /* dst_path */ "",
        /* src_fd */ -1,
        /* dst_fd */ -1,
        /* pid */ pid,
        /* ppid */ ppid,
        /* command_line */ "",
        /* error */ 0,
        /* path_type */ SandboxEventPathType::kAbsolutePaths);
    event.SetSourceAccessCheck(AccessCheckResult(RequestedAccess::Read, ResultAction::Allow, ReportLevel::Report));

    return event;
}

SandboxEvent SandboxEvent::ExecSandboxEvent(const char *system_call, pid_t pid, const char *path, std::string command_line) {
    if (path == nullptr) {
        return SandboxEvent::Invalid();
    }
    
    auto event = SandboxEvent(
        /* system_call */ system_call,
        /* event_type */ buildxl::linux::EventType::kExec,
        /* src_path */ path,
        /* dst_path */ "",
        /* src_fd */ -1,
        /* dst_fd */ -1,
        /* pid */ pid,
        /* ppid */ 0,
        /* command_line */ command_line,
        /* error */ 0,
        /* path_type */ path[0] != '/' ? SandboxEventPathType::kRelativePaths : SandboxEventPathType::kAbsolutePaths);
    event.SetSourceAccessCheck(AccessCheckResult(RequestedAccess::None, ResultAction::Allow, ReportLevel::Report));

    return event;
}

SandboxEvent SandboxEvent::ExitSandboxEvent(const char *system_call, std::string path, pid_t pid) {
    auto event = SandboxEvent(
        /* system_call */ system_call,
        /* event_type */ buildxl::linux::EventType::kExit,
        /* src_path */ path,
        /* dst_path */ "",
        /* src_fd */ -1,
        /* dst_fd */ -1,
        /* pid */ pid,
        /* ppid */ pid,
        /* command_line */ "",
        /* error */ 0,
        /* path_type */ SandboxEventPathType::kAbsolutePaths);
    event.SetSourceAccessCheck(AccessCheckResult(RequestedAccess::None, ResultAction::Allow, ReportLevel::Report));
    event.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);

    return event;
}

SandboxEvent SandboxEvent::AbsolutePathSandboxEvent(
    const char *system_call,
    buildxl::linux::EventType event_type,
    pid_t pid,
    uint error,
    const char *src_path,
    const char *dst_path) {
    if (src_path == nullptr || dst_path == nullptr) {
        return SandboxEvent::Invalid();
    }
    // If the path isn't rooted, then it isn't an absolute path.
    // We will treat this as a relative path from the current working directory.
    // The source path cannot be empty, but the dst path can be empty if a dst path is never passed in and the default value is used.
    bool is_src_relative = src_path[0] == '\0' || src_path[0] != '/';
    bool is_dst_relative = dst_path[0] != '\0' && dst_path[0] != '/';

    if (is_src_relative || is_dst_relative) {
        return RelativePathSandboxEvent(
            system_call,
            event_type,
            pid,
            error,
            src_path,
            is_src_relative ? AT_FDCWD : -1,
            dst_path,
            is_dst_relative ? AT_FDCWD : -1);
    }

    return SandboxEvent(
        /* system_call */ system_call,
        /* event_type */ event_type,
        /* src_path */ src_path,
        /* dst_path */ dst_path,
        /* src_fd */ -1,
        /* dst_fd */ -1,
        /* pid */ pid,
        /* ppid */ 0,
        /* command_line */ "",
        /* error */ error,
        /* path_type */ SandboxEventPathType::kAbsolutePaths);
}

SandboxEvent SandboxEvent::FileDescriptorSandboxEvent(
    const char *system_call,
    buildxl::linux::EventType event_type,
    pid_t pid,
    uint error,
    int src_fd,
    int dst_fd) {
    return SandboxEvent(
        /* system_call */ system_call,
        /* event_type */ event_type,
        /* src_path */ "",
        /* dst_path */ "",
        /* src_fd */ src_fd,
        /* dst_fd */ dst_fd,
        /* pid */ pid,
        /* ppid */ 0,
        /* command_line */ "",
        /* error */ error,
        /* path_type */ SandboxEventPathType::kFileDescriptors);
}

SandboxEvent SandboxEvent::RelativePathSandboxEvent(
    const char *system_call,
    buildxl::linux::EventType event_type,
    pid_t pid,
    uint error,
    const char *src_path,
    int src_fd,
    const char *dst_path,
    int dst_fd) {
    if (src_path == nullptr || dst_path == nullptr) {
        return SandboxEvent::Invalid();
    }

    return SandboxEvent(
        /* system_call */ system_call,
        /* event_type */ event_type,
        /* src_path */ src_path,
        /* dst_path */ dst_path,
        /* src_fd */ src_fd,
        /* dst_fd */ dst_fd,
        /* pid */ pid,
        /* ppid */ 0,
        /* command_line */ "",
        /* error */ error,
        /* path_type */ SandboxEventPathType::kRelativePaths);
}

/* Setters */
void SandboxEvent::SetResolvedPaths(const std::string& src_path, const std::string& dst_path) {
    assert(is_valid_);
    assert(!is_sealed_);

    source_access_report_.path = src_path;
    destination_access_report_.path = dst_path;
    source_access_report_.fd = -1;
    destination_access_report_.fd = -1;
    required_path_resolution_ = RequiredPathResolution::kDoNotResolve; // Prevent the paths from being normalized again 
    path_type_ = SandboxEventPathType::kAbsolutePaths;
}

void SandboxEvent::SetSourceAccessCheck(AccessCheckResult check_result) {
    assert(is_valid_);
    assert(!is_sealed_);

    source_access_report_.access_check_result = check_result;
}

void SandboxEvent::SetDestinationAccessCheck(AccessCheckResult check_result) {
    assert(is_valid_);
    assert(!is_sealed_);

    destination_access_report_.access_check_result = check_result;
}

}  // namespace linux
}  // namespace buildxl