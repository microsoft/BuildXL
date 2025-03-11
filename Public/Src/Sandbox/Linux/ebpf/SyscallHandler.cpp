// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include "SyscallHandler.h"
#include "SandboxEvent.h"

#define HANDLER_FUNCTION(syscallName) void SyscallHandler::MAKE_HANDLER_FN_NAME(syscallName) (BxlObserver *bxl, ebpf_event *event)

namespace buildxl {
namespace linux {
namespace ebpf {

bool SyscallHandler::HandleSingleEvent(BxlObserver *bxl, const ebpf_event *event) {
    switch(event->metadata.operation_type) {
        case kClone:
        {
            auto sandboxEvent = buildxl::linux::SandboxEvent::CloneSandboxEvent(
                /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                /* pid */           event->metadata.child_pid,
                /* ppid */          event->metadata.pid,
                /* path */          event->src_path);
        
            // We have a single operation for now that can emit a kClone (wake_up_new_task), and this is unlikely to change, 
            // so do not bother checking IsEventCacheable
            bxl->CreateAndReportAccess(sandboxEvent, /* checkCache */ false);
            break;
        }
        case kExit:
        {
            bxl->SendExitReport(event->metadata.pid, 0);
            break;
        }
        case kGenericWrite:
        {
            auto sandboxEvent = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
                /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                /* event_type */    buildxl::linux::EventType::kGenericWrite,
                /* pid */           event->metadata.pid,
                /* ppid */          0,
                /* error */         0,
                /* src_path */      event->src_path);
            sandboxEvent.SetMode(event->metadata.mode);
            sandboxEvent.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kDoNotResolve);
            bxl->CreateAndReportAccess(sandboxEvent);

            break;
        }
        case kCreate:
        {
            auto sandboxEvent = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
                /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                /* event_type */    buildxl::linux::EventType::kCreate,
                /* pid */           event->metadata.pid,
                /* ppid */          0,
                /* error */         0,
                /* src_path */      event->src_path);
            sandboxEvent.SetMode(event->metadata.mode);
            sandboxEvent.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kDoNotResolve);
            bxl->CreateAndReportAccess(sandboxEvent, /* check_cache */ IsEventCacheable(event));

            break;
        }
        case kUnlink: 
        {
            auto sandboxEvent = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
                /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                /* event_type */    buildxl::linux::EventType::kUnlink,
                /* pid */           event->metadata.pid,
                /* ppid */          0,
                /* error */         event->metadata.error * -1, // error is negative for rmdir
                /* src_path */      event->src_path);
            sandboxEvent.SetMode(event->metadata.mode);
            sandboxEvent.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kDoNotResolve);

            bxl->CreateAndReportAccess(sandboxEvent, /* check_cache */ IsEventCacheable(event));
            break;
        }
        case kGenericProbe:
        {
            auto sandboxEvent = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
                /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                /* event_type */    buildxl::linux::EventType::kGenericProbe,
                /* pid */           event->metadata.pid,
                /* ppid */          0,
                /* error */         abs(event->metadata.error), // Managed side always expect a non-negative number
                /* src_path */      event->src_path);
            sandboxEvent.SetMode(event->metadata.mode);
            sandboxEvent.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kDoNotResolve);
        
            bxl->CreateAndReportAccess(sandboxEvent);
            break;
        }
        case kGenericRead:
        {
            auto sandboxEvent = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
                /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                /* event_type */    buildxl::linux::EventType::kGenericRead,
                /* pid */           event->metadata.pid,
                /* ppid */          0,
                /* error */         0,
                /* src_path */      event->src_path);
            sandboxEvent.SetMode(event->metadata.mode);
            sandboxEvent.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kDoNotResolve);

            bxl->CreateAndReportAccess(sandboxEvent);
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
                    auto sandboxEventDestination = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
                        /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                        /* event_type */    buildxl::linux::EventType::kCreate,
                        /* pid */           event->metadata.pid,
                        /* ppid */          0,
                        /* error */         0,
                        /* src_path */      fileOrDirectory.c_str());
                    sandboxEventDestination.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kDoNotResolve);
                    sandboxEventDestination.SetMode(mode);
                    bxl->CreateAndReportAccess(sandboxEventDestination);

                    // Source
                    fileOrDirectory.replace(0, destinationPath.length(), sourcePath);
                    auto sandboxEventSource = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
                        /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                        /* event_type */    buildxl::linux::EventType::kUnlink,
                        /* pid */           event->metadata.pid,
                        /* ppid */          0,
                        /* error */         0,
                        /* src_path */      fileOrDirectory.c_str());
                    sandboxEventSource.SetMode(mode);
                    sandboxEventSource.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kDoNotResolve);
                    bxl->CreateAndReportAccess(sandboxEventSource);
                }
            }
            else {
                // Source
                auto sandboxEventSource = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
                    /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                    /* event_type */    buildxl::linux::EventType::kUnlink,
                    /* pid */           event->metadata.pid,
                    /* ppid */          0,
                    /* error */         0,
                    /* src_path */      event->src_path);
                sandboxEventSource.SetMode(event->metadata.mode);
                sandboxEventSource.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kDoNotResolve);
                bxl->CreateAndReportAccess(sandboxEventSource);

                // Destination
                auto sandboxEventDestination = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
                    /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
                    /* event_type */    buildxl::linux::EventType::kCreate,
                    /* pid */           event->metadata.pid,
                    /* ppid */          0,
                    /* error */         0,
                    /* src_path */      event->dst_path);
                sandboxEventDestination.SetMode(event->metadata.mode);
                sandboxEventDestination.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kDoNotResolve);
                bxl->CreateAndReportAccess(sandboxEventDestination);
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
    auto commandLineArgs = bxl->DoGetProcessCommandLine(event->metadata.pid);

    auto sandboxEvent = buildxl::linux::SandboxEvent::ExecSandboxEvent(
        /* system_call */   kernel_function_to_string(event->metadata.kernel_function),
        /* pid */           event->metadata.pid,
        /* ppid */          0,
        /* path */          event->exe_path,
        /* command_line */  bxl->IsReportingProcessArgs() ? commandLineArgs : "");
    bxl->CreateAndReportAccess(sandboxEvent, /* check_cache */ false);

    return bxl->ShouldBreakaway(sandboxEvent.GetSrcPath().c_str(), commandLineArgs, event->metadata.pid, 0);
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
    
} // ebpf
} // linux
} // buildxl