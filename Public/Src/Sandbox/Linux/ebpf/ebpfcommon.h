// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: GPL-2.0 OR MIT

#ifndef __COMMON_H
#define __COMMON_H

#include "kernelconstants.h"

// NOTE: this file follows the Linux coding style since it's shared with kernel code which is written in C

#define KERNEL_FUNCTION(name) KERNEL_##name
#define CONVERT_KERNEL_FUNCTION_TO_STRING(fn) case KERNEL_FUNCTION(fn): return #fn;

// Copied from Public/Src/Sandbox/Linux/Operations.h. TODO: Unify
typedef enum operation_type {
    kClone = 0,
    kPTrace,
    kFirstAllowWriteCheckInProcess,
    kExec,
    kExit,
    kOpen,
    kClose,
    kCreate,
    kGenericWrite,
    kGenericRead,
    kGenericProbe,
    kRename,
    kReadLink,
    kLink,
    kUnlink,
    kBreakAway,
    kMax // Not a valid event type
} operation_type;

// Just for debugging purposes
inline const char* operation_type_to_string(operation_type o)
{
    switch (o)
    {
        case kClone:   return "clone";
        case kPTrace:   return "ptrace";
        case kFirstAllowWriteCheckInProcess: return "FirstAllowWriteCheckInProcess";
        case kExec:   return "exec";
        case kExit:   return "exit";
        case kOpen:   return "open";
        case kClose:   return "close";
        case kCreate:   return "create";
        case kGenericWrite:   return "write";
        case kGenericRead:   return "read";
        case kGenericProbe:   return "probe";
        case kRename:   return "rename";
        case kReadLink:   return "readlink";
        case kLink:   return "link";
        case kUnlink:   return "unlink";
        case kBreakAway:   return "breakaway";
        default:      return "[unknown operation]";
    }
}

// This is the list of kernel functions we trace. In general, we prefer hooking into security_* as much as possible since
// that's a common layer for many kernel functions and we can 1) trace less functions overall, compared to tracing higher-level ones (like
// syscalls), 2) consume paths that are already resolved, so we don't need to duplicate kernel semantics to interpret them and 3) we may be 
// better covered for potential additions to the kernel, as many of these security hooks are reused. General info about the security layer 
// here: https://www.kernel.org/doc/html/v6.14-rc5/security/lsm.html.
typedef enum kernel_function 
{
    KERNEL_FUNCTION(wake_up_new_task) = 0,
    KERNEL_FUNCTION(exit),
    KERNEL_FUNCTION(path_lookupat),
    KERNEL_FUNCTION(path_openat),
    KERNEL_FUNCTION(path_parentat),
    KERNEL_FUNCTION(security_file_open),
    KERNEL_FUNCTION(security_file_permission),
    KERNEL_FUNCTION(security_file_truncate),
    KERNEL_FUNCTION(pick_link_enter),
    KERNEL_FUNCTION(security_path_link),
    KERNEL_FUNCTION(do_readlinkat),
    KERNEL_FUNCTION(security_path_rename),
    KERNEL_FUNCTION(security_path_rmdir),
    KERNEL_FUNCTION(security_path_symlink),
    KERNEL_FUNCTION(security_path_unlink),
    KERNEL_FUNCTION(security_path_mknod),
    KERNEL_FUNCTION(security_path_chown),
    KERNEL_FUNCTION(security_path_chmod),
    KERNEL_FUNCTION(security_inode_getattr),
    KERNEL_FUNCTION(do_rmdir),
    KERNEL_FUNCTION(do_mkdirat),
    KERNEL_FUNCTION(do_execveat_common),
    KERNEL_FUNCTION(security_bprm_committed_creds),
    KERNEL_FUNCTION(vfs_utimes)
} kernel_function;

inline const char* kernel_function_to_string(kernel_function kf) {
    switch (kf) {
        CONVERT_KERNEL_FUNCTION_TO_STRING(wake_up_new_task)
        CONVERT_KERNEL_FUNCTION_TO_STRING(exit)
        CONVERT_KERNEL_FUNCTION_TO_STRING(path_lookupat)
        CONVERT_KERNEL_FUNCTION_TO_STRING(path_openat)
        CONVERT_KERNEL_FUNCTION_TO_STRING(path_parentat)
        CONVERT_KERNEL_FUNCTION_TO_STRING(security_file_open)
        CONVERT_KERNEL_FUNCTION_TO_STRING(security_file_permission)
        CONVERT_KERNEL_FUNCTION_TO_STRING(security_file_truncate)
        CONVERT_KERNEL_FUNCTION_TO_STRING(pick_link_enter)
        CONVERT_KERNEL_FUNCTION_TO_STRING(security_path_link)
        CONVERT_KERNEL_FUNCTION_TO_STRING(do_readlinkat)
        CONVERT_KERNEL_FUNCTION_TO_STRING(security_path_rename)
        CONVERT_KERNEL_FUNCTION_TO_STRING(security_path_rmdir)
        CONVERT_KERNEL_FUNCTION_TO_STRING(security_path_symlink)
        CONVERT_KERNEL_FUNCTION_TO_STRING(security_path_unlink)
        CONVERT_KERNEL_FUNCTION_TO_STRING(security_path_mknod)
        CONVERT_KERNEL_FUNCTION_TO_STRING(security_path_chown)
        CONVERT_KERNEL_FUNCTION_TO_STRING(security_path_chmod)
        CONVERT_KERNEL_FUNCTION_TO_STRING(security_inode_getattr)
        CONVERT_KERNEL_FUNCTION_TO_STRING(do_rmdir)
        CONVERT_KERNEL_FUNCTION_TO_STRING(do_mkdirat)
        CONVERT_KERNEL_FUNCTION_TO_STRING(do_execveat_common)
        CONVERT_KERNEL_FUNCTION_TO_STRING(security_bprm_committed_creds)
        CONVERT_KERNEL_FUNCTION_TO_STRING(vfs_utimes)
        default:
            return "[unknown kernel function]";
    }
}

typedef enum ebpf_event_type {
    SINGLE_PATH = 1,
    DOUBLE_PATH = 2,
    EXEC = 3,
    DEBUG = 4
} ebpf_event_type;

typedef struct ebpf_event_metadata {
    // The 'conceptual' operation the event represents (e.g. a read, an exec, etc.)
    enum operation_type operation_type;
    // The kernel function we trace. Mostly for debugging purposes
    enum kernel_function kernel_function;
    int pid;
    int child_pid;
    unsigned int mode;
    int error;
} ebpf_event_metadata;

typedef struct ebpf_event {
    ebpf_event_type event_type;
    ebpf_event_metadata metadata;
    char src_path[PATH_MAX];
} ebpf_event;

typedef struct ebpf_event_double {
    ebpf_event_type event_type;
    ebpf_event_metadata metadata;
    char src_path[PATH_MAX];
    char dst_path[PATH_MAX];
} ebpf_event_double;

typedef struct ebpf_event_exec {
    ebpf_event_type event_type;
    ebpf_event_metadata metadata;
    char exe_path[PATH_MAX];
    char args[PATH_MAX];
} ebpf_event_exec;

typedef struct ebpf_event_debug {
    ebpf_event_type event_type;
    int pid;
    char message[PATH_MAX];
} ebpf_event_debug;

#endif // __COMMON_H