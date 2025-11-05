// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include <filesystem>
#include "SyscallHandler.h"
#include "AccessChecker.h"

#define HANDLER_FUNCTION(syscallName) void SyscallHandler::MAKE_HANDLER_FN_NAME(syscallName) (BxlObserver *bxl, ebpf_event *event)

namespace buildxl {
namespace linux {
namespace ebpf {

SyscallHandler::SyscallHandler(BxlObserver *bxl, pid_t root_pid, pid_t runner_pid, const char* root_filename, std::atomic<EventRingBuffer *>* active_ringbuffer, int stats_per_pip_map_fd)
    : m_active_pids(),
        m_root_pid(root_pid), 
        m_runner_pid(runner_pid), 
        m_bxl(bxl), 
        m_runner_exit_sent(false), 
        m_root_filename(root_filename), 
        m_active_ringbuffer(active_ringbuffer), 
        m_stats_per_pip_map_fd(stats_per_pip_map_fd),
        m_last_paths_per_cpu(),
        m_bytes_saved_incremental(0),
        m_bytes_submitted(0),
        m_event_count(0),
        m_diagnostics_event_count(0),
        m_diagnostics_bytes_submitted(0) {
    sem_init(&m_no_active_pids_semaphore, 0, 0);

    // Our managed side tracking expects a 'clone/fork' event before an exec in order to assign the right pids and update the active process collection. Doing
    // this on managed side is racy (since the pid to use will be available only after the root process has started and events may have arrived already)
    // Observe that we will see the exit event for the root process later, but we won't see the exit event for the runner process since it is not traced by ebpf.
    SendInitForkEvent(bxl, getpid(), getppid(), root_filename);
    SendInitForkEvent(bxl, root_pid, getpid(), root_filename);

    // This map will hold at most #CPUs entries, one for each CPU that has sent an event.
    m_last_paths_per_cpu.reserve(std::thread::hardware_concurrency());

    if (m_bxl->LogDebugEnabled()) {
        // When diagnostics are enabled, we also reserve the diagnostics per CPU map
        // A diagnostic event should arrive for every event right before the actual event (before in a CPU-ordered way).
        m_diagnostics_per_cpu.reserve(std::thread::hardware_concurrency());
    }

    // For testing only
    InjectMessagesForTests();
}

SyscallHandler::~SyscallHandler() {
    sem_destroy(&m_no_active_pids_semaphore);
    // If we did not send the exit report for the runner process, we do it now.
    // This is to ensure that the managed side is aware of the exit of the root process, even if the runner has
    // an early unexpected exit.
    if (!m_runner_exit_sent) {
        SendStats();
        m_bxl->SendExitReport(getpid(), getppid(), m_root_filename);
    }
}

kernel_function SyscallHandler::RetrieveKernelFunctionIfAvailable(const ebpf_event_metadata& metadata) const {
    std::shared_ptr<ebpf_diagnostics> diagnostics = RetrieveDiagnosticsIfAvailable(metadata);
    
    return diagnostics != nullptr 
        ? diagnostics->kernel_function 
        : kernel_function::KERNEL_unknown;
}

std::shared_ptr<ebpf_diagnostics> SyscallHandler::RetrieveDiagnosticsIfAvailable(const ebpf_event_metadata& metadata) const {
    if (m_bxl->LogDebugEnabled()) {
        auto it = m_diagnostics_per_cpu.find(metadata.processor_id);
        if (it != m_diagnostics_per_cpu.end()) {
            return it->second;
        }
    }

    return nullptr;
}

void SyscallHandler::ResolveSymlinksIfNeeded(std::string &path, path_symlink_resolution resolution) {
    switch (resolution) {
        case noResolve:
            // nothing to do, the path should be used as is
            break;
        case fullyResolve: {
            // Fully resolve the path. The path shouldn't contain any . or .. components at this point (which weakly_canonical would resolve too),
            // but what we are interested in is resolving any symlinks in the path. The path should also point to an existing file by design, but
            // we err on the side of caution and use weakly_canonical which will return a path even if the final file doesn't exist.
            std::error_code ec;
            auto resolved_path = std::filesystem::weakly_canonical(path, ec).string();
            if (ec.value() == 0) {
                path = resolved_path;
            }
            // If we failed to fully resolve the path, just keep the original path
            
            break;
        }
        case resolveIntermediates: {
            std::filesystem::path p(path);
            if (p.has_parent_path()) {
                std::error_code ec;
                auto parent = std::filesystem::weakly_canonical(p.parent_path(), ec);
                if (ec.value() == 0) {
                    path = (parent / p.filename()).string();
                }
                // If we failed to resolve the parent path, just keep the original path
            }
            // If there's no parent path (e.g., just a filename), nothing to resolve
            
            break;
        }
        default:
            assert(false && "Unknown symlink resolution type");
            break;
    }
}

const std::string SyscallHandler::DecodeIncrementalEvent(const ebpf_event_metadata* metadata, const char* src_path, bool for_logging) {
    std::string final_path;

    unsigned short incremental_length = metadata->source_path_incremental_length;

    // Reconstruct the full path if this is an incremental event
    if (incremental_length > 0) {
        // We don't count bytes saved when logging for debug purposes
        if (!for_logging) {
            // Keep track of how many bytes we saved by using incremental paths, just for statistics purposes
            // To be strictly fair, the event metadata has a couple extra fields just to be able to reconstruct
            // the original paths on user side. So substract those, so we can detect the true savings.
            m_bytes_saved_incremental += incremental_length;
            m_bytes_saved_incremental -= sizeof(metadata->source_path_incremental_length);
            m_bytes_saved_incremental -= sizeof(metadata->processor_id);
        }

        auto last_path = m_last_paths_per_cpu.find(metadata->processor_id);

        // If we have seen an event from this CPU before, use its last path to reconstruct the full path
        if (last_path != m_last_paths_per_cpu.end()) {
            // The new path is the concatenation of the prefix of the last path seen by this CPU (of length incremental_length)
            // and the new path sent by the event.
            final_path = std::string(last_path->second).substr(0, incremental_length) + src_path;
        } else {
            assert(false && "Received an incremental event for a CPU that has not sent any events before. This should not happen.");
        }
    }
    // If this is not an incremental event, just use the path as is
    else {
        final_path = src_path;
    }

    // If we are just logging for debug purposes, do not update the last path for this CPU
    if (!for_logging) {
        // Update the last path for this CPU so that it can be used for future events.
        // This mimics what happens on kernel side, where the last path is updated for each CPU.
        m_last_paths_per_cpu[metadata->processor_id] = final_path;
    }

    return final_path;
}

void SyscallHandler::InjectMessagesForTests() {
    // If the __BUILDXL_TEST_INJECTINFRAERROR environment variable is set, we inject an infra error event to test the managed side handling of infra errors.
    const char* injectInfraError = getenv(BxlInjectInfraError);
    if (injectInfraError && strcmp(injectInfraError, "1") == 0) {
        m_bxl->LogError(getpid(), "Injected infrastructure error for testing purposes", -1);
    }
}

mode_t SyscallHandler::FromEBPFMode(ebpf_mode mode) {
    if (mode == UNKNOWN_MODE) {
        return 0;
    }

    // Just hardcode this case to something that is not a regular file, directory or symlink
    if (mode == OTHER) {
        return S_IFIFO;
    }

    mode_t result = 0;
    if (mode & REGULAR_FILE) {
        result |= S_IFREG;
    }
    if (mode & DIRECTORY) {
        result |= S_IFDIR;
    }
    if (mode & SYMLINK) {
        result |= S_IFLNK;
    }

    return result;
}

bool SyscallHandler::HandleDiagnosticsEvent(const ebpf_diagnostics *event) {    
    shared_ptr<ebpf_diagnostics> diagnostics;
    m_diagnostics_event_count++;
    m_diagnostics_bytes_submitted += sizeof(ebpf_diagnostics);

    // If there is already diagnostics info for this CPU, we overwrite it
    auto it = m_diagnostics_per_cpu.find(event->processor_id);
    if (it != m_diagnostics_per_cpu.end()) {
        diagnostics = it->second;
    }
    else {
        // Otherwise, create a new diagnostics struct
        diagnostics = std::make_shared<ebpf_diagnostics>();
    }

    // Copy the data. The original event is going to be freed after this call returns and we need to keep the data around
    diagnostics->event_type = event->event_type;
    diagnostics->processor_id = event->processor_id;
    diagnostics->kernel_function = event->kernel_function;
    diagnostics->available_data_to_consume = event->available_data_to_consume;

    // Store the diagnostics information per CPU
    m_diagnostics_per_cpu[event->processor_id] = diagnostics;

    return true;
}

bool SyscallHandler::HandleSingleEvent(const ebpf_event *event) {
    // Track the total bytes submitted for this event
    m_bytes_submitted += sizeof(ebpf_event_metadata) + strlen(event->src_path) + 1;
    m_event_count++;

    std::string final_path = DecodeIncrementalEvent(&(event->metadata), event->src_path, /* forLogging */ false);

    // We make any error map to ENOENT, just to save space on the event structure. Managed side only cares about
    // whether there was an error (error != 0) and in some cases whether the error was ENOENT specifically.
    int error = event->metadata.event_type == ebpf_event_type::SINGLE_PATH_WITH_ERROR
        ? ENOENT
        : 0;

    return HandleSingleEventInternal((const ebpf_event*) event, /* child_pid */ 0, error, final_path);
}

bool SyscallHandler::HandleSingleEvent(const ebpf_event_cpid *event) {
    // Track the total bytes submitted for this event
    m_bytes_submitted += sizeof(ebpf_event_metadata) + sizeof(pid_t) + strlen(event->src_path) + 1;
    m_event_count++;

    std::string final_path = DecodeIncrementalEvent(&(event->metadata), event->src_path, /* forLogging */ false);

    return HandleSingleEventInternal((const ebpf_event*) event, event->child_pid, /* error */0, final_path);
}

bool SyscallHandler::HandleSingleEventInternal(const ebpf_event *event, pid_t child_pid, int error, std::string& final_path) {
    kernel_function kernel_function = RetrieveKernelFunctionIfAvailable(event->metadata);

    // For some operations (e.g. memory files) our path translation returns an empty string. Those cases
    // should match with the ones we don't care about tracing. So do not send that event to managed side but
    // let the log debug event call above log it, so we can investigate otherwise.
    if (!IsPathRooted(final_path)) {
        return false;
    }

    // Some paths may still contain unresolved symlinks. Resolve them if needed.
    ResolveSymlinksIfNeeded(final_path, event->metadata.symlink_resolution);
    
    mode_t mode = FromEBPFMode(event->metadata.mode);

    switch(event->metadata.operation_type) {
        case kClone:
        {
            auto sandboxEvent = SandboxEvent::CloneSandboxEvent(
                /* system_call */   kernel_function_to_string(kernel_function),
                /* pid */           child_pid,
                /* ppid */          event->metadata.pid,
                /* path */          final_path);
        
            // We have a single operation for now that can emit a kClone (wake_up_new_task), and this is unlikely to change, 
            // so do not bother checking IsEventCacheable
            CreateAndReportAccess(m_bxl, sandboxEvent, /* checkCache */ false);

            // Update the set of active pids to add the newly created child
            m_active_pids.emplace(child_pid);

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
                m_runner_exit_sent = true;
            }

            break;
        }
        case kGenericWrite:
        {
            // The inode is being written. Send a special event to indicate this so file existence based policies can be applied downstream
            ReportFirstAllowWriteCheck(m_bxl, event->metadata.operation_type, final_path, mode, event->metadata.pid);

            auto sandboxEvent = SandboxEvent::AbsolutePathSandboxEvent(
                /* system_call */   kernel_function_to_string(kernel_function),
                /* event_type */    EventType::kGenericWrite,
                /* pid */           event->metadata.pid,
                /* ppid */          0,
                /* error */         0,
                /* src_path */      final_path);
            sandboxEvent.SetMode(mode);
            sandboxEvent.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);
            CreateAndReportAccess(m_bxl,sandboxEvent);

            break;
        }
        case kCreate:
        {
            // The inode is being created. Send a special event to indicate this so file existence based policies can be applied downstream
            ReportFirstAllowWriteCheck(m_bxl, event->metadata.operation_type, final_path, mode, event->metadata.pid);

            auto sandboxEvent = SandboxEvent::AbsolutePathSandboxEvent(
                /* system_call */   kernel_function_to_string(kernel_function),
                /* event_type */    EventType::kCreate,
                /* pid */           event->metadata.pid,
                /* ppid */          0,
                /* error */         0,
                /* src_path */      final_path);
            sandboxEvent.SetMode(mode);
            sandboxEvent.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);
            CreateAndReportAccess(m_bxl, sandboxEvent, /* check_cache */ IsEventCacheable((const ebpf_event*) event));

            break;
        }
        case kUnlink: 
        {
            auto sandboxEvent = SandboxEvent::AbsolutePathSandboxEvent(
                /* system_call */   kernel_function_to_string(kernel_function),
                /* event_type */    EventType::kUnlink,
                /* pid */           event->metadata.pid,
                /* ppid */          0,
                /* error */         abs(error), // Managed side always expect a non-negative number
                /* src_path */      final_path);
            sandboxEvent.SetMode(mode);
            sandboxEvent.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);

            CreateAndReportAccess(m_bxl, sandboxEvent, /* check_cache */ IsEventCacheable((const ebpf_event*) event));
            break;
        }
        case kGenericProbe:
        {
            auto sandboxEvent = SandboxEvent::AbsolutePathSandboxEvent(
                /* system_call */   kernel_function_to_string(kernel_function),
                /* event_type */    EventType::kGenericProbe,
                /* pid */           event->metadata.pid,
                /* ppid */          0,
                /* error */         abs(error), // Managed side always expect a non-negative number
                /* src_path */      final_path);
            sandboxEvent.SetMode(mode);
            sandboxEvent.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);

            CreateAndReportAccess(m_bxl, sandboxEvent);
            break;
        }
        case kGenericRead:
        {
            auto sandboxEvent = SandboxEvent::AbsolutePathSandboxEvent(
                /* system_call */   kernel_function_to_string(kernel_function),
                /* event_type */    EventType::kGenericRead,
                /* pid */           event->metadata.pid,
                /* ppid */          0,
                /* error */         0,
                /* src_path */      final_path);
            sandboxEvent.SetMode(mode);
            sandboxEvent.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);

            CreateAndReportAccess(m_bxl, sandboxEvent);
            break;
        }
        case kReadLink:
        {
            auto sandboxEvent = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
                /* system_call */   kernel_function_to_string(kernel_function),
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
    m_bytes_submitted += sizeof(ebpf_event_metadata) + strlen(src_path) + 1 + strlen(dst_path) + 1;
    m_event_count++;

    // Same consideration for fully resolved paths as in the single path case
    if (!IsPathRooted(src_path) || !IsPathRooted(dst_path)) {
        return false;
    }

    std::string sourcePath(src_path);
    std::string destinationPath(dst_path);

    // Some paths may still contain unresolved symlinks. Resolve them if needed.
    ResolveSymlinksIfNeeded(sourcePath, event->metadata.symlink_resolution);
    ResolveSymlinksIfNeeded(destinationPath, event->metadata.symlink_resolution);

    kernel_function kernel_function = RetrieveKernelFunctionIfAvailable(event->metadata);

    switch (event->metadata.operation_type) {
        case kRename:
        {
            // Handling for this event is different based on whether it's a file or directory.
            // If a directory, the source directory no longer exists because the rename has already happened.
            // We can enumerate the destination directory instead.
            if (S_ISDIR(FromEBPFMode(event->metadata.mode))) {
                std::vector<std::string> filesAndDirectories;
                m_bxl->EnumerateDirectory(destinationPath, /* recursive */ true, filesAndDirectories);

                for (auto fileOrDirectory : filesAndDirectories) {
                    // Destination
                    auto mode = m_bxl->get_mode(fileOrDirectory.c_str());

                    // Send this special event on creation, similar to what we do with a kCreate coming from EBPF
                    SandboxEvent firstAllowWriteDst;
                    ReportFirstAllowWriteCheck(m_bxl, kCreate, fileOrDirectory.c_str(), mode, event->metadata.pid);

                    auto sandboxEventDestination = SandboxEvent::AbsolutePathSandboxEvent(
                        /* system_call */   kernel_function_to_string(kernel_function),
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
                        /* system_call */   kernel_function_to_string(kernel_function),
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
                auto mode = m_bxl->get_mode(destinationPath.c_str());
                // Source
                // Send this special event on write, similar to what we do with a kWrite coming from EBPF
                ReportFirstAllowWriteCheck(m_bxl, kGenericWrite, sourcePath, mode, event->metadata.pid);

                auto sandboxEventSource = SandboxEvent::AbsolutePathSandboxEvent(
                    /* system_call */   kernel_function_to_string(kernel_function),
                    /* event_type */    EventType::kUnlink,
                    /* pid */           event->metadata.pid,
                    /* ppid */          0,
                    /* error */         0,
                    /* src_path */      sourcePath);
                // Source should be absent now, infer the mode from the destination
                sandboxEventSource.SetMode(mode);
                sandboxEventSource.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);
                CreateAndReportAccess(m_bxl, sandboxEventSource, /* check cache */ true);

                // Destination
                // Send this special event on creation, similar to what we do with a kCreate coming from EBPF
                ReportFirstAllowWriteCheck(m_bxl, kCreate, destinationPath, mode, event->metadata.pid);

                auto sandboxEventDestination = SandboxEvent::AbsolutePathSandboxEvent(
                    /* system_call */   kernel_function_to_string(kernel_function),
                    /* event_type */    EventType::kCreate,
                    /* pid */           event->metadata.pid,
                    /* ppid */          0,
                    /* error */         0,
                    /* src_path */      destinationPath);
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

    const char* exe_path = get_exe_path(event);
    const char* args = get_args(event);

    // Track the total bytes submitted for this event
    m_bytes_submitted += sizeof(ebpf_event_metadata) + strlen(exe_path) + 1 + strlen(args) + 1;
    m_event_count++;

    // Some paths may still contain unresolved symlinks. Resolve them if needed.
    std::string exePath(exe_path);
    ResolveSymlinksIfNeeded(exePath, event->metadata.symlink_resolution);

    auto sandboxEvent = SandboxEvent::ExecSandboxEvent(
        /* system_call */   kernel_function_to_string(RetrieveKernelFunctionIfAvailable(event->metadata)),
        /* pid */           event->metadata.pid,
        /* ppid */          0,
        /* path */          exePath,
        /* command_line */  m_bxl->IsReportingProcessArgs() ? args : "");
    CreateAndReportAccess(m_bxl,sandboxEvent, /* check_cache */ false);

    return true;
}

bool SyscallHandler::HandleDebugEvent(const ebpf_event_debug *event) {

    // Track the total bytes submitted for this event
    m_bytes_submitted += sizeof(ebpf_event_debug);
    m_event_count++;

    // Add the pip id (as seen by EBPF) to all debug messages
    char messageWithPipId[PATH_MAX];
    snprintf(messageWithPipId, PATH_MAX, "[%d] [%d] %s", event->runner_pid, event->pid, event->message);

    m_bxl->LogError(event->pid, messageWithPipId);
    return true;
}

bool SyscallHandler::IsEventCacheable(const ebpf_event *event)
{
    return event->metadata.is_cacheable;
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
        "[Ring buffer monitoring] Minimum available space: %.2f KB (%.2f%%). Total available space: %.2f KB. Total bytes sent: %.2f KB. Total events %ld. Capacity exceeded %d time(s).",
        (double) min_available / (1024.0), 
        percent_available, 
        (double) total / (1024.0),  
        (double) m_bytes_submitted / (1024.0), 
        m_event_count,
        eventRingbuffer->GetId());

    double percent_incremental_saved = (m_bytes_saved_incremental != 0) ? (100.0 * m_bytes_saved_incremental / (m_bytes_submitted + m_bytes_saved_incremental)) : 0.0;
    m_bxl->LogInfo(
        getpid(),
        "[Ring buffer monitoring] Total bytes saved by using incremental path encoding: %.2f KB (%.2f%%).",
        (double)m_bytes_saved_incremental / (1024.0), percent_incremental_saved);

    if (m_bxl->LogDebugEnabled()) {
        m_bxl->LogDebug(
            getpid(),
            "[Ring buffer monitoring] Total diagnostics events: %ld. Total diagnostics bytes submitted: %.2f KB. Total events including diagnostics: %ld. Total bytes submitted including diagnostics: %.2f KB.",
            m_diagnostics_event_count,
            (double)m_diagnostics_bytes_submitted / (1024.0),
            m_event_count + m_diagnostics_event_count,
            (double)(m_bytes_submitted + m_diagnostics_bytes_submitted) / (1024.0));
    }
}

void SyscallHandler::RemovePid(pid_t pid) {
    auto result = m_active_pids.erase(pid);
    // If we removed the last active pid, signal that there are no more active pids
    if (m_active_pids.empty()) {
        sem_post(&m_no_active_pids_semaphore);
    }
}

void SyscallHandler::LogDebugEvent(ebpf_event *event)
{
    // Shortcut if debug logging is not enabled
    // We don't log anything for diagnostics events, since they just contribute to the subsequent event
    if (!m_bxl->LogDebugEnabled() || event->metadata.event_type == DIAGNOSTICS)
    {
        return;
    }

    // Add additional diagnostics info if available
    std::shared_ptr<ebpf_diagnostics> diagnostics = RetrieveDiagnosticsIfAvailable(event->metadata);
    kernel_function kernel_function = kernel_function::KERNEL_unknown;;
    double percent_available = 0;
    if (diagnostics != nullptr)
    {
        kernel_function = diagnostics->kernel_function;

        auto eventRingbuffer = m_active_ringbuffer->load();
        size_t total = eventRingbuffer->GetRingBufferSize();
        size_t available_space = total - diagnostics->available_data_to_consume;
        percent_available = (total > 0) ? (100.0 * available_space / total) : 0.0;
    }
    
    switch (event->metadata.event_type)
    {
        case EXEC: 
        {
            const ebpf_event_exec * exec_event = (const ebpf_event_exec *)event;
            m_bxl->LogDebug(
                exec_event->metadata.pid, 
                "[%d] (available: %.2f%%) kernel function: %s, operation: %s, exe path: '%s', args: '%s'",
                exec_event->metadata.pid,
                percent_available,
                kernel_function_to_string(kernel_function), 
                operation_type_to_string(exec_event->metadata.operation_type),
                get_exe_path(exec_event),
                get_args(exec_event));
            break;
        }
        case SINGLE_PATH:
        case SINGLE_PATH_WITH_CPID: 
        case SINGLE_PATH_WITH_ERROR:
        {
            std::string final_path;
            const char* src_path;
            // All three event types have the same metadata structure, so we can share the code
            // However, for SINGLE_PATH_WITH_CPID we need to cast to the right type to access the src_path
            if (event->metadata.event_type == SINGLE_PATH_WITH_CPID) {
                const ebpf_event_cpid * cpid_event = (const ebpf_event_cpid *)event;
                src_path = cpid_event->src_path;
                final_path = DecodeIncrementalEvent(&(cpid_event->metadata), cpid_event->src_path, /* forLogging */ true);
            } else {
                src_path = event->src_path;
                final_path = DecodeIncrementalEvent(&(event->metadata), event->src_path, /* forLogging */ true);
            }

            m_bxl->LogDebug(
                event->metadata.pid, 
                "[%d] (available: %.2f%%) kernel function: %s, operation: %s, S_ISREG: %d, S_ISDIR: %d, errno: %d, CPU id: %d, common prefix length: %d, incremental length: %d, path: '%s'",
                event->metadata.pid, 
                percent_available,
                kernel_function_to_string(kernel_function),
                operation_type_to_string(event->metadata.operation_type),
                S_ISREG(FromEBPFMode(event->metadata.mode)), 
                S_ISDIR(FromEBPFMode(event->metadata.mode)),
                event->metadata.event_type == ebpf_event_type::SINGLE_PATH_WITH_ERROR,
                event->metadata.processor_id,
                final_path.length() - strlen(src_path),
                strlen(src_path),
                final_path.c_str());
            break;
        }
        case DOUBLE_PATH:
        {
            const ebpf_event_double * double_event = (const ebpf_event_double *)event;
            m_bxl->LogDebug(
                double_event->metadata.pid, 
                "[%d] (available: %.2f%%) kernel function: %s, operation: %s, S_ISREG: %d, S_ISDIR: %d, source path: '%s', dest path '%s'",
                event->metadata.pid, 
                percent_available,
                kernel_function_to_string(kernel_function),
                operation_type_to_string(double_event->metadata.operation_type),
                S_ISREG(FromEBPFMode(event->metadata.mode)),
                S_ISDIR(FromEBPFMode(event->metadata.mode)),
                get_src_path(double_event),
                get_dst_path(double_event));
            break;
        }
        // We do nothing with Debug messages because they are going to get logged as is anyway downstream
        default:
        {
            break;
        }
    }
}

} // ebpf
} // linux
} // buildxl