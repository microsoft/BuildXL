// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#ifndef BUILDXL_SANDBOX_LINUX_SANDBOX_EVENT_H
#define BUILDXL_SANDBOX_LINUX_SANDBOX_EVENT_H

#include <unistd.h>
#include <string>
#include <sys/stat.h>

#include "FileAccessHelpers.h"
#include "Operations.h"

namespace buildxl {
namespace linux {

enum class SandboxEventPathType {
    kAbsolutePaths,
    kRelativePaths,
    kFileDescriptors
};

// Indicates if this event is constructed with paths that still need resolution
typedef enum RequiredPathResolution {
    // Fully resolve the paths
    kFullyResolve,

    // Resolve intermediate directory symlinks, but not the final component of the path (basically, O_NOFOLLOW)
    kResolveNoFollow,

    // Do not resolve the paths in this event. 
    // We set for internally constructed events, or when we know the paths have already been resolved
    kDoNotResolve
} RequiredPathResolution;

/**
 * Contains all of the information required to send an access report for SandboxEvent.
*/
struct AccessReport {
    /** The FileOperation performed by this access report. */
    buildxl::linux::FileOperation file_operation;

    /** Relative or absolute path for the source path */
    std::string path;

    /** File descriptor to an absolute OR the root directory file descriptor for relative paths if path is not empty. */
    int fd;

    /** Access check result for provided path. */
    AccessCheckResult access_check_result;

    AccessReport() : file_operation(buildxl::linux::FileOperation::kMax), fd(-1), access_check_result(AccessCheckResult::Invalid()) {}

    // Copy Constructor
    AccessReport(const AccessReport& other) :
        file_operation(other.file_operation),
        path(other.path),
        fd(other.fd),
        access_check_result(other.access_check_result) { }
};

class SandboxEvent {
private:
    /** The system call that generated this event. */
    const char *system_call_;

    /** The type of event that this SandboxEvent represents. */
    buildxl::linux::EventType event_type_;

    /** Represents whether the path is a fully resolved or not. */
    SandboxEventPathType path_type_;

    /** The pid of the process that generated this event. On fork/clone events, this should be set to the pid of the newly created process. */
    pid_t pid_;

    /** Parent process ID of the process that generated this event. On fork/clone events, this should be set to the pid of the caller of fork/clone. */
    pid_t ppid_;

    /** Indicates if this event is constructed with paths that still need resolution. */
    RequiredPathResolution required_path_resolution_;

    /** Used only by fork/clone/exec events to include command line of processes that were created. */
    std::string command_line_;

    /** Mode for the source path */
    mode_t mode_;

    /** Optional errno for the system call */
    uint error_;

    /** Source Access Reports containing a path and an access check. */
    AccessReport source_access_report_;

    /** Destination Access Reports containing a path and an access check. */
    AccessReport destination_access_report_;

    /** Indicates whether logging was disabled for this event. */
    bool disable_logging_;

    /** Indicates whether this object represents a valid SandboxEvent. */
    bool is_valid_;

    /** Indicates whether this event can no longer be updated. */
    bool is_sealed_;
    
    /**
     * Empty constructor for an invalid event.
     */
    SandboxEvent() :
        is_valid_(false),
        is_sealed_(false)
    { /* Empty constructor for invalid object */ }

    /**
     * Default constructor
     */
    SandboxEvent(
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
        SandboxEventPathType path_type);

    /**
     * Creates an invalid SandboxEvent.
     */
    static inline SandboxEvent Invalid() {
        return SandboxEvent();
    }
public:
    /**
     * Copy Constructor
    */
    SandboxEvent(const SandboxEvent& other);
    
    /**
     * SandboxEvent for a fork/clone event.
     */
    static SandboxEvent CloneSandboxEvent(const char *system_call, pid_t pid, pid_t ppid, const char *path);

    /**
     * SandboxEvent for exec events.
     */
    static SandboxEvent ExecSandboxEvent(const char *system_call, pid_t pid, pid_t ppid, const char *path, std::string command_line);

    /**
     * SandboxEvent for an exit event.
     */
    static SandboxEvent ExitSandboxEvent(const char *system_call, std::string path, pid_t pid, pid_t ppid);

    /**
     * SandboxEvent for paths.
     */
    static SandboxEvent AbsolutePathSandboxEvent(
        const char *system_call,
        buildxl::linux::EventType event_type,
        pid_t pid,
        pid_t ppid,
        uint error,
        const char *src_path,
        const char *dst_path = "");

    /**
     * SandboxEvent for a paths from a file descriptor.
     */
    static SandboxEvent FileDescriptorSandboxEvent(
        const char *system_call,
        buildxl::linux::EventType event_type,
        pid_t pid,
        pid_t ppid,
        uint error,
        int src_fd,
        int dst_fd = -1);

    /**
     * SandboxEvent for a relative paths and FDs for their root directory.
     */
    static SandboxEvent RelativePathSandboxEvent(
        const char *system_call,
        buildxl::linux::EventType event_type,
        pid_t pid,
        pid_t ppid,
        uint error,
        const char *src_path,
        int src_fd,
        const char *dst_path = "",
        int dst_fd = -1);

    /* Getters */
    pid_t IsValid() const { return is_valid_; }
    const char *GetSystemCall() const { assert(is_valid_); return system_call_; }
    pid_t GetPid() const { assert(is_valid_); return pid_; }
    pid_t GetParentPid() const { assert(is_valid_); return ppid_; }
    buildxl::linux::EventType GetEventType() const { assert(is_valid_); return event_type_; }
    mode_t GetMode() const { assert(is_valid_); return mode_; }
    const std::string& GetSrcPath() const { assert(is_valid_); return source_access_report_.path; }
    const std::string& GetDstPath() const { assert(is_valid_); return destination_access_report_.path; }
    const std::string& GetCommandLine() const { assert(is_valid_); return command_line_; }
    int GetSrcFd() const { assert(is_valid_); return source_access_report_.fd; }
    int GetDstFd() const { assert(is_valid_); return destination_access_report_.fd; }
    const AccessReport GetSourceAccessReport() const { assert(is_valid_); return source_access_report_; }
    const AccessReport GetDestinationAccessReport() const { assert(is_valid_); return destination_access_report_; }
    uint GetError() const { assert(is_valid_); return error_; }
    SandboxEventPathType GetPathType() const { assert(is_valid_); return path_type_; }
    RequiredPathResolution GetRequiredPathResolution() const { assert(is_valid_); return required_path_resolution_; }
    AccessCheckResult GetSourceAccessCheckResult() const { assert(is_valid_); return source_access_report_.access_check_result; }
    AccessCheckResult GetDestinationAccessCheckResult() const { assert(is_valid_); return destination_access_report_.access_check_result; }
    bool IsLoggingDisabled() const { assert(is_valid_); return disable_logging_; }

    /* For debug logging */
    const char *DebugGetSystemCall() const { return system_call_; }

    /**
     * This access check represents the access check for this event as a whole (rather than as two separate accesses).
     * If a destination access check is set, then returns a combined access check, else returns the source access check.
     */
    AccessCheckResult GetEventAccessCheckResult() const {
        assert(is_valid_);

        if (!destination_access_report_.path.empty()) {
            return AccessCheckResult::Combine(source_access_report_.access_check_result, destination_access_report_.access_check_result);
        }

        return source_access_report_.access_check_result;
    }
    
    bool IsDirectory() const { assert(is_valid_); return S_ISDIR(mode_); }
    bool PathExists() const { assert(is_valid_); return mode_ != 0; }

    // Seal the event after constructing a report. This makes the event immutable.
    void Seal() { is_sealed_ = true; }

    // Setters
    void SetMode(mode_t mode) { assert(is_valid_); assert(!is_sealed_); mode_ = mode; }
    void SetRequiredPathResolution(RequiredPathResolution r) { assert(is_valid_); assert(!is_sealed_); required_path_resolution_ = r; }
    void SetSourceFileOperation(buildxl::linux::FileOperation file_operation) { assert(is_valid_); assert(!is_sealed_); source_access_report_.file_operation = file_operation; }
    void SetDestinationFileOperation(buildxl::linux::FileOperation file_operation) { assert(is_valid_); assert(!is_sealed_); destination_access_report_.file_operation = file_operation; }
    // SetErrno in particular does not check whether the event is sealed because the errno value is obtained after the system call is completed.
    void SetErrno(int error) { assert(is_valid_); error_ = error; }

    /**
     * Updates the source and destination paths to be absolute paths.
     */
    void SetResolvedPaths(const std::string& src_path, const std::string& dst_path);

    /**
     * Sets an access check result for the source/destination paths.
     */
    void SetSourceAccessCheck(AccessCheckResult check_result);
    void SetDestinationAccessCheck(AccessCheckResult check_result);

    /**
     * Disable logging for this event.
     */
    void DisableLogging();
};

} // namespace linux
} // namespace buildxl

#endif // BUILDXL_SANDBOX_LINUX_SANDBOX_EVENT_H