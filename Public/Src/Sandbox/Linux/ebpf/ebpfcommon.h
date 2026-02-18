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
#define EVENT_CACHE_MAP_SIZE (16384)
// Size of the string cache map. This is used to avoid sending repetitive events for paths-as-strings (when we don't have a struct path available).
// With the current key+value size, this is about 2.4 MB in size per pip.
#define STRING_CACHE_MAP_SIZE (4096)
// The maximum size of a path that we can handle in the string cache. Paths longer than this will not be cached.
#define STRING_CACHE_PATH_MAX 512
// Size of the negative dentry cache map. This is used to avoid sending repetitive absent probe events.
// Negative dentries are cached using {dentry_ptr, d_parent_ptr, d_name.hash_len} as the key (24 bytes per entry + 2 bytes value).
// With the current key+value size, this is about 1.4 MB in size per pip.
#define NEG_DENTRY_CACHE_MAP_SIZE (16384)

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
    kRenameSource, // Only used for distinguishing source vs target in the event cache, has no consumers outside of that
    kRenameTarget, // Same as above
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

// Describes how symlinks should be resolved on managed side when looking up a path
// Most ebpf programs will get paths that are already resolved, so there is no need to resolve symlinks. However,
// some programs (like readlink) use paths as strings and they may contain symlinks. We indicate how to handle those
// cases with this enum. The resolution is done in userspace, so this is just a hint to indicate how the path should be treated.
typedef enum path_symlink_resolution {
    // Resolve intermediate symlinks, but not the final component of the path (basically, O_NOFOLLOW)
    resolveIntermediates = 0,
    // Resolve intermediate symlinks and the final component of the path
    fullyResolve,
    // Do not resolve any symlinks
    noResolve
} path_symlink_resolution;

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
    KERNEL_FUNCTION(do_faccessat),
    KERNEL_FUNCTION(test_synthetic), // not a real operation, tests can inject these
    // When diagnostics is not turned on, we don't get the kernel function for events, so we use this as a placeholder
    // This is fine since when diagnostics is off the kernel function is not visible anyway
    KERNEL_FUNCTION(unknown)
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
        CONVERT_KERNEL_FUNCTION_TO_STRING(do_faccessat)
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

// Single path events are by far the most common type of events we see.
// Within those, the vast majority are successful operations (no error) and where the child pid is not needed.
// To optimize for this common case, we have three different event types for single path events:
// 1) SINGLE_PATH: for successful operations where child pid is not needed
// 2) SINGLE_PATH_WITH_CPID: for successful operations where child pid is needed (e.g., clone)
// 3) SINGLE_PATH_WITH_ERROR: for operations that hit an error
// In all these cases, we send the path as a string with incremental encoding to save space.
typedef enum ebpf_event_type {
    // Single path event where the path is sent as a string with incremental encoding
    SINGLE_PATH = 1,
    // Single path event along with the child process ID (e.g., clone)
    SINGLE_PATH_WITH_CPID = 2,
    // Single path event where the corresponding operation hit an error (any error, we later map all errors to ENOENT to save space)
    SINGLE_PATH_WITH_ERROR = 3,
    // Double path event (e.g., rename)
    DOUBLE_PATH = 4,
    // Exec event (e.g., execve)
    EXEC = 5,
    // Debug event - carries an arbitrary debug message
    DEBUG = 6,
    // Diagnostics event (used for internal diagnostics between ebpf and user mode)
    DIAGNOSTICS = 7,
} ebpf_event_type;

// We don't need all the many file modes available in the kernel. We just need to distinguish
// between regular files, directories, symlinks and others. We can encode these in 4 bits
// (as opposed to the 4 bytes used in the kernel)
typedef enum ebpf_mode {
    UNKNOWN_MODE = 0,
    REGULAR_FILE = 1,
    DIRECTORY = 2,
    SYMLINK = 4,
    OTHER = 8
} ebpf_mode;

// Common metadata for all events
// We want to keep this one to the bare minimum in terms of size, as it is included in all events. Every byte counts!
// Fields that are conceptually part of the metadata but are not used very frequently (e.g., error code, child pid) 
// are included in the specific event structures instead.
typedef struct ebpf_event_metadata {
    // We have a bunch of enums here that we want to keep as small as possible
    // to save space in the event metadata. We use bitfields to pack them tightly.
    // Today we need 15 bits to encode all the enums, so we can fit them in 2 bytes (16 bits).
    union {
        struct {
            // Main event type (single path, double path, exec, etc.)
            enum ebpf_event_type event_type : 3;
            // The 'conceptual' operation the event represents (e.g. a read, an exec, etc.)
            enum operation_type operation_type : 5;
            // Whether symlinks should be resolved
            enum path_symlink_resolution symlink_resolution : 2;
            enum ebpf_mode mode : 4;
            // Whether the event is cacheable (on user side)
            bool is_cacheable: 1;
            // Padding bits to make the struct aligned to 2 bytes
            uint16_t reserved : 1;
        } __attribute__((packed));
        uint16_t packed_enums;
    };
    pid_t pid;
    // The symmetric multiprocessing processor id that processed this event.
    // Useful to reconstruct incremental paths on user side, since that is described in terms of the last path per CPU.
    // In theory a 32-bit value, but in practice Linux supports up to 8192 CPUs today, so we can use a 16-bit value here.
    uint16_t processor_id;
    // The length of the source path prefix that is shared with the last path seen by this CPU.
    // Observe that an unsigned short is 2 bytes, enough to represent PATH_MAX (4096) lengths.
    uint16_t source_path_incremental_length;
} __attribute__((packed)) ebpf_event_metadata;

// Diagnostics event structure
// These events are used for internal diagnostics between ebpf and user mode
// The type for this event is DIAGNOSTICS
// Keep the event type as the first field, since we use it to identify the event on user side
// In order to keep regular event sizes small, we send all the non-essential information in diagnostics events. When diagnostics
// is enabled, we send a diagnostics event right before the actual event, so user side can correlate them. Both the diagnostic event
// and the actual event share the same processor_id, so user side can match them easily.
typedef struct ebpf_diagnostics {
    union {
        struct {
            enum ebpf_event_type event_type : 3;
            enum kernel_function kernel_function : 5;
            uint16_t padding: 8; // just to align to 2 bytes
        } __attribute__((packed));
        uint16_t packed_enums;
    };
    uint16_t processor_id;
    long available_data_to_consume;
} __attribute__((packed)) ebpf_diagnostics;

// Event structure for events with a single path (e.g., open, read, write, etc.) where we send the path as a plain string.
// These events are very common, so we use incremental encoding to save space.
// The type for this event is SINGLE_PATH or SINGLE_PATH_WITH_ERROR
typedef struct ebpf_event {
    ebpf_event_metadata metadata;
    // The source path is stored here as a flexible array member.
    char src_path[];
} __attribute__((packed)) ebpf_event;

// Event structure for events with a single path (e.g., clone) where we send the path as a string along with the child process ID.
// Sending the child PID is uncommon enough that we factor out a separate structure for it, so we avoid sending it when not needed.
// The type for this event is SINGLE_PATH_WITH_CPID
typedef struct ebpf_event_cpid {
    ebpf_event_metadata metadata;
    // The child PID
    pid_t child_pid;
    // The source path is stored here as a flexible array member.
    char src_path[];
} __attribute__((packed)) ebpf_event_cpid;

// Event structure for events with two paths (e.g., rename)
// These event don't tend to be very common, so we don't use incremental encoding for them and just send both paths as-is.
// The type for this event is DOUBLE_PATH
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
} __attribute__((packed)) ebpf_event_double;

// Helpers to retrieve the source and destination paths from a double path event
inline const char* get_src_path(const ebpf_event_double* event) {
    return &(event->src_and_dst_path[0]);
}

inline const char* get_dst_path(const ebpf_event_double* event) {
    return &(event->src_and_dst_path[event->src_path_length]);
}

// Event structure for exec events
// These events contain the exe path and the args concatenated in the same buffer.
// Not very common, so we don't use incremental encoding for them and just send both strings as-is.
// The type for this event is EXEC
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
} __attribute__((packed)) ebpf_event_exec;

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
    // Whether to send a diagnostic event (which includes debugging information, as the kernel function name) for each event processed
    bool enable_diagnostics;
} sandbox_options;

/**
 * Used to communicate general statistics about the sandbox to userspace.
 */
typedef struct pip_stats {
    int event_cache_hit;
    int event_cache_miss;
    int string_cache_hit;
    int string_cache_miss;
    int string_cache_uncacheable;
    int neg_dentry_cache_hit;
    int neg_dentry_cache_miss;
    int untracked_path_count;
    long untracked_path_bytes;
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
    long unsigned int inode_number;
    operation_type op_type;
} cache_event_key;

/**
 * A cache key for negative dentries (absent path components). Used to deduplicate absent probes per-pip.
 * The key combines the dentry pointer with its parent and name hash to detect slab reuse:
 * - dentry_ptr: fast O(1) identity for the kernel dentry object
 * - d_parent_ptr: guards against slab reuse under a different parent directory
 * - d_name_hash_len: 32-bit hash + 32-bit length of the component name, guards against reuse with a different name
 */
typedef struct neg_dentry_cache_key {
    unsigned long dentry_ptr;
    unsigned long d_parent_ptr;
    unsigned long d_name_hash_len;
} neg_dentry_cache_key;

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

/** Given the restrictions on LPM tries, the longest path we store is 256 bytes. See untracked_scopes map */
#define MAX_LPM_PATH_LEN 256

/**
 * Key for untracked_scopes map.
 */
struct untracked_path_key {
    // Number of bytes expressed in bits. Has to be a multiple of 8 and less than 2048.
    // This means the longest path is 256 bytes.
    __u32 prefixlen;
    // Path in raw bytes
    char path[MAX_LPM_PATH_LEN];
};

// Useful for retrieving 1-sized or 2-sized arrays
const static int ZERO = 0;
const static int ONE = 1;

/** Arguments for the path canonicalization test */
typedef struct test_path_canonicalization_args {
    char path[PATH_MAX];
} test_path_canonicalization_args;

#endif // __PUBLIC_SRC_SANDBOX_LINUX_EBPF_EBPFCOMMON_H