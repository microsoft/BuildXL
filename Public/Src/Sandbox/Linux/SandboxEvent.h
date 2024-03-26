// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#ifndef BUILDXL_SANDBOX_LINUX_SANDBOX_EVENT_H
#define BUILDXL_SANDBOX_LINUX_SANDBOX_EVENT_H

#include <unistd.h>
#include <string>
#include <sys/stat.h>

namespace buildxl {
namespace linux {

typedef enum SandboxEventPathType {
    kAbsolutePaths,
    kRelativePaths,
    kFileDescriptors
} SandboxEventPathType;

class SandboxEvent {
private:
    es_event_type_t event_type_;
    // Describes the type of path that this SandboxEvent represents.
    SandboxEventPathType path_type_;
    // Relative or absolute paths
    std::string src_path_;
    std::string dst_path_;
    // File descriptor to src/dst paths or file descriptors for root directories for relative paths
    int src_fd_;
    int dst_fd_;
    pid_t pid_;
    pid_t child_pid_;
    mode_t mode_;
    uint error_;

    SandboxEvent(
        es_event_type_t event_type,
        const std::string& src_path,
        const std::string& dst_path,
        int src_fd,
        int dst_fd,
        pid_t pid,
        pid_t child_pid,
        uint error,
        SandboxEventPathType path_type) :
            event_type_(event_type),
            src_path_(src_path),
            dst_path_(dst_path),
            src_fd_(src_fd),
            dst_fd_(dst_fd),
            pid_(pid),
            child_pid_(child_pid),
            mode_(0),
            error_(error),
            path_type_(path_type) { }

public:
    /**
     * SandboxEvent for a fork/clone event.
     */
    static SandboxEvent ForkSandboxEvent(pid_t pid, pid_t child_pid, const std::string& path) {
        return SandboxEvent(
            /* event_type */ ES_EVENT_TYPE_NOTIFY_FORK,
            /* src_path */ path,
            /* dst_path */ "",
            /* src_fd */ -1,
            /* dst_fd */ -1,
            /* pid */ pid,
            /* child_pid */ child_pid,
            /* error */ 0,
            /* path_type */ SandboxEventPathType::kAbsolutePaths);
    }

    /**
     * SandboxEvent for paths.
     */
    static SandboxEvent AbsolutePathSandboxEvent(
        es_event_type_t event_type,
        pid_t pid,
        uint error,
        const std::string& src_path,
        const std::string& dst_path = "") {
        return SandboxEvent(
            /* event_type */ event_type,
            /* src_path */ src_path,
            /* dst_path */ dst_path,
            /* src_fd */ -1,
            /* dst_fd */ -1,
            /* pid */ pid,
            /* child_pid */ 0,
            /* error */ error,
            /* path_type */ SandboxEventPathType::kAbsolutePaths);
    }

    /**
     * SandboxEvent for a paths from a file descriptor.
     */
    static SandboxEvent FileDescriptorSandboxEvent(
        es_event_type_t event_type,
        pid_t pid,
        uint error,
        int src_fd,
        int dst_fd = -1) {
        return SandboxEvent(
            /* event_type */ event_type,
            /* src_path */ "",
            /* dst_path */ "",
            /* src_fd */ src_fd,
            /* dst_fd */ dst_fd,
            /* pid */ pid,
            /* child_pid */ 0,
            /* error */ error,
            /* path_type */ SandboxEventPathType::kFileDescriptors);
    }

    /**
     * SandboxEvent for a relative paths and FDs for their root directory.
     */
    static SandboxEvent RelativePathSandboxEvent(
        es_event_type_t event_type,
        pid_t pid,
        uint error,
        const std::string& src_path,
        int src_fd,
        const std::string& dst_path = "",
        int dst_fd = -1) {
        return SandboxEvent(
            /* event_type */ event_type,
            /* src_path */ src_path,
            /* dst_path */ dst_path,
            /* src_fd */ src_fd,
            /* dst_fd */ dst_fd,
            /* pid */ pid,
            /* child_pid */ 0,
            /* error */ error,
            /* path_type */ SandboxEventPathType::kRelativePaths);
    }

    // Getters
    pid_t GetPid() const { return pid_; }
    es_event_type_t GetEventType() const { return event_type_; }
    mode_t GetMode() const { return mode_; }
    const std::string& GetSrcPath() const { return src_path_; }
    const std::string& GetDstPath() const { return dst_path_; }
    int GetSrcFd() const { return src_fd_; }
    int GetDstFd() const { return dst_fd_; }
    uint GetError() const { return error_; }
    bool IsDirectory() const { return S_ISDIR(mode_); }
    SandboxEventPathType GetPathType() const { return path_type_; }

    // Setters
    void SetMode(mode_t mode) { mode_ = mode; }

    /**
     * Updates the source and destination paths to fully resolved paths.
     */
    void UpdatePaths(const std::string& src_path, const std::string& dst_path) {
        src_path_ = src_path;
        dst_path_ = dst_path;
        src_fd_ = -1;
        dst_fd_ = -1;
        path_type_ = SandboxEventPathType::kAbsolutePaths;
    }
};

} // namespace linux
} // namespace buildxl

#endif // BUILDXL_SANDBOX_LINUX_SANDBOX_EVENT_H