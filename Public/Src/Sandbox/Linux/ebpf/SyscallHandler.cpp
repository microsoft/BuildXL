// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include "SyscallHandler.h"
#include "AccessChecker.h"

#define HANDLER_FUNCTION(syscallName) void SyscallHandler::MAKE_HANDLER_FN_NAME(syscallName) (BxlObserver *bxl, ebpf_event *event)

namespace buildxl {
namespace linux {
namespace ebpf {

bool SyscallHandler::HandleSingleEvent(BxlObserver *bxl, const ebpf_event *event) {
    switch(event->metadata.operation_type) {
        case kClone:
        {
            auto sandboxEvent = SandboxEvent::CloneSandboxEvent(
                /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                /* pid */           event->metadata.child_pid,
                /* ppid */          event->metadata.pid,
                /* path */          event->src_path);
        
            // We have a single operation for now that can emit a kClone (wake_up_new_task), and this is unlikely to change, 
            // so do not bother checking IsEventCacheable
            CreateAndReportAccess(bxl, sandboxEvent, /* checkCache */ false);
            break;
        }
        case kExit:
        {
            bxl->SendExitReport(event->metadata.pid, 0);
            break;
        }
        case kGenericWrite:
        {
            // The inode is being written. Send a special event to indicate this so file existence based policies can be applied downstream
            ReportFirstAllowWriteCheck(bxl, event->metadata.operation_type, event->src_path, event->metadata.mode, event->metadata.pid);

            auto sandboxEvent = SandboxEvent::AbsolutePathSandboxEvent(
                /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                /* event_type */    EventType::kGenericWrite,
                /* pid */           event->metadata.pid,
                /* ppid */          0,
                /* error */         0,
                /* src_path */      event->src_path);
            sandboxEvent.SetMode(event->metadata.mode);
            sandboxEvent.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);
            CreateAndReportAccess(bxl,sandboxEvent);

            break;
        }
        case kCreate:
        {
            // The inode is being created. Send a special event to indicate this so file existence based policies can be applied downstream
            ReportFirstAllowWriteCheck(bxl, event->metadata.operation_type, event->src_path, event->metadata.mode, event->metadata.pid);

            auto sandboxEvent = SandboxEvent::AbsolutePathSandboxEvent(
                /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                /* event_type */    EventType::kCreate,
                /* pid */           event->metadata.pid,
                /* ppid */          0,
                /* error */         0,
                /* src_path */      event->src_path);
            sandboxEvent.SetMode(event->metadata.mode);
            sandboxEvent.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);
            CreateAndReportAccess(bxl,sandboxEvent, /* check_cache */ IsEventCacheable(event));

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
                /* src_path */      event->src_path);
            sandboxEvent.SetMode(event->metadata.mode);
            sandboxEvent.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);

            CreateAndReportAccess(bxl,sandboxEvent, /* check_cache */ IsEventCacheable(event));
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
                /* src_path */      event->src_path);
            sandboxEvent.SetMode(event->metadata.mode);
            sandboxEvent.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);
        
            CreateAndReportAccess(bxl,sandboxEvent);
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
                /* src_path */      event->src_path);
            sandboxEvent.SetMode(event->metadata.mode);
            sandboxEvent.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);

            CreateAndReportAccess(bxl,sandboxEvent);
            break;
        }
        case kBreakAway:
        {
            bxl->SendBreakawayReport(event->src_path, event->metadata.pid, /** ppid */ 0);
            break;
        }
        default:
            fprintf(stderr, "Unhandled operation type %d", event->metadata.operation_type);
            exit(1);
            break;
    }

    return true;
}

bool SyscallHandler::HandleDoubleEvent(BxlObserver *bxl, const ebpf_event_double *event) {
    switch (event->metadata.operation_type) {
        case kRename:
        {
            // Handling for this event is different based on whether it's a file or directory.
            // If a directory, the source directory no longer exists because the rename has already happened.
            // We can enumerate the destination directory instead.
            if (S_ISDIR(event->metadata.mode)) {
                std::vector<std::string> filesAndDirectories;
                std::string sourcePath(event->src_path);
                std::string destinationPath(event->dst_path);
                bxl->EnumerateDirectory(event->dst_path, /* recursive */ true, filesAndDirectories);

                for (auto fileOrDirectory : filesAndDirectories) {
                    // Destination
                    auto mode = bxl->get_mode(fileOrDirectory.c_str());

                    // Send this special event on creation, similar to what we do with a kCreate coming from EBPF
                    SandboxEvent firstAllowWriteDst;
                    ReportFirstAllowWriteCheck(bxl, kCreate, fileOrDirectory.c_str(), mode, event->metadata.pid);

                    auto sandboxEventDestination = SandboxEvent::AbsolutePathSandboxEvent(
                        /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                        /* event_type */    EventType::kCreate,
                        /* pid */           event->metadata.pid,
                        /* ppid */          0,
                        /* error */         0,
                        /* src_path */      fileOrDirectory.c_str());
                    sandboxEventDestination.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);
                    sandboxEventDestination.SetMode(mode);
                    CreateAndReportAccess(bxl, sandboxEventDestination, /* check cache */ true);

                    // Source
                    fileOrDirectory.replace(0, destinationPath.length(), sourcePath);

                    // Send this special event on write, similar to what we do with a kWrite coming from EBPF
                    ReportFirstAllowWriteCheck(bxl, kGenericWrite, fileOrDirectory.c_str(), 0, event->metadata.pid);
                    
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
                    CreateAndReportAccess(bxl, sandboxEventSource, /* check cache */ true);
                }
            }
            else {
                auto mode = bxl->get_mode(event->dst_path);
                // Source
                // Send this special event on write, similar to what we do with a kWrite coming from EBPF
                ReportFirstAllowWriteCheck(bxl, kGenericWrite, event->src_path, mode, event->metadata.pid);

                auto sandboxEventSource = SandboxEvent::AbsolutePathSandboxEvent(
                    /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                    /* event_type */    EventType::kUnlink,
                    /* pid */           event->metadata.pid,
                    /* ppid */          0,
                    /* error */         0,
                    /* src_path */      event->src_path);
                // Source should be absent now, infer the mode from the destination
                sandboxEventSource.SetMode(mode);
                sandboxEventSource.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);
                CreateAndReportAccess(bxl, sandboxEventSource, /* check cache */ true);

                // Destination
                // Send this special event on creation, similar to what we do with a kCreate coming from EBPF
                ReportFirstAllowWriteCheck(bxl, kCreate, event->dst_path, mode, event->metadata.pid);

                auto sandboxEventDestination = SandboxEvent::AbsolutePathSandboxEvent(
                    /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                    /* event_type */    EventType::kCreate,
                    /* pid */           event->metadata.pid,
                    /* ppid */          0,
                    /* error */         0,
                    /* src_path */      event->dst_path);
                sandboxEventDestination.SetMode(mode);
                sandboxEventDestination.SetRequiredPathResolution(RequiredPathResolution::kDoNotResolve);
                CreateAndReportAccess(bxl, sandboxEventDestination, /* check cache */ true);
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

bool SyscallHandler::HandleExecEvent(BxlObserver *bxl, const ebpf_event_exec *event) {
    auto sandboxEvent = SandboxEvent::ExecSandboxEvent(
        /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
        /* pid */           event->metadata.pid,
        /* ppid */          0,
        /* path */          event->exe_path,
        /* command_line */  bxl->IsReportingProcessArgs() ? event->args : "");
    CreateAndReportAccess(bxl,sandboxEvent, /* check_cache */ false);

    return true;
}

bool SyscallHandler::HandleDebugEvent(BxlObserver *bxl, const ebpf_event_debug *event) {
    bxl->LogError(event->pid, event->message);
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

bool SyscallHandler::TryCreateFirstAllowWriteCheck(BxlObserver *bxl, operation_type operation_type, const char *path, mode_t mode, pid_t pid, SandboxEvent &event)
{
    // The inode is being created or is being written. event->metadata.operation_type is expected to be either a kWrite or a kCreate.
    assert(operation_type == kGenericWrite || operation_type == kCreate);

    // Send a special event to indicate this whenever OverrideAllowWriteForExistingFiles is on and the node is a regular file (we
    // don't send this event for directories)
    if (mode != 0 && !S_ISREG(mode))
    {
        return false;
    }

    auto policy = AccessChecker::PolicyForPath(bxl->GetFileAccessManifest(), path);

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

void SyscallHandler::ReportFirstAllowWriteCheck(BxlObserver *bxl, operation_type operation_type, const char *path, mode_t mode, pid_t pid)
{
    SandboxEvent event;

    if (TryCreateFirstAllowWriteCheck(bxl, operation_type, path, mode, pid, event))
    {
        bxl->SendReport(event);
    }
}

} // ebpf
} // linux
} // buildxl