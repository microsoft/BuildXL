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

// Size of the ring buffers used to communicate file accesses and debug events to userspace.
// PATH_MAX * 512 entries
// This number was chosen based on experiments with customer builds, where we found that 512 entries is a good balance between memory usage and the number of events we can handle. We tipically
// never go below 60% of available space in the ring buffer, so this should be enough for most scenarios.
#define FILE_ACCESS_RINGBUFFER_SIZE (4096 * 512) 
// Size of the debug ring buffer used to communicate debug events to userspace.
// PATH_MAX * 64 entries
// We don't need this to be very big, as the first error sent is usually enough to signal that there is something
// going wrong. Debugging scenarios (where we send a lot of debug events) are not expected to be very common, so we can afford to have a smaller buffer here.
#define DEBUG_RINGBUFFER_SIZE (4096 * 64) 
// Size of the event cache map. This is used to avoid sending repetitive events for the same operation+path.
// With the current key+value size, this is about 1.8 MB in size per pip.
#define EVENT_CACHE_MAP_SIZE (16834)
// Size of the string cache map. This is used to avoid sending repetitive events for paths-as-strings (when we don't have a struct path available).
// With the current key+value size, this is about 2.4 MB in size per pip.
#define STRING_CACHE_MAP_SIZE (4096)
// The maximum size of a path that we can handle in the string cache. Paths longer than this will not be cached.
#define STRING_CACHE_PATH_MAX 512

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
    KERNEL_FUNCTION(vfs_utimes),
    KERNEL_FUNCTION(test_synthetic) // not a real operation, tests can inject these
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
        CONVERT_KERNEL_FUNCTION_TO_STRING(test_synthetic)
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
    ebpf_event_type event_type;
    // The 'conceptual' operation the event represents (e.g. a read, an exec, etc.)
    enum operation_type operation_type;
    // The kernel function we trace. Mostly for debugging purposes
    enum kernel_function kernel_function;
    int pid;
    int child_pid;
    unsigned int mode;
    int error;
    // The symmetric multiprocessing processor id that processed this event.
    // Useful to reconstruct incremental paths on user side, since that is described in terms of the last path per CPU.  
    __u32 processor_id;
    // The length of the source path prefix that is shared with the last path seen by this CPU.
    // Observe that an unsigned short is 2 bytes, enough to represent PATH_MAX (4096) lengths.
    unsigned short source_path_incremental_length;
} ebpf_event_metadata;

typedef struct ebpf_event {
    ebpf_event_metadata metadata;
    char src_path[];
} ebpf_event;

typedef struct ebpf_event_double {
    ebpf_event_metadata metadata;
    // The length of the source path, including the null terminator
    // This is used to calculate the offset of the destination path
    int src_path_length;
    // Source an destination paths are concatenated in the same buffer
    // The destination path starts at src_path_length
    // We use flexible arrays to avoid having to allocate a fixed size for the paths
    // Check below here for helpers to retrieve the paths
    char src_and_dst_path[];
} ebpf_event_double;

// Helpers to retrieve the source and destination paths from a double path event
inline const char* get_src_path(const ebpf_event_double* event) {
    return &(event->src_and_dst_path[0]);
}

inline const char* get_dst_path(const ebpf_event_double* event) {
    return &(event->src_and_dst_path[event->src_path_length]);
}

typedef struct ebpf_event_exec {
    ebpf_event_metadata metadata;
    // The length of the exe path, including the null terminator
    // This is used to calculate the offset of the args
    int exe_path_length;
    // Exe and args are concatenated in the same buffer
    // The args start at exe_path_length
    // We use flexible arrays to avoid having to allocate a fixed size for the paths
    // Check below here for helpers to retrieve the paths
    char exe_path_and_args[];
} ebpf_event_exec;

// Helpers to retrieve the exe path and args from an exec event
inline const char* get_exe_path(const ebpf_event_exec* event) {
    return &(event->exe_path_and_args[0]);
}

inline const char* get_args(const ebpf_event_exec* event) {
    return &(event->exe_path_and_args[event->exe_path_length]);
}

/**
 * This structure is only used internally by the bpf program to track lengths
 * of strings.
 */
typedef struct exec_event_metadata {
    char* exe_path;
    char* args;
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
 * Used to communicate general statistics about the sandbox to userspace.
 * Populated when the root pid exits
 */
typedef struct pip_stats {
    int event_cache_hit;
    int event_cache_miss;
    int string_cache_hit;
    int string_cache_miss;
    int string_cache_uncacheable;
} pip_stats;

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

/** This structure is used to pass arguments to the test_write_ringbuf syscall  */
typedef struct test_write_ringbuf_args {
    pid_t runner_pid;
    int number;
} test_write_ringbuf_args;

/** This structure is used to pass arguments to the test_incremental_event syscall  */
typedef struct test_incremental_event_args {
    char path1[PATH_MAX];
    char path2[PATH_MAX];
} test_incremental_event_args;

/**
 * The constant we use as map values when using a map as a set (and so the value is not important).
 */
static const short NO_VALUE = 0;

#endif // __PUBLIC_SRC_SANDBOX_LINUX_EBPF_EBPFCOMMON_H