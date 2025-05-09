// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: GPL-2.0 OR MIT

#ifndef __PUBLIC_SRC_SANDBOX_LINUX_EBPF_EBPFCOMMON_H
#define __PUBLIC_SRC_SANDBOX_LINUX_EBPF_EBPFCOMMON_H

#include "kernelconstants.h"

// NOTE: this file follows the Linux coding style since it's shared with kernel code which is written in C

/**
 * Maximum number of breakaway processes supported by the sandbox.
 */
#define MAX_BREAKAWAY_PROCESSES 64

/**
 * Maximum number of arguments that the sandbox will attempt to
 * parse from argv.
 */
#define MAX_ARGV_ARGUMENTS 128

// Standard file size on most file systems is 255
// +1 here for the null terminator
#define FILENAME_MAX 256

#define KERNEL_FUNCTION(name) KERNEL_##name
#define CONVERT_KERNEL_FUNCTION_TO_STRING(fn) case KERNEL_FUNCTION(fn): return #fn;
#define TO_STRING(s) #s
#define EXPAND_AND_STRINGIFY(x) TO_STRING(x)

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
inline const char* operation_type_to_string(operation_type o) {
    switch (o)
    {
        case kClone:
            return "clone";
        case kPTrace:
            return "ptrace";
        case kFirstAllowWriteCheckInProcess:
            return "FirstAllowWriteCheckInProcess";
        case kExec:
            return "exec";
        case kExit:
            return "exit";
        case kOpen:
            return "open";
        case kClose:
            return "close";
        case kCreate:
            return "create";
        case kGenericWrite:
            return "write";
        case kGenericRead:
            return "read";
        case kGenericProbe:
            return "probe";
        case kRename:
            return "rename";
        case kReadLink:
            return "readlink";
        case kLink:
            return "link";
        case kUnlink:
            return "unlink";
        case kBreakAway:
            return "breakaway";
        default:
            return "[unknown operation]";
    }
}

// This function is arbitrarily picked as the witness for having loaded all our ebpf programs
#define LOADING_WITNESS wake_up_new_task

// This is the list of kernel functions we trace. In general, we prefer hooking into security_* as much as possible since
// that's a common layer for many kernel functions and we can 1) trace less functions overall, compared to tracing higher-level ones (like
// syscalls), 2) consume paths that are already resolved, so we don't need to duplicate kernel semantics to interpret them and 3) we may be
// better covered for potential additions to the kernel, as many of these security hooks are reused. General info about the security layer
// here: https://www.kernel.org/doc/html/v6.14-rc5/security/lsm.html.
typedef enum kernel_function {
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
    KERNEL_FUNCTION(execve),
    KERNEL_FUNCTION(execveat),
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
        CONVERT_KERNEL_FUNCTION_TO_STRING(execve)
        CONVERT_KERNEL_FUNCTION_TO_STRING(execveat)
        CONVERT_KERNEL_FUNCTION_TO_STRING(security_bprm_committed_creds)
        CONVERT_KERNEL_FUNCTION_TO_STRING(vfs_utimes)
        default:
            return "[unknown kernel function]";
    }
}

typedef struct breakaway_process {
    char tool[FILENAME_MAX];
    int tool_len;
    char arguments[PATH_MAX];
    int arguments_len;
    bool args_ignore_case;
} breakaway_process;

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
    // We use PATH_MAX + FILENAME_MAX here to enable us to do some string operations
    // on the kernel side without having the verifier complain about the size of the buffer
    char exe_path[PATH_MAX + FILENAME_MAX];
    char args[PATH_MAX];
} ebpf_event_exec;

/**
 * This structure is only used internally by the bpf program to track lengths
 * of strings.
 */
typedef struct exec_event_metadata {
    ebpf_event_exec *event;
    int exe_path_len;
    int exe_name_start_index;
    int exe_name_len;
    int args_len;
    bool needs_breakaway;
} exec_event_metadata;

typedef struct ebpf_event_debug {
    ebpf_event_type event_type;
    int pid;
    int runner_pid;
    char message[PATH_MAX];
} ebpf_event_debug;

typedef struct sandbox_options {
    int root_pid;
    int root_pid_init_exec_occured;
    int is_monitoring_child_processes;
} sandbox_options;

/**
 * An event key represents an operation + path, and used as a way to identify 'equivalent' events and prevent sending duplicates to user space.
 * For identifying the path, we use a combination of its dentry and vfsmount pair, and just use their memory
 * location (as unsigned long) to identify them. The rationale is that a dentry + mount pair is already pointing to a univocally
 * determined object in memory representing the path (which assumes that when the kernel lookup calls resolve a given path-as-string
 * it always ends up with the same dentry+mount instances for the same string). Even if this is not the case in all possible contexts, that it is
 * true in *most* contexts is enough to avoid sending too many equivalent events to user space.
 * Consider that using path-as-strings for the key is probably not a great idea, as the lookup logic for bpf maps use bitwise equality and there is no good way to represent
 * a PATH_MAX long string in the key and make that efficient. Luckily, most operations we care about give us access to the corresponding dentry and mount.
 */
typedef struct cache_event_key {
    unsigned long dentry;
    unsigned long vfsmount;
    operation_type op_type;
} cache_event_key;

#endif // __PUBLIC_SRC_SANDBOX_LINUX_EBPF_EBPFCOMMON_H