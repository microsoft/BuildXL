// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include "SyscallHandler.h"
#include "AccessChecker.h"

#define HANDLER_FUNCTION(syscallName) void SyscallHandler::MAKE_HANDLER_FN_NAME(syscallName) (BxlObserver *bxl, ebpf_event *event)

namespace buildxl {
namespace linux {
namespace ebpf {

SyscallHandler::SyscallHandler(BxlObserver *bxl, pid_t root_pid, pid_t runner_pid, const char* root_filename, std::atomic<EventRingBuffer *>* active_ringbuffer, int stats_per_pip_map_fd)
    : m_activePids(),
        m_root_pid(root_pid), 
        m_runner_pid(runner_pid), 
        m_bxl(bxl), 
        m_runnerExitSent(false), 
        m_root_filename(root_filename), 
        m_active_ringbuffer(active_ringbuffer), 
        m_stats_per_pip_map_fd(stats_per_pip_map_fd),
        m_lastPathsPerCPU(),
        m_bytesSavedIncremental(0),
        m_bytesSubmitted(0) {
    sem_init(&m_noActivePidsSemaphore, 0, 0);

    // Our managed side tracking expects a 'clone/fork' event before an exec in order to assign the right pids and update the active process collection. Doing
    // this on managed side is racy (since the pid to use will be available only after the root process has started and events may have arrived already)
    // Observe that we will see the exit event for the root process later, but we won't see the exit event for the runner process since it is not traced by ebpf.
    SendInitForkEvent(bxl, getpid(), getppid(), root_filename);
    SendInitForkEvent(bxl, root_pid, getpid(), root_filename);

    // This map will hold at most #CPUs entries, one for each CPU that has sent an event.
    m_lastPathsPerCPU.reserve(std::thread::hardware_concurrency());
}

SyscallHandler::~SyscallHandler() {
    sem_destroy(&m_noActivePidsSemaphore);
    // If we did not send the exit report for the runner process, we do it now.
    // This is to ensure that the managed side is aware of the exit of the root process, even if the runner has
    // an early unexpected exit.
    if (!m_runnerExitSent) {
        SendStats();
        m_bxl->SendExitReport(getpid(), getppid(), m_root_filename);
    }
 }

std::string SyscallHandler::DecodeIncrementalEvent(const ebpf_event* event) {
    std::string final_path;

    assert(event->metadata.event_type == SINGLE_PATH && "DecodeIncrementalEvent should only be called for single path events");

    unsigned short incremental_length = event->metadata.source_path_incremental_length;

    // Reconstruct the full path if this is an incremental event
    if (incremental_length > 0) {
        // Keep track of how many bytes we saved by using incremental paths, just for statistics purposes
        // To be strictly fair, the event metadata has a couple extra fields just to be able to reconstruct
        // the original paths on user side. So substract those, so we can detect the true savings.
        m_bytesSavedIncremental += incremental_length;
        m_bytesSavedIncremental -= sizeof(event->metadata.source_path_incremental_length);
        m_bytesSavedIncremental -= sizeof(event->metadata.processor_id);

        auto last_path = m_lastPathsPerCPU.find(event->metadata.processor_id);

        // If we have seen an event from this CPU before, use its last path to reconstruct the full path
        if (last_path != m_lastPathsPerCPU.end()) {
            // The new path is the concatenation of the prefix of the last path seen by this CPU (of length incremental_length)
            // and the new path sent by the event.
            final_path = std::string(last_path->second).substr(0, incremental_length) + event->src_path;        
        } else {
            assert(false && "Received an incremental event for a CPU that has not sent any events before. This should not happen.");
        }
    }
    // If this is not an incremental event, just use the path as is
    else {
        final_path = event->src_path;
    }

    // Update the last path for this CPU so that it can be used for future events.
    // This mimics what happens on kernel side, where the last path is updated for each CPU.
    m_lastPathsPerCPU[event->metadata.processor_id] = final_path;

    return final_path;
}

bool SyscallHandler::HandleSingleEvent(const ebpf_event *event) {
    // Track the total bytes submitted for this event
    m_bytesSubmitted += sizeof(ebpf_event_metadata) + strlen(event->src_path) + 1;
    
    std::string final_path = DecodeIncrementalEvent(event);

    // For some operations (e.g. memory files) our path translation returns an empty string. Those cases
    // should match with the ones we don't care about tracing. So do not send that event to managed side but
    // let the log debug event call above log it, so we can investigate otherwise.
    if (!IsPathFullyResolved(final_path)) {
        return false;
    }

    switch(event->metadata.operation_type) {
        case kClone:
        {
            auto sandboxEvent = SandboxEvent::CloneSandboxEvent(
                /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                /* pid */           event->metadata.child_pid,
                /* ppid */          event->metadata.pid,
                /* path */          final_path);
        
            // We have a single operation for now that can emit a kClone (wake_up_new_task), and this is unlikely to change, 
            // so do not bother checking IsEventCacheable
            CreateAndReportAccess(m_bxl, sandboxEvent, /* checkCache */ false);

            // Update the set of active pids to add the newly created child
            m_activePids.emplace(event->metadata.child_pid);

            break;
        }
        case kExit:
        {
            m_bxl->SendExitReport(event->metadata.pid, 0, final_path);

            // Update the set of active pids to remove the exiting pid
            RemovePid(event->metadata.pid);

            // If the exiting pid is the root pid, we also send a special exit report to indicate that the runner process has exited.
            // This is the symmetric to the first init fork event we sent on construction (the second init will have a regular
            // exit process observed, since that represents the root process of the pip and it is tracked).
            if (event->metadata.pid == m_root_pid) {
                SendStats();
                m_bxl->SendExitReport(getpid(), getppid(), m_root_filename);
                RemovePid(getpid());
                m_runnerExitSent = true;
            }

            break;
        }
        case kGenericWrite:
        {
            // The inode is being written. Send a special event to indicate this so file existence based policies can be applied downstream
            ReportFirstAllowWriteCheck(m_bxl, event->metadata.operation_type, final_path, event->metadata.mode, event->metadata.pid);

            auto sandboxEvent = SandboxEvent::AbsolutePathSandboxEvent(
                /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                /* event_type */    EventType::kGenericWrite,
                /* pid */           event->metadata.pid,
                /* ppid */          0,
                /* error */         0,
                /* src_path */      final_path);
            sandboxEvent.SetMode(event->metadata.mode);
            sandboxEvent.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);
            CreateAndReportAccess(m_bxl,sandboxEvent);

            break;
        }
        case kCreate:
        {
            // The inode is being created. Send a special event to indicate this so file existence based policies can be applied downstream
            ReportFirstAllowWriteCheck(m_bxl, event->metadata.operation_type, final_path, event->metadata.mode, event->metadata.pid);

            auto sandboxEvent = SandboxEvent::AbsolutePathSandboxEvent(
                /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                /* event_type */    EventType::kCreate,
                /* pid */           event->metadata.pid,
                /* ppid */          0,
                /* error */         0,
                /* src_path */      final_path);
            sandboxEvent.SetMode(event->metadata.mode);
            sandboxEvent.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);
            CreateAndReportAccess(m_bxl,sandboxEvent, /* check_cache */ IsEventCacheable(event));

            break;
        }
        case kUnlink: 
        {
            auto sandboxEvent = SandboxEvent::AbsolutePathSandboxEvent(
                /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                /* event_type */    EventType::kUnlink,
                /* pid */           event->metadata.pid,
                /* ppid */          0,
                /* error */         event->metadata.error * -1, // error is negative for rmdir
                /* src_path */      final_path);
            sandboxEvent.SetMode(event->metadata.mode);
            sandboxEvent.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);

            CreateAndReportAccess(m_bxl,sandboxEvent, /* check_cache */ IsEventCacheable(event));
            break;
        }
        case kGenericProbe:
        {
            auto sandboxEvent = SandboxEvent::AbsolutePathSandboxEvent(
                /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                /* event_type */    EventType::kGenericProbe,
                /* pid */           event->metadata.pid,
                /* ppid */          0,
                /* error */         abs(event->metadata.error), // Managed side always expect a non-negative number
                /* src_path */      final_path);
            sandboxEvent.SetMode(event->metadata.mode);
            sandboxEvent.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);

            CreateAndReportAccess(m_bxl,sandboxEvent);
            break;
        }
        case kGenericRead:
        {
            auto sandboxEvent = SandboxEvent::AbsolutePathSandboxEvent(
                /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                /* event_type */    EventType::kGenericRead,
                /* pid */           event->metadata.pid,
                /* ppid */          0,
                /* error */         0,
                /* src_path */      final_path);
            sandboxEvent.SetMode(event->metadata.mode);
            sandboxEvent.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);

            CreateAndReportAccess(m_bxl,sandboxEvent);
            break;
        }
        case kReadLink:
        {
            auto sandboxEvent = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
                /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                /* event_type */    EventType::kReadLink,
                /* pid */           event->metadata.pid,
                /* ppid */          0,
                /* error */         0,
                /* src_path */      final_path);
            sandboxEvent.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);
            // mode is explicitly not set here so that the BxlObserver can determine it.
            CreateAndReportAccess(m_bxl, sandboxEvent);
            break;

        }
        case kBreakAway:
        {
            m_bxl->SendBreakawayReport(final_path, event->metadata.pid, /** ppid */ 0);

            // A breakaway event means the process is no longer under our control, so we remove it from the active pids set.
            RemovePid(event->metadata.pid);
            break;
        }
        default:
            fprintf(stderr, "Unhandled operation type %d", event->metadata.operation_type);
            exit(1);
            break;
    }

    return true;
}

bool SyscallHandler::HandleDoubleEvent(const ebpf_event_double *event) {
    const char* src_path = get_src_path(event);
    const char* dst_path = get_dst_path(event);

    // Track the total bytes submitted for this event
    m_bytesSubmitted += sizeof(ebpf_event_metadata) + strlen(src_path) + 1 + strlen(dst_path) + 1;

    assert(event->metadata.source_path_incremental_length == 0 && "Incremental paths are not supported for double path events");

    // Same consideration for fully resolved paths as in the single path case
    if (!IsPathFullyResolved(src_path) || !IsPathFullyResolved(dst_path)) {
        return false;
    }

    switch (event->metadata.operation_type) {
        case kRename:
        {
            // Handling for this event is different based on whether it's a file or directory.
            // If a directory, the source directory no longer exists because the rename has already happened.
            // We can enumerate the destination directory instead.
            if (S_ISDIR(event->metadata.mode)) {
                std::vector<std::string> filesAndDirectories;
                std::string sourcePath(src_path);
                std::string destinationPath(dst_path);
                m_bxl->EnumerateDirectory(dst_path, /* recursive */ true, filesAndDirectories);

                for (auto fileOrDirectory : filesAndDirectories) {
                    // Destination
                    auto mode = m_bxl->get_mode(fileOrDirectory.c_str());

                    // Send this special event on creation, similar to what we do with a kCreate coming from EBPF
                    SandboxEvent firstAllowWriteDst;
                    ReportFirstAllowWriteCheck(m_bxl, kCreate, fileOrDirectory.c_str(), mode, event->metadata.pid);

                    auto sandboxEventDestination = SandboxEvent::AbsolutePathSandboxEvent(
                        /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                        /* event_type */    EventType::kCreate,
                        /* pid */           event->metadata.pid,
                        /* ppid */          0,
                        /* error */         0,
                        /* src_path */      fileOrDirectory.c_str());
                    sandboxEventDestination.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);
                    sandboxEventDestination.SetMode(mode);
                    CreateAndReportAccess(m_bxl, sandboxEventDestination, /* check cache */ true);

                    // Source
                    fileOrDirectory.replace(0, destinationPath.length(), sourcePath);

                    // Send this special event on write, similar to what we do with a kWrite coming from EBPF
                    ReportFirstAllowWriteCheck(m_bxl, kGenericWrite, fileOrDirectory.c_str(), 0, event->metadata.pid);
                    
                    auto sandboxEventSource = SandboxEvent::AbsolutePathSandboxEvent(
                        /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                        /* event_type */    EventType::kUnlink,
                        /* pid */           event->metadata.pid,
                        /* ppid */          0,
                        /* error */         0,
                        /* src_path */      fileOrDirectory.c_str());
                    // Sources should be absent now, infer the mode from the destination (in the end we care whether the path is a file or a directory)
                    sandboxEventSource.SetMode(mode);
                    sandboxEventSource.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);
                    CreateAndReportAccess(m_bxl, sandboxEventSource, /* check cache */ true);
                }
            }
            else {
                auto mode = m_bxl->get_mode(dst_path);
                // Source
                // Send this special event on write, similar to what we do with a kWrite coming from EBPF
                ReportFirstAllowWriteCheck(m_bxl, kGenericWrite, src_path, mode, event->metadata.pid);

                auto sandboxEventSource = SandboxEvent::AbsolutePathSandboxEvent(
                    /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                    /* event_type */    EventType::kUnlink,
                    /* pid */           event->metadata.pid,
                    /* ppid */          0,
                    /* error */         0,
                    /* src_path */      src_path);
                // Source should be absent now, infer the mode from the destination
                sandboxEventSource.SetMode(mode);
                sandboxEventSource.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);
                CreateAndReportAccess(m_bxl, sandboxEventSource, /* check cache */ true);

                // Destination
                // Send this special event on creation, similar to what we do with a kCreate coming from EBPF
                ReportFirstAllowWriteCheck(m_bxl, kCreate, dst_path, mode, event->metadata.pid);

                auto sandboxEventDestination = SandboxEvent::AbsolutePathSandboxEvent(
                    /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                    /* event_type */    EventType::kCreate,
                    /* pid */           event->metadata.pid,
                    /* ppid */          0,
                    /* error */         0,
                    /* src_path */      dst_path);
                sandboxEventDestination.SetMode(mode);
                sandboxEventDestination.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);
                CreateAndReportAccess(m_bxl, sandboxEventDestination, /* check cache */ true);
            }
            break;
        }
        
        default:
            fprintf(stderr, "Unhandled operation type %d", event->metadata.operation_type);
            exit(1);
            break;
    }

    return true;
}

bool SyscallHandler::HandleExecEvent(const ebpf_event_exec *event) {

    assert(event->metadata.source_path_incremental_length == 0 && "Incremental paths are not supported for exec events");

    const char* exe_path = get_exe_path(event);
    const char* args = get_args(event);

    // Track the total bytes submitted for this event
    m_bytesSubmitted += sizeof(ebpf_event_metadata) + strlen(exe_path) + 1 + strlen(args) + 1;

    auto sandboxEvent = SandboxEvent::ExecSandboxEvent(
        /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
        /* pid */           event->metadata.pid,
        /* ppid */          0,
        /* path */          exe_path,
        /* command_line */  m_bxl->IsReportingProcessArgs() ? args : "");
    CreateAndReportAccess(m_bxl,sandboxEvent, /* check_cache */ false);

    return true;
}

bool SyscallHandler::HandleDebugEvent(const ebpf_event_debug *event) {

    // Track the total bytes submitted for this event
    m_bytesSubmitted += sizeof(ebpf_event_debug);

    // Add the pip id (as seen by EBPF) to all debug messages
    char messageWithPipId[PATH_MAX];
    snprintf(messageWithPipId, PATH_MAX, "[%d] [%d] %s", event->runner_pid, event->pid, event->message);

    m_bxl->LogError(event->pid, messageWithPipId);
    return true;
}

bool SyscallHandler::IsEventCacheable(const ebpf_event *event)
{
    switch (event->metadata.kernel_function)
    {
        // We want to see every (successful) creation and deletion of directories on managed side
        // since we keep track of it for optimizing directory fingerprint computation
        case KERNEL_FUNCTION(do_rmdir):
        case KERNEL_FUNCTION(do_mkdirat):
        // We want to see every clone so we keep track of all created pids
        case KERNEL_FUNCTION(wake_up_new_task):
            return false;
        default:
            return true;
    }
}

void SyscallHandler::CreateAndReportAccess(BxlObserver *bxl, SandboxEvent& event, bool check_cache) 
{
    // With EBPF we always check the access report based on policy (and never on file existence)
    // The special event firstAllowWriteCheck that on Windows happen during write access check cannot
    // happen since the file creation happens on kernel side and sending this special event is not blocking the call.
    // Therefore, the special event (which carries the information of whether the file is present at the time the event is sent) is not accurate.
    // The special event firstAllowWriteCheck is only sent when creating a node (check HandleSingleEvent - kCreate case)
    bxl->CreateAndReportAccess(event, check_cache, /* basedOnPolicy */ true);
}

bool SyscallHandler::TryCreateFirstAllowWriteCheck(BxlObserver *bxl, operation_type operation_type, const std::string& path, mode_t mode, pid_t pid, SandboxEvent &event)
{
    // The inode is being created or is being written. event->metadata.operation_type is expected to be either a kWrite or a kCreate.
    assert(operation_type == kGenericWrite || operation_type == kCreate);

    // Send a special event to indicate this whenever OverrideAllowWriteForExistingFiles is on and the node is a regular file (we
    // don't send this event for directories)
    if (mode != 0 && !S_ISREG(mode))
    {
        return false;
    }

    auto policy = AccessChecker::PolicyForPath(bxl->GetFileAccessManifest(), path.c_str());

    // Register that we are sending this special event for the given path. If this is the first time we are seeing this path and
    // the operation is a kCreate, then the file was not there before the first write. Otherwise, if the operation is a kGenericWrite
    // the file was present.
    if (policy.OverrideAllowWriteForExistingFiles() && FilesCheckedForAccess::GetInstance()->TryRegisterPath(path))
    {
        mode_t final_mode = operation_type == kCreate
            // Observe the mode on event->metada.mode for the case of mknod indicates the mode of the file that
            // is about to be created. We don't want this, since when security_path_mknod is called, that's the
            // indicator the file was not there to begin with.
            ? 0
            // When the inode is being written, just send out the existing mode (which should be a regular file)
            : mode;

        bxl->create_firstAllowWriteCheck(path, final_mode, pid, /*ppid*/ 0, event);
        return true;
    }
    
    return false;
}

void SyscallHandler::ReportFirstAllowWriteCheck(BxlObserver *bxl, operation_type operation_type, const std::string& path, mode_t mode, pid_t pid)
{
    SandboxEvent event;

    if (TryCreateFirstAllowWriteCheck(bxl, operation_type, path, mode, pid, event))
    {
        bxl->SendReport(event);
    }
}

void SyscallHandler::SendInitForkEvent(BxlObserver* bxl, pid_t pid, pid_t ppid, const char *file)
{
    auto fork_event = buildxl::linux::SandboxEvent::CloneSandboxEvent(
        /* system_call */   "__init__fork",
        /* pid */           pid,
        /* ppid */          ppid,
        /* src_path */      file);
    fork_event.SetMode(bxl->get_mode(file));
    fork_event.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kDoNotResolve);
    bxl->CreateAndReportAccess(fork_event);
}

void SyscallHandler::SendStats()
{
    // Let's check whether we have stats for the pip
    pip_stats stats;
    int res = bpf_map_lookup_elem(m_stats_per_pip_map_fd, &m_runner_pid, &stats);
    // Best effort basis, if stats are not there, we just move on.
    if (res == 0)
    {
        double event_cache_hit_percentage = (stats.event_cache_hit + stats.event_cache_miss > 0) ? (100.0 * stats.event_cache_hit / (stats.event_cache_hit + stats.event_cache_miss)) : 0.0;

        m_bxl->LogInfo(
            getpid(),
            "[Ring buffer monitoring] Event cache hit: %d (%.2f%%), Event cache miss: %d",
            stats.event_cache_hit, event_cache_hit_percentage, stats.event_cache_miss);

        double string_cache_hit_percentage = (stats.string_cache_hit + stats.string_cache_miss > 0) ? (100.0 * stats.string_cache_hit / (stats.string_cache_hit + stats.string_cache_miss)) : 0.0;
        m_bxl->LogInfo(
            getpid(),
            "[Ring buffer monitoring] String cache hit: %d (%.2f%%), String cache miss: %d, String cache uncacheable: %d",
            stats.string_cache_hit, string_cache_hit_percentage, stats.string_cache_miss, stats.string_cache_uncacheable);

        m_bxl->LogInfo(
            getpid(),
            "[Ring buffer monitoring] Avoided sending to user side %d untracked accesses (%.2f KB)",
            stats.untracked_path_count, (double)stats.untracked_path_bytes / 1024.0);
    }

    auto eventRingbuffer = m_active_ringbuffer->load();
    size_t min_available = eventRingbuffer->GetMinimumAvailableSpace();
    size_t total = eventRingbuffer->GetRingBufferSize();
    double percent_available = (total > 0) ? (100.0 * min_available / total) : 0.0;

    // The buffer id is a 0-based index that gets increased every time a new buffer is created.
    // So the id also represents the number of times the ring buffer capacity has been exceeded.
    m_bxl->LogInfo(
        getpid(),
        "[Ring buffer monitoring] Minimum available space: %zu bytes (%.2f%%). Total available space: %zu bytes. Capacity exceeded %d time(s).",
        min_available, percent_available, total, eventRingbuffer->GetId());

    double percent_incremental_saved = (m_bytesSavedIncremental != 0) ? (100.0 * m_bytesSavedIncremental / (m_bytesSubmitted + m_bytesSavedIncremental)) : 0.0;
    m_bxl->LogInfo(
        getpid(),
        "[Ring buffer monitoring] Total bytes saved by using incremental path encoding: %.2f KB (%.2f%%). Total bytes sent: %.2f KB.",
        (double)m_bytesSavedIncremental / (1024.0), percent_incremental_saved, (double)m_bytesSubmitted / (1024.0));
}

void SyscallHandler::RemovePid(pid_t pid) {
    auto result = m_activePids.erase(pid);
    // If we removed the last active pid, signal that there are no more active pids
    if (m_activePids.empty()) {
        sem_post(&m_noActivePidsSemaphore);
    }
}

} // ebpf
} // linux
} // buildxl