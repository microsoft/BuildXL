// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: GPL-2.0 OR MIT

#ifndef __PUBLIC_SRC_SANDBOX_LINUX_EBPF_EBPFUTILITIES_H
#define __PUBLIC_SRC_SANDBOX_LINUX_EBPF_EBPFUTILITIES_H

#include "bpf/bpf_helpers.h"

#include "vmlinux.h"
#include "kernelconstants.h"
#include "ebpfstringutilities.h"
#include "ebpfcommon.h"

extern int LINUX_KERNEL_VERSION __kconfig;

/**
 * The wakeup strategy is as follows:
 * - If the ring buffer is relatively empty (a quarter of its capacity), we do not wake up the user-side process, so we prioritize speed.
 * - If the ring buffer is relatively full (more than a quarter of its capacity), we force a wakeup for every submission. In this
 *  case we prioritize not running out of buffer space because user side cannot keep up.
 */
__attribute__((always_inline)) static long get_flags(void *ringbuffer) {
    long total_size = bpf_ringbuf_query(ringbuffer, BPF_RB_RING_SIZE);

    // If the current ring buffer size is greater than the original size, this means the ring buffer went through a swap to increase its size
    // This is the indication of high event pressure, so wake up every time from that point on
    if (total_size > FILE_ACCESS_RINGBUFFER_SIZE) {
        return BPF_RB_FORCE_WAKEUP;
    }

    long data_size = bpf_ringbuf_query(ringbuffer, BPF_RB_AVAIL_DATA);
    long threshold = total_size >> 2; // A quarter of the total size

    return data_size >= threshold ? BPF_RB_FORCE_WAKEUP : BPF_RB_NO_WAKEUP;
}

/*
 * Map containing currently active process id -> runner pid. Root process id is pre-populated by the userspace.
 * Observe these pids are the ones corresponding to the root namespace. So the assumption is that
 * BuildXL is running in the root namespace, otherwise pids won't match.
 * TODO: Ideally we should always return pids corresponding with the same namespace where BuildXL was launched
 * (which in an arbitrary situation might not be the root one)
 */
struct {
    __uint(type, BPF_MAP_TYPE_HASH);
    __type(key, pid_t);
    // The value is the pid of the runner that this sandboxed process belongs to
    __type(value, pid_t);
    // This is the max value of concurrent processes that can run in a linux OS
    // We will probably be always very far from reaching this number, but at the same time this map is pretty lightweight (int -> long)
    // so this shouldn't have a big memory footprint
    __uint(max_entries, 4194304);
    // We don't want to preallocate memory for this map, as the max bound is way higher than the usual number of processes that will run concurrently
    __uint(map_flags, BPF_F_NO_PREALLOC);
    // We need to share the pid_map across all runners
    __uint(pinning, LIBBPF_PIN_BY_NAME);
} pid_map SEC(".maps");

/**
 * Additional options to pass to the sandbox per pip.
 */
struct {
    __uint(type, BPF_MAP_TYPE_HASH);
    // The key is the pid of the runner process that this sandboxed process belongs to
    __type(key, pid_t);
    __type(value, sandbox_options);
    // We need to share the options across all runners
    __uint(pinning, LIBBPF_PIN_BY_NAME);
} sandbox_options_per_pip SEC(".maps");

/**
 * Statistics per pip. This is used to report statistics about the sandboxed processes.
 * The key is the pid of the runner process that this sandboxed process belongs to.
 * The value is a pip_stats structure that contains the statistics.
*/
struct {
    __uint(type, BPF_MAP_TYPE_HASH);
    // The key is the pid of the runner process that this sandboxed process belongs to
    __type(key, pid_t);
    __type(value, pip_stats);
    // We need to share the options across all runners
    __uint(pinning, LIBBPF_PIN_BY_NAME);
} stats_per_pip SEC(".maps");

/*
 * Ring buffer used to communicate file accesses to userspace.
 * We have one of these per runner pid and is held in the map-of-maps file_access_per_pip
 * This map is dynamically created in user space when a pip is about to start
 */
struct file_access_ring_buffer {
    __uint(type, BPF_MAP_TYPE_RINGBUF);
    __uint(max_entries, FILE_ACCESS_RINGBUFFER_SIZE);
    // We set the numa node of the ringbuffer so it matches the one the corresponding runner is pinned to.
    // This improves performance and reduces latency.
    __uint(map_flags, BPF_F_NUMA_NODE);
} file_access_ring_buffer SEC(".maps");

/**
 * Ring buffer used to send debug events to the userspace. Same per-pip settings as the above map
  */
struct debug_ring_buffer {
    __uint(type, BPF_MAP_TYPE_RINGBUF);
    __uint(max_entries, DEBUG_RINGBUFFER_SIZE);
} debug_ring_buffer SEC(".maps");

/**
 * This is a map of maps where the key is a PID of the runner associated with this sandboxed process
 * and its value is the file access ring buffer associated with each pip.
 * We need one file access ring buffer per pip (that is, per runner) since each runner
 * subscribes to its own file access ring buffer and applies file manifest specific logic
 * We set max entries dynamically at creation time
 */
struct {
    __uint(type, BPF_MAP_TYPE_HASH_OF_MAPS);
    __type(key, pid_t);
    // We need all runners to share this map
    __uint(pinning, LIBBPF_PIN_BY_NAME);
    // The max number of entries is the max number of runners that can run concurrently, which is typically way over dimensioned
    // Each entry in this map is a ring buffer - significantly big in theory - so we don't want to preallocate memory for it
    __uint(map_flags, BPF_F_NO_PREALLOC);
    __array(values, struct file_access_ring_buffer);
} file_access_per_pip SEC(".maps");

/**
 * Similar to file_access_per_pip, holds one debug ring buffer per sandboxed process.
 * We set max entries dynamically at creation time
 */
struct {
    __uint(type, BPF_MAP_TYPE_HASH_OF_MAPS);
    __type(key, pid_t);
    // We need all runners to share this map
    __uint(pinning, LIBBPF_PIN_BY_NAME);
    // The max number of entries is the max number of runners that can run concurrently, which is typically way over dimensioned
    // Each entry in this map is a ring buffer - significantly big in theory - so we don't want to preallocate memory for it
    __uint(map_flags, BPF_F_NO_PREALLOC);
    __array(values, struct debug_ring_buffer);
} debug_buffer_per_pip SEC(".maps");

/**
 * Used to hold processes that will breakaway from the sandbox.
 * Used on kernel side only, this map is not exposed to user side
 */
struct {
    __uint(type, BPF_MAP_TYPE_HASH);
    __type(key, pid_t);
    __type(value, pid_t);
    // The upper bound represents the max number of pids that matches a breakaway definition from the time
    // execve is traced to the point where wakeup_new_task happens. So in theory we should be fine with a pretty
    // small number, but considering this is an int -> int map, memory shouldn't be a big concern
    __uint(max_entries, 512);
} breakaway_pids SEC(".maps");

/**
 * Map containing breakaway processes populated from the user side. We have one of this per pip and are held in a
 * map of maps
 */
struct breakaway_processes {
    __uint(type, BPF_MAP_TYPE_ARRAY);
    __uint(key_size, sizeof(pid_t));
    __uint(value_size, sizeof(breakaway_process));
    __uint(max_entries, MAX_BREAKAWAY_PROCESSES);
} breakaway_processes SEC(".maps");

/**
 * Map holding the last accessed path per CPU. In this way we can send paths in incremental mode, just describing the difference
 * wrt the last sent path for a given CPU.
 * Not really exposed to user side, even though this is an inner map that lives in the last_path_per_pip map of maps. This is just
 * because the last path has to be per CPU AND per pip, and having the outer map makes it easier to manage. The outer map is managed
 * from user side, as any other outer map.
 * Max entries is set dynamically at creation time, as we need to know the max CPUs available in the system.
 */
struct last_path_per_cpu {
    __uint(type, BPF_MAP_TYPE_HASH);
    // The key is the current CPU. We are not using a BPF_MAP_TYPE_PERCPU_ARRAY because it is not clear what the PERCPU behavior is
    // when being an inner map
    __type(key, __u32);
    __type(value, char[PATH_MAX]);
    // On creation we set this to be the max number of CPUs available in the system. This is just for the 'template' inner map.
    __uint(max_entries, 1);
} last_path_per_cpu SEC(".maps");

/**
 * Outer map that holds the last path per pip. The ebpf-runner cannot deal with incremental paths across pips since it only has visibility into
 * its corresponding pip. So we scope down the last path per CPU to the pip that is running the ebpf program. The runner will add one inner map on start
 * and remove it on exit (as it happens with all the per_pip maps).
 */
struct {
    __uint(type, BPF_MAP_TYPE_HASH_OF_MAPS);
    __type(key, pid_t);
    // We need all runners to share this map
    __uint(pinning, LIBBPF_PIN_BY_NAME);
    // The max number of entries is the max number of runners that can run concurrently, which is typically way over dimensioned
    // Most pips won't have breakaway processes, so we set the map flags to avoid preallocating memory
    __uint(map_flags, BPF_F_NO_PREALLOC);
    __array(values, struct last_path_per_cpu);
} last_path_per_pip SEC(".maps");

/**
 * Similar to file_access_per_pip, holds one maps of breakaway processes per sandboxed process.
 * We set max entries dynamically at creation time
 */
struct {
    __uint(type, BPF_MAP_TYPE_HASH_OF_MAPS);
    __type(key, pid_t);
    // We need all runners to share this map
    __uint(pinning, LIBBPF_PIN_BY_NAME);
    // The max number of entries is the max number of runners that can run concurrently, which is typically way over dimensioned
    // Most pips won't have breakaway processes, so we set the map flags to avoid preallocating memory
    __uint(map_flags, BPF_F_NO_PREALLOC);
    __array(values, struct breakaway_processes);
} breakaway_processes_per_pip SEC(".maps");

/**
 * Stores a collection of paths that represent untracked scopes. We avoid sending accesses that fall under any path stored in this map.
 * The map is populated from user side based on the pip information. Kernel side only queries the map.
 */
struct untracked_scopes {
    __uint(type, BPF_MAP_TYPE_LPM_TRIE);
    __uint(key_size, sizeof(struct untracked_path_key));
    // We don't really need a value, we are just checking membership
    __uint(value_size, sizeof(short));
    // Set dynamically from user side based on the number of untracked scopes of a given pip. Left here
    // for template matching.
    __uint(max_entries, 1);
    // Block writes from the kernel side. On user side, the map is frozen after untracked scopes are added.
    __uint(map_flags, BPF_F_NO_PREALLOC | BPF_F_RDONLY_PROG);
} untracked_scopes SEC(".maps"); 

/**
 * Holds one untracked_scopes map per sandboxed process.
 * We set the max number of entries dynamically at creation time.
 */
struct {
    __uint(type, BPF_MAP_TYPE_HASH_OF_MAPS);
    __type(key, pid_t);
    // We need all runners to share this map
    __uint(pinning, LIBBPF_PIN_BY_NAME);
    // The max number of entries is the max number of runners that can run concurrently, which is typically way over dimensioned
    // Most pips won't have untracked scopes, so we set the map flags to avoid preallocating memory
    __uint(map_flags, BPF_F_NO_PREALLOC);
    __array(values, struct untracked_scopes);
} untracked_scopes_per_pip SEC(".maps");

/**
 * Holds a temporary per-CPU untracked scope key, so we can construct one for doing lookups.
 */
struct {
    __uint(type, BPF_MAP_TYPE_PERCPU_ARRAY);
    __uint(key_size, sizeof(uint32_t));
    __uint(value_size, sizeof(struct untracked_path_key));
    __uint(max_entries, 1);
} temporary_untracked_scopes SEC(".maps");

// Call this function to report the free capacity of the ring buffer in the kernel debug pipe. For debugging purposes only.
__attribute__((always_inline)) static inline void debug_ringbuffer_capacity(pid_t runner_pid, void* ring_buffer)
{
    ulong avail = bpf_ringbuf_query(ring_buffer, BPF_RB_AVAIL_DATA);
    ulong size = bpf_ringbuf_query(ring_buffer, BPF_RB_RING_SIZE);
    ulong con_pos = bpf_ringbuf_query(ring_buffer, BPF_RB_CONS_POS);
    ulong prod_pos = bpf_ringbuf_query(ring_buffer, BPF_RB_PROD_POS);

    ulong available_percentage = ((size - avail) * 100 )/ size;
    bpf_printk("[%d] Free capacity: %ld%% (con_pos: %ld, prod_pos: %ld)\n", runner_pid, available_percentage, con_pos, prod_pos);
}

/**
 * Attempts to reserve space on the debug ring buffer to report a problem
 */
__attribute__((always_inline)) static inline void report_ring_buffer_error(pid_t runner_pid, const char* error_message) {
    void *debug_ring_buffer = bpf_map_lookup_elem(&debug_buffer_per_pip, &runner_pid);
    if (debug_ring_buffer == NULL) {
        bpf_printk("[ERROR] Couldn't find debug ring buffer for pip %d. Error %s", runner_pid, error_message);
        return;
    }

    ebpf_event_debug *debug_event = bpf_ringbuf_reserve(debug_ring_buffer, sizeof(ebpf_event_debug), 0);

    if (!debug_event) {
        bpf_printk("[ERROR] Unable to reserve debug ring buffer for pip %d and pid %d", runner_pid, bpf_get_current_pid_tgid() >> 32);
        return;
    }

    debug_event->event_type = DEBUG;
    debug_event->pid = bpf_get_current_pid_tgid() >> 32;
    debug_event->runner_pid = runner_pid;
    __builtin_strcpy(debug_event->message, error_message);
    bpf_ringbuf_submit(debug_event, /* flags */ 0);
}

__attribute__((always_inline)) static inline void report_buffer_reservation_failure(pid_t runner_pid, void* ring_buffer)
{
    report_ring_buffer_error(runner_pid, "[ERROR] Unable to reserve ring buffer for pip");
    long avail = bpf_ringbuf_query(ring_buffer, BPF_RB_AVAIL_DATA);
    long size = bpf_ringbuf_query(ring_buffer, BPF_RB_RING_SIZE);
    long con_pos = bpf_ringbuf_query(ring_buffer, BPF_RB_CONS_POS);
    long prod_pos = bpf_ringbuf_query(ring_buffer, BPF_RB_PROD_POS);
    bpf_printk("Buffer reservation failure. [%d] Available data %ld, size %ld, consumer pos %ld, producer ps %ld", runner_pid, avail, size, con_pos, prod_pos);
}

__attribute__((always_inline)) static inline void report_file_access_buffer_not_found(pid_t runner_pid)
{
    report_ring_buffer_error(runner_pid, "[ERROR] Could not find file access ring buffer.");
}

__attribute__((always_inline)) static inline void report_last_path_per_cpu_not_found(pid_t runner_pid)
{
    report_ring_buffer_error(runner_pid, "[ERROR] Could not find last path per CPU.");
}

__attribute__((always_inline)) static inline void report_event_cache_not_found(pid_t runner_pid)
{
    report_ring_buffer_error(runner_pid, "[ERROR] Could not find event cache.");
}

__attribute__((always_inline)) static inline void report_stats_not_found(pid_t runner_pid)
{
    report_ring_buffer_error(runner_pid, "[ERROR] Could not find stats.");
}

__attribute__((always_inline)) static inline void report_string_cache_not_found(pid_t runner_pid)
{
    report_ring_buffer_error(runner_pid, "[ERROR] Could not find string cache.");
}

__attribute__((always_inline)) static inline void report_breakaway_map_not_found(pid_t runner_pid)
{
    report_ring_buffer_error(runner_pid, "[ERROR] Could not find breakaway map.");
}

// Whether the given pid is one we care about (i.e. is part of the pid map we keep). If the pid is valid,
// sets runner_pid to its associated value
__attribute__((always_inline)) static bool is_valid_pid(const pid_t pid, pid_t *runner_pid) {
    pid_t *value = bpf_map_lookup_elem(&pid_map, &pid);
    if (value)
    {
        *runner_pid = *value;
        return true;
    }

    return false;
}

__attribute__((always_inline)) static inline bool monitor_process(const pid_t pid, pid_t runner_pid) {
    struct sandbox_options *options = bpf_map_lookup_elem(&sandbox_options_per_pip, &runner_pid);

    // If for some reason options was not set, assume that child processes are monitored
    if (!options) {
        return true;
    }

    // If this is not the root process, then this flag is not applicable
    // We will always monitor child processes of children
    if (pid == options->root_pid) {
        // Only the first exec on the root process is counted against the monitoring child processes flag
        // If another exec comes on the same process, we will not monitor child processes if the flag set.
        if (!options->root_pid_init_exec_occured) {
            options->root_pid_init_exec_occured = 1;
            return true;
        }
    }

    return options->is_monitoring_child_processes;
}

// Returns the parent pid of the given task
__attribute__((always_inline)) static int get_ppid(struct task_struct* task) {
    int ppid = BPF_CORE_READ(task, real_parent, tgid);

    return ppid;
}

// Used to store temporary paths. We need 2 entries for the case of exec and rename, which require dealing with two paths
// simultaneously
struct
{
    __uint(type, BPF_MAP_TYPE_PERCPU_ARRAY);
    __uint(key_size, sizeof(uint32_t));
    // using PATH_MAX * 2 to keep the verifier happy.
    __uint(value_size, PATH_MAX * 2);
    __uint(max_entries, 2);
} tmp_paths SEC(".maps");

// Useful for retrieving the couple of available temporary paths from tmp_paths
const static int ZERO = 0;
const static int ONE = 1;

// We use one entry per cpu
// Used by deref_path_info, combine_paths, and argv_to_string
// The dereference needs two paths, so the size here is PATH_MAX * 2, and the resulting element is logically split in halves
// More generally, it is very useful to use a PATH_MAX * 2 sized buffer for path-related operations (when two paths are involved, or when
// a temporary path is kept while operating with another one), as the verifier will be happy with the given boundaries
struct
{
    __uint(type, BPF_MAP_TYPE_PERCPU_ARRAY);
    __uint(key_size, sizeof(uint32_t));
    __uint(value_size, PATH_MAX * 2);
    __uint(max_entries, 1);
} derefpaths SEC(".maps");

/**
 * Body of the loop used in deref_path_info.
 * This is used instead of defining it directly in the function because depending on the current kernel version
 * different types of loops must be used.
 *
 * Returns 0 if the loop should continue, 1 if it should return, and 2 if it should break.
 * If the return value is set to 1 then the return code is set in the returncode variable.
 */
__attribute__((always_inline)) static inline int deref_paths_info_loop(
    unsigned int i,
    int *returncode,
    const void **dentry,
    const void **newdentry,
    const void *vfsmount,
    const void **mnt,
    char **dname,
    char *temp,
    int *dlen,
    int *dlen2,
    unsigned int *psize,
    uint32_t *ptsize)
{
    *dname = (char *)BPF_CORE_READ((struct dentry *)*dentry, d_name.name);

    if (!*dname)
    {
        // If we didn't have a mount set, this means we reach the root of the filesystem
        if (vfsmount == NULL)
        {
            return 2; // break
        }

        *returncode = 0;
        return 1; // return
    }
    // store this dentry name in start of second half of our temporary storage
    *dlen = bpf_core_read_str(&temp[PATH_MAX], PATH_MAX, *dname);

    // get parent dentry
    *newdentry = (char *)BPF_CORE_READ((struct dentry *)*dentry, d_parent);

    // Check if the retrieved dname is just a '/'. In that case, we just want to skip it.
    // We will consistently add separators in between afterwards, so we don't want a double slash
    if (!(temp[PATH_MAX] == '/' && *dlen == 2))
    {
        // NOTE: We copy the value of these variables to local variables and then back to the original pointers
        // because the asm code we execute below failed to compile when using the pointers directly.
        int size = *psize;
        int tsize = *ptsize;

        // copy the temporary copy to the first half of our temporary storage, building it backwards from the middle of
        // it
        *dlen2 = bpf_core_read_str(&temp[(PATH_MAX - size - *dlen) & (PATH_MAX - 1)], *dlen & (PATH_MAX - 1), &temp[PATH_MAX]);
        // check if current dentry name is valid
        if (*dlen2 <= 0 || *dlen <= 0 || *dlen >= PATH_MAX || size + *dlen > PATH_MAX)
        {
            *returncode = 0;
            return 1; // return
        }

        if (size > 0)
        {
            asm volatile("%[tsize] = " XSTR(PATH_MAX) "\n"
                        "%[tsize] -= %[size]\n"
                        "%[tsize] -= 1\n"
                        "%[tsize] &= " XSTR(PATH_MAX - 1) "\n"
                        : [size] "+&r"(size), [tsize] "+&r"(tsize)
                        );

            temp[tsize & (PATH_MAX - 1)] = '/';
        }

        size = (size + *dlen2) &
            (PATH_MAX - 1); // by restricting size to PATH_MAX we help the verifier keep the complexity
                            // low enough so that it can analyse the loop without hitting the 1M ceiling

        // Copy back modifications to size and tsize
        *psize = size;
        *ptsize = tsize;
    }

    // check if this is the root of the filesystem or we reach the given mountpoint
    // We always prefer the mountpoint instead of continuing walking up the chain so we honor what the application context
    // is trying to do wrt path lookups
    if (!*newdentry || *dentry == *newdentry || *newdentry == BPF_CORE_READ((struct vfsmount *)vfsmount, mnt_root))
    {
        // check if we're on a mounted partition
        // find mount struct from vfsmount
        const void *parent = BPF_CORE_READ((struct mount *)*mnt, mnt_parent);

        // check if we're at the real root
        if (parent == *mnt)
        {
            return 2; // break
        }

        // move to mount point
        *newdentry = BPF_CORE_READ((struct mount *)*mnt, mnt_mountpoint);
        *mnt = parent;

        // another check for real root
        if (*dentry == *newdentry)
        {
            return 2; // break
        }
    }

    // go up one directory
    *dentry = *newdentry;

    return 0; // continue
}

// Returns a string representation of the content of a struct path (dentry and vfsmount being its two components)
__attribute__((always_inline)) static inline uint32_t deref_path_info(char *dest, const void *dentry, const void *vfsmount)
{
    int dlen, dlen2;
    char *dname = NULL;
    char *temp = NULL;
    unsigned int i;
    unsigned int size = 0;
    const void *path = NULL;
    const void *newdentry = NULL;
    const void *mnt = NULL;
    uint32_t tsize = 0;

    // nullify string in case of error
    dest[0] = 0x00;

    mnt = container_of(vfsmount, struct mount, mnt);

    // retrieve temporary filepath storage
    int index = 0;
    temp = bpf_map_lookup_elem(&derefpaths, &index);
    if (!temp)
    {
        return 0;
    }

    int returncode = 0;
    if (LINUX_KERNEL_VERSION >= KERNEL_VERSION(6, 8, 0))
    {
        // Newer kernels hit the instruction limit when using bpf_for, so we use a regular for loop
        // to avoid hitting the limit.
        for (i = 0; i < FILEPATH_NUMDIRS; i++)
        {
            int loopexit = deref_paths_info_loop(
                i,
                &returncode,
                &dentry,
                &newdentry,
                vfsmount,
                &mnt,
                &dname,
                temp,
                &dlen,
                &dlen2,
                &size,
                &tsize);

            // loopexit can be:
            // 0: continue the loop
            // 1: return with the exit code set in returncode
            // 2: break
            if (loopexit == 1)
            {
                return returncode;
            }
            else if (loopexit == 2)
            {
                break;
            }
        }
    }
    else
    {
        bpf_for(i, 0, FILEPATH_NUMDIRS)
        {
            int loopexit = deref_paths_info_loop(
                i,
                &returncode,
                &dentry,
                &newdentry,
                vfsmount,
                &mnt,
                &dname,
                temp,
                &dlen,
                &dlen2,
                &size,
                &tsize);

            // loopexit can be:
            // 0: continue the loop
            // 1: return with the exit code set in returncode
            // 2: break
            if (loopexit == 1)
            {
                return returncode;
            }
            else if (loopexit == 2)
            {
                break;
            }
        }
    }

    // check if we exhausted the number of directories we can traverse
    if (i == FILEPATH_NUMDIRS)
    {
        // add a '+/' to the start to indicate it's not a full path

        // Following piece of asm is required as clang likes to optimise
        // an increment followed by ANDing with (PATH_MAX -1), into simply
        // XORing with (PATH_MAX -1) and then converting to 32 bits by
        // <<32, >>32. This means the verifier thinks max value is 2^32 -1,
        // instead of (PATH_MAX -1).

        asm volatile("%[size] += 1\n"
                     "%[tsize] = " XSTR(PATH_MAX) "\n"
                     "%[tsize] -= %[size]\n"
                     "%[tsize] &= " XSTR(PATH_MAX - 1) "\n"
                    : [size] "+&r"(size), [tsize] "+&r"(tsize)
                    );
        temp[tsize & (PATH_MAX - 1)] = '/';

        asm volatile("%[size] += 1\n"
                     "%[tsize] -= 1\n"
                     "%[tsize] &= " XSTR(PATH_MAX - 1) "\n"
                    : [size] "+&r"(size), [tsize] "+&r"(tsize)
                    );
        temp[tsize & (PATH_MAX - 1)] = '+';
    }
    else if (size == 0)
    {
        // This means we only found '/' characters along the way. Assume this represents the root dir
        size++;
        temp[(PATH_MAX - size) & (PATH_MAX -1)] = '\0';
        size++;
        temp[(PATH_MAX - size) & (PATH_MAX -1)] = '/';
    }
    else if (size == 1)
    {
        // This means the shortest valid read would be a single null character.
        // assume this represents the root dir
        size++;
        temp[(PATH_MAX - size) & (PATH_MAX -1)] = '/';
    }
    else if (size > 2)
    {
        // size of 2 is simply "/" which is good. Need to check >2.

        // check if starting with '/'
        if (temp[(PATH_MAX - size) & (PATH_MAX - 1)] == '/')
        {
            // check for double / ("//")
            if (temp[(PATH_MAX - (size - 1)) & (PATH_MAX - 1)] == '/')
            {
                size--;
            }
        }
        else
        {
            // add a '/'

            asm volatile("%[size] += 1\n"
                         "%[tsize] = " XSTR(PATH_MAX) "\n"
                         "%[tsize] -= %[size]\n"
                         "%[tsize] &= " XSTR(PATH_MAX - 1) "\n"
                        : [size] "+&r"(size), [tsize] "+&r"(tsize)
                        );

            temp[tsize & (PATH_MAX - 1)] = '/';
        }
    }

    // copy the path from the temporary location to the destination
    dlen = bpf_core_read_str(dest, PATH_MAX, &temp[(PATH_MAX - size) & (PATH_MAX -1)]);

    if (dlen <= 0)
    {
        return 0;
    }

    return dlen;
}

// Returns the mode stored in the corresponding inode
__attribute__((always_inline)) static inline unsigned int get_mode(const struct dentry *dentry) {
    return BPF_CORE_READ(dentry, d_inode, i_mode);
}

// Returns the mode stored in the corresponding inode
__attribute__((always_inline)) static inline unsigned int get_mode_from_file(const struct file *file) {
    return BPF_CORE_READ(file, f_inode, i_mode);
}

// Returns the mode stored in the corresponding inode
__attribute__((always_inline)) static inline unsigned int get_mode_from_path(const struct path *path) {
    return get_mode(BPF_CORE_READ(path, dentry));
}

// Turns a struct path into a string representation of the full path
__attribute__((always_inline)) static inline uint32_t path_to_string(char *dest, const struct path* path)
{
    if (!dest)
    {
        return 0;
    }

    dest[0] = '\0';

    void *dentry = BPF_CORE_READ(path, dentry);
    if (!dentry)
    {
        return 0;
    }

    // Observe de-referencing can work even if the mount is missing, so no null checking here.
    void *vfsmount = BPF_CORE_READ(path, mnt);

    return deref_path_info(dest, dentry, vfsmount);
}

__attribute__((always_inline)) static inline uint32_t fd_to_string(char *fdPath, int fd, const void *task)
{
    void *path = NULL;

    // check if fd is valid
    int maxFds = BPF_CORE_READ((struct task_struct *)task, files, fdt, max_fds);
    if (fd < 0 || fd > MAX_FDS || maxFds <= 0 || fd > maxFds)
    {
        return 0;
    }

    // Retrieve the file descriptor table from the current task
    const void **fdTable = (const void **)BPF_CORE_READ((struct task_struct *)task, files, fdt, fd);
    if (!fdTable)
    {
        return 0;
    }

    // Retrieve the struct file instance that is pointed by the fd
    const struct file *fdfile = NULL;
    if (bpf_core_read(&fdfile, sizeof(fdfile), &fdTable[fd & MAX_FDS]) != READ_OKAY || !fdfile)
    {
        return 0;
    }
    else
    {
        // Resolve the corresponding struct path to a string
        struct path path = BPF_CORE_READ(fdfile, f_path);
        return path_to_string(fdPath, &path);
    }
}

// Combines path and atom, placing the result in path
__attribute__((always_inline)) static inline uint32_t combine_paths(char* path, const char* atom)
{
    uint32_t tsize = 0;

    char *temp_path = NULL;
    int index = 0;
    temp_path = bpf_map_lookup_elem(&derefpaths, &index);
    if (!temp_path)
    {
        return 0;
    }

    // Copy the path to the temporary path. Observe the temporary path has size 2*PATH_MAX, so the
    // verifier will allow putting two paths together without complaining
    int length = bpf_core_read_str(temp_path, PATH_MAX, path);

    // Check whether the last element of the path is already a directory separator, and add one otherwise.
    // Observe that length includes the null character, so 'length - 1' should be pointing to the null separator
    if (temp_path[(length - 2) & (PATH_MAX - 1)] != '/')
    {
        temp_path[(length - 1) & (PATH_MAX - 1)] = '/';
    }
    else
    {
        length--;
    }

    bpf_core_read_str(&temp_path[length & (PATH_MAX - 1)], PATH_MAX, atom);

    // Copy to the final destination
    return bpf_core_read_str(path, PATH_MAX, temp_path);
}

// Returns the current working directory of the given task
__attribute__((always_inline)) static inline uint32_t get_cwd(struct task_struct * task, char* cwd)
{
    struct path pwd_path = BPF_CORE_READ(task, fs, pwd);
    return path_to_string(cwd, &pwd_path);
}

// Returns a string representation of the path carried by file descriptor followed but a filename.
// These input arguments are used to perform a path lookup, which means that the dentry/inode is not resolved
// yet
__attribute__((always_inline)) static inline uint32_t fd_string_to_string(char* path, int fd, const char* filename, bool user_strings)
{
    // Copy the filename to the destination, as a way to bound it to PATH_MAX and keep the verifier happy
    int length = user_strings
        ? bpf_core_read_user_str(path, PATH_MAX, filename)
        : bpf_core_read_str(path, PATH_MAX, filename);

    if (length <= 0)
    {
        return 0;
    }

    // Check if file descriptor is invalid or if the filename is absolute. In those case, the file descriptor is ignored and
    // the final path should be in the filename
    if ((fd < 0 && fd != AT_FDCWD) || path[0] == '/')
    {
        return length;
    }

    // The file descriptor is valid. This could either be AT_FDCWD (the current directory) or a valid handle
    struct task_struct *task = (struct task_struct *)bpf_get_current_task();
    if (fd == AT_FDCWD)
    {
        length = get_cwd(task, path);
    }
    else
    {
        length = fd_to_string(path, fd, task);
    }

    if (length <= 0)
    {
        return 0;
    }

    // We got a resolved directory in path and a relative path on filename. Put them together.
    return combine_paths(path, filename);
}

// Returns a string representation of the path carried by file descriptor followed by a struct filename.
// These input arguments are used to perform a path lookup, which means that the dentry/inode is not resolved
// yet
__attribute__((always_inline)) static inline uint32_t fd_filename_to_string(char* output_path, int fd, const struct filename* filename_struct) {
    const char *filename = BPF_CORE_READ(filename_struct, name);

    return fd_string_to_string(output_path, fd, filename, /* user_strings */ false);
}

// Returns a string representation of the path carried by a nameidata instance.
// Observe that nameidata is typically used to perform a path lookup, which means that the dentry/inode is not resolved
// yet (and might not even exist in case of an absent path). The consequence of this is that we have to do extra work to
// put the final path together
__attribute__((always_inline)) static inline uint32_t nameidata_to_string(char* path, const struct nameidata* ns)
{
    // A nameidata contains a file descriptor maybe pointing to a directory (dfd) and a name component which may contain a filename
    // or full path
    int fd = BPF_CORE_READ(ns, dfd);
    const struct filename *filename = BPF_CORE_READ(ns, name);

    return fd_filename_to_string(path, fd, filename);
}

// Returns the path to the current executable by inspecting the given task
__attribute__((always_inline)) static inline unsigned int get_task_exec_path(struct task_struct* task, char* dest)
{
    dest[0] = '\0';

    struct mm_struct* mm = BPF_CORE_READ(task, mm);
    if (mm)
    {
        struct path path = BPF_CORE_READ(mm, exe_file, f_path);
        return path_to_string(dest, &path);
    }

    return 0;
}

/**
 * Returns whether the mode is set and it is not a file, nor a directory neither a symlink
 */
__attribute__((always_inline)) static inline bool is_non_file(mode_t mode)
{
    return mode != 0 && !S_ISDIR(mode) && !S_ISREG(mode) && !S_ISLNK(mode);
}

/**
 * argv_to_string() - Converts an argv array to a string representation.
 * @argv: The argv array to convert. This pointer is in user memory.
 * @dest: The destination buffer to store the resulting string. This pointer is in kernel memory.
 *
 * Each argument will be separated by a space.
 * Final string is null terminated.
 * NOTE: the destination buffer is assumed to always be PATH_MAX in size.
 */
static int argv_to_string(char *const *argv, char* dest)
{
    if (!argv) {
        return 0;
    }

    int index = 0;
    int remaining_length = PATH_MAX - 1;

    // Using temporary path storage here to read each argument
    char *temp = bpf_map_lookup_elem(&derefpaths, &index);
    if (temp == NULL) {
        return 0;
    }

    for (int i = 0; i < MAX_ARGV_ARGUMENTS; i++) {
        char *arg;
        // Get a pointer to the current argument
        if (bpf_probe_read_user(&arg, sizeof(arg), &argv[i]) != 0) {
            break;
        }

        // Copy string to temporary location starting on the second half of the string
        long copied_len = bpf_probe_read_user_str(&temp[PATH_MAX], PATH_MAX, arg);
        if (copied_len <= 0) {
            break;
        }

        // Copy the string to the first half of the temporary array to concatenate it with the rest of the arguments
        // We'll add a space here if it's the second argument onwards
        if (i > 0) {
            temp[index & (PATH_MAX - 1)] = ' ';
            index++;
        }

        // NOTE: this is a kernel str because we are copying from the derefpaths map now
        long copied_len2 = bpf_probe_read_kernel_str(&temp[index & (PATH_MAX - 1)], PATH_MAX, &temp[PATH_MAX]);
        index += copied_len2 - 1; // -1 since this an index, not a length
        if (index >= PATH_MAX - 1) {
            break;
        }
    }

    // Copy the path to the final destination
    // index + 1 is used here because index is used as a length not an index here
    return bpf_probe_read_kernel_str(dest, ((index + 1) & (PATH_MAX - 1)), &temp[0]);
}

/**
 * breakaway_map_callback() - Callback function to check if the current process needs to breakaway.
 *
 * This function is called for each entry in the breakaway processes map.
 * It checks if the current process matches the breakaway process criteria.
 */
static long breakaway_map_callback(struct bpf_map *map, const uint32_t *key, breakaway_process *value, exec_event_metadata **ctx) {
    exec_event_metadata *event = *ctx;

    if (value->tool[0] == '\0') {
        // Reached the end of the map, the rest of the elements are not populated
        return 1;
    }

    char *toolname = &event->exe_path[event->exe_name_start_index & (PATH_MAX - 1)];

    bool exe_match = string_contains(toolname, event->exe_name_len, value->tool, value->tool_len, /* case_sensitive */ true);
    // Args can be ignored if they weren't specified in the breakaway process map
     bool args_match = value->arguments[0] == '\0'
         ? true
         : string_contains(value->arguments, value->arguments_len, event->args, event->args_len, /* case_sensitive */ !value->args_ignore_case);

    event->needs_breakaway = exe_match && args_match;

    // If we already found a match, we can return 1 here to terminate the loop early
    return event->needs_breakaway ? 1 : 0;
}

/**
 * basename_loop_callback() - Loop callback to find the basename of the executable path.
 *
 * The basename is the last component of the path, which is the executable name.
 * Finds the starting index of the basename in the path.
 */
static long basename_loop_callback(u64 index, exec_event_metadata **ctx) {
    exec_event_metadata *event = *ctx;
    u64 i = (event->exe_path_len - index) & (PATH_MAX - 1);

    // Since bpf_loop can only start at 0 and increment, keep track of the last '/'
    // If the next character is a '\0' then it's a trailing '/' which can be ignored.
    if (event->exe_path[i] == '/' && event->exe_path[i + 1 & (PATH_MAX - 1)] != '\0') {
        event->exe_name_start_index = i + 1;
        return 1;
    }

    return 0;
}

/**
 * process_needs_breakaway() - Verifies if the given process needs to breakaway by updating
 * the given event `needs_breakaway` field. Returns non-zero if the breakaway map could not be retrieved
 *
 * This function uses bpf_loop instead of a for/while loop because using an escape
 * hatch allows us to reduce the amount of time needed to verify the program.
 * Additionally, bpf_loop lets us have much bigger loops without hitting the instruction limit.
 */
static int process_needs_breakaway(exec_event_metadata *event, pid_t runner_pid) {
    event->needs_breakaway = false;

    // The path that we have is the full path to the executable
    // Breakaway processes match with the executable path atom so we need to find the basename
    bpf_loop((event->exe_path_len & (PATH_MAX - 1)), basename_loop_callback, &event, 0);

    // -1 to Ignore the null terminating character
    event->exe_name_len = event->exe_path_len - event->exe_name_start_index - 1;

    // Retrieve the corresponding breakaway map given the runner id
    void *breakaway_processes = bpf_map_lookup_elem(&breakaway_processes_per_pip, &runner_pid);
    if (breakaway_processes == NULL) {
        return -1;
    }

    // Check if the process needs to breakaway
    bpf_for_each_map_elem(breakaway_processes, breakaway_map_callback, &event, 0);

    return 0;
}

/**
 * write_metadata() - Writes the given metadata to the start of the given dynptr.
 * @ptr: The dynptr to write the metadata to.
 * @metadata_to_write: The metadata to write.
 * @prefix_len: The length of the common prefix between the current path and the last path for the current CPU.
 *
 * Returns 0 on success, -1 on failure.
 */
__attribute__((always_inline)) static int write_metadata(
    struct bpf_dynptr* ptr,
    ebpf_event_metadata *metadata_to_write,
    unsigned short prefix_len) {

    ebpf_event_metadata* metadata = (ebpf_event_metadata*) bpf_dynptr_data(
        ptr, /* offset */ 0, sizeof(ebpf_event_metadata));

    if (metadata == NULL) {
        return -1;
    }

    metadata->event_type = metadata_to_write->event_type;
    metadata->processor_id = metadata_to_write->processor_id;
    metadata->operation_type = metadata_to_write->operation_type;
    metadata->kernel_function = metadata_to_write->kernel_function;
    metadata->pid = metadata_to_write->pid;
    metadata->child_pid = metadata_to_write->child_pid;
    metadata->mode = metadata_to_write->mode;
    metadata->error = metadata_to_write->error;
    metadata->source_path_incremental_length = prefix_len;

    return 0;
}

/**
 * write_and_submit_access() - Writes the given path to the given dynptr and submits it to the ring buffer.
 * @runner_pid: The PID of the runner process.
 * @file_access_ring_buffer: The ring buffer to write the path to.
 * @metadata: The metadata to write.
 * @prefix_len: The length of the common prefix between the current path and the last path for the current CPU.
 * @path_to_write: The path to write.
 * @path_to_write_length: The length of the path to write.
 */
__attribute__((always_inline)) static void write_and_submit_access(
    pid_t runner_pid,
    void* file_access_ring_buffer,
    ebpf_event_metadata *metadata,
    unsigned short prefix_len,
    char *path_to_write,
    int path_to_write_length) {

    // Reserve space in the ring buffer for the metadata and the path
    struct bpf_dynptr ptr;
    if (bpf_ringbuf_reserve_dynptr(
        file_access_ring_buffer,
        (sizeof(ebpf_event_metadata) + path_to_write_length) & (PATH_MAX - 1),
        /* flags*/ 0,
        &ptr)) {

        report_buffer_reservation_failure(runner_pid, file_access_ring_buffer);
        bpf_ringbuf_discard_dynptr(&ptr, 0);
        return;
    }

    // Write the metadata at the start of the dynptr
    if (write_metadata(&ptr, metadata, prefix_len))
    {
        report_ring_buffer_error(runner_pid, "[ERROR] Unable to write metadata to dynptr");
        bpf_ringbuf_discard_dynptr(&ptr, 0);
        return;
    }

    // Write the path after the metadata
    if (path_to_write_length < 0 || path_to_write_length >= PATH_MAX || bpf_dynptr_write(&ptr, sizeof(ebpf_event_metadata), path_to_write, path_to_write_length, 0)) {
        report_ring_buffer_error(runner_pid, "[ERROR] Unable to write path to dynptr");
        bpf_ringbuf_discard_dynptr(&ptr, 0);
        return;
    }

    bpf_ringbuf_submit_dynptr(&ptr, get_flags(file_access_ring_buffer));
}

/**
 * Context used in write_path_incremental_callback
 */
struct write_path_incremental_context {
    pid_t runner_pid;
    void* file_access_ring_buffer;
    ebpf_event_metadata *metadata;
    char *path_to_write;
    int path_to_write_length;
    const char *last_path;
    unsigned short prefix_len; // Length of the common prefix.
};

/**
 * write_path_incremental_callback() - Callback function used in write_path_incremental to find the common prefix
 * between the last path and the current path, and write the incremental part to the dynptr.
 */
static long write_path_incremental_callback(u64 index, struct write_path_incremental_context *ctx) {
    int the_index = index & (PATH_MAX - 1);

    char c1 = ctx->last_path[the_index];
    char c2 = ctx->path_to_write[the_index];

    // If characters don't match, stop checking further and write the remaining of the string.
    // If we reached the end of either string, stop checking (the PATH_MAX - 1 case is just to keep the verifier happy,
    // we should always reach a \0 before reaching PATH_MAX).
    if (c1 != c2 || c1 == '\0' || index == PATH_MAX - 1) {
        int incremental_length = ctx->path_to_write_length - index;
        char *incremental_path = &ctx->path_to_write[index & (PATH_MAX - 1)];

        write_and_submit_access(
            ctx->runner_pid,
            ctx->file_access_ring_buffer,
            ctx->metadata,
            ctx->prefix_len,
            incremental_path,
            incremental_length);
        return 1;
    }

    // Characters match, increment the prefix length.
    ctx->prefix_len++;

    // We haven't reached the end of the string. Continue looping.
    return 0;
}

/**
 * write_path_incremental() - Writes a single path event to the ring buffer, using incremental encoding based on the last path
 * for the current CPU.
 *
 * One could argue that it'd be a better approach to just compute the index of the common prefix without coupling it with actually writing the incremental
 * path (so we don't need to carry all those extra arguments across layers), but the verifier didn't like that approach.
 *
 * @path_to_write: The full path to write.
 * @path_to_write_length: The length of the path to write.
 * @runner_pid: The PID of the runner process.
 * @dynptr: The dynptr to write the incremental path to.
  */
__attribute__((always_inline)) static void write_single_path_event_incremental(
    pid_t runner_pid,
    void* file_access_ring_buffer,
    ebpf_event_metadata *metadata,
    char *path_to_write,
    int path_to_write_length) {

    // Retrieve the per-pip last path outer map
    void *last_path_per_cpu = bpf_map_lookup_elem(&last_path_per_pip, &runner_pid);
    if (last_path_per_cpu == NULL) {
        report_last_path_per_cpu_not_found(runner_pid);
        return;
    }

    // Retrieve the per-CPU last path inner map for the given pip
    __u32 cpu_id = metadata->processor_id;
    const char *last_path = bpf_map_lookup_elem(last_path_per_cpu, &cpu_id);

    // Check whether there is a last path for this CPU. If not, write the full path
    if (last_path == NULL || last_path[0] == '\0') {
        write_and_submit_access(
            runner_pid,
            file_access_ring_buffer,
            metadata,
            /* prefix_len */ 0,
            path_to_write,
            path_to_write_length);
    }
    else {
        // Otherwise, find the common prefix with respect to the last path and write the incremental part
        struct write_path_incremental_context ctx = {
            .runner_pid = runner_pid,
            .path_to_write = path_to_write,
            .path_to_write_length = path_to_write_length,
            .last_path = last_path,
            .file_access_ring_buffer = file_access_ring_buffer,
            .metadata = metadata,
            .prefix_len = 0,
        };

        bpf_loop(PATH_MAX, write_path_incremental_callback, &ctx, 0);
    }

    // Update the last path for this CPU with the current path
    bpf_map_update_elem(last_path_per_cpu, &cpu_id, path_to_write, BPF_ANY);
}

__attribute__((always_inline)) static bool is_path_untracked(int runner_pid, const char* path, int path_length) {
    // Untracked scopes might not be there if the pip didn't have any. So it is not an error if we don't find it.
    void *untracked_scopes = bpf_map_lookup_elem(&untracked_scopes_per_pip, &runner_pid);
    if (untracked_scopes == NULL) {
        return false;
    }
    
    // We don't store the null terminator in the trie, so use path_length - 1
    int key_length = path_length - 1 <= MAX_LPM_PATH_LEN ? path_length - 1 : MAX_LPM_PATH_LEN;

    // Retrieve a temporary untracked path key, so we can build the lookup key
    struct untracked_path_key* key = bpf_map_lookup_elem(&temporary_untracked_scopes, &ZERO);
    if (!key) {
        report_ring_buffer_error(runner_pid, "[ERROR] Could not find temporary untracked path key.");
        return false;
    }
    
    // If the path is smaller than the maximum length, nullify the remaining space so we keep the key deterministic
    if (key_length < MAX_LPM_PATH_LEN) {
        nullify_string(key->path, MAX_LPM_PATH_LEN);
    }

    // Copy over the path (maybe truncated) and its byte aligned length
    bpf_core_read(
        &key->path, 
        key_length < MAX_LPM_PATH_LEN 
            // The destination is already nullified, so we can copy up to (and excluding) the null terminator
            ? key_length & (MAX_LPM_PATH_LEN - 1)
            // The path being evaluated is longer than the max key. So we populate the key based on its max length
            : MAX_LPM_PATH_LEN,
        path);
    key->prefixlen = key_length * 8; // Length in bits

    if (bpf_map_lookup_elem(untracked_scopes, key) != NULL) {
        // The path is untracked. Update stats accordingly.
        struct pip_stats *stats = bpf_map_lookup_elem(&stats_per_pip, &runner_pid);
        if (!stats) {
            report_stats_not_found(runner_pid);
            return true;
        }

        __sync_fetch_and_add(&stats->untracked_path_count, 1);
        __sync_fetch_and_add(&stats->untracked_path_bytes, path_length);
        return true;
    }

    return false;
}

/**
 * submit_file_access() - Submits a file access event to the ring buffer for the given runner process.
 *
 * This function uses an incremental encoding for representing the file path. Single-file accesses are by far
 * the most common type of accesses and where the vast majority of file access events occur, so saving
 * space in the ring buffer for these events is crucial.
 * The main idea for the incremental encoding is that we store the last path that was sent for the current CPU. And
 * the next path for the same CPU is described in terms of the last one: we compute the common prefix between both and
 * only send the length of the common prefix plus the rest of the new path. Reconstructing the original path on user side
 * needs to follow an equivalent treatment, where we also store the last path per CPU. The decision to make the CPU the
 * key for the last path map relies on the fact that in this way 'last' is well defined and we don't have to deal with
 * any concurrency issues: for a given CPU user side will get all its events in the same order compared to kernel side, so
 * the storage and retrieval of the last path can be exactly mimicked.
 *
 * @runner_pid: The PID of the runner process.
 * @operation_type: The type of operation
 * @kernel_function: The kernel function that triggered the event.
 * @pid: The PID of the process that performed the operation.
 * @child_pid: The PID of the child process, if applicable.
 * @mode: The mode of the file (e.g., S_IFREG, S_IFDIR, etc.).
 * @error: The error code, if any.
 * @path: The path to the file.
 * @path_length: The length of the path.
 */
__attribute__((always_inline)) static void submit_file_access(
    pid_t runner_pid,
    enum operation_type operation_type,
    enum kernel_function kernel_function,
    int pid,
    int child_pid,
    unsigned int mode,
    int error,
    char* path,
    int path_length) {

    if (path_length < 0 || path_length >= PATH_MAX) {
        report_ring_buffer_error(runner_pid, "[ERROR] Path length is invalid");
        return;
    }

    // Do not bother sending the path to user side if it is untracked
    // We make an exception with clone/exit/breakaway operations, as those are used by
    // managed side to track the lifecycle of processes (even if those operations end up being
    // ignored from a cache fingerprint standpoint)
    if (operation_type != kClone && 
        operation_type != kExit && 
        operation_type != kBreakAway && 
        is_path_untracked(runner_pid, path, path_length)) {
        return;
    }

    void *file_access_ring_buffer = bpf_map_lookup_elem(&file_access_per_pip, &runner_pid);
    if (file_access_ring_buffer == NULL) {
        report_file_access_buffer_not_found(runner_pid);
        return;
    }

    ebpf_event_metadata metadata = {
        .event_type = SINGLE_PATH,
        .operation_type = operation_type,
        .kernel_function = kernel_function,
        .pid = pid,
        .child_pid = child_pid,
        .mode = mode,
        .error = error,
        .processor_id = bpf_get_smp_processor_id()
    };

    // We always use incremental encoding for single path events
    write_single_path_event_incremental(
        runner_pid,
        file_access_ring_buffer,
        &metadata,
        path,
        path_length);
}

/**
 * submit_file_access_double() - Submits a file access event with two paths to the ring buffer for the given runner process.
 * This function does not use incremental encoding, as double path events are less common.
 * @runner_pid: The PID of the runner process.
 * @operation_type: The type of operation
 * @kernel_function: The kernel function that triggered the event.
 * @pid: The PID of the process that performed the operation.
 * @child_pid: The PID of the child process, if applicable.
 * @mode: The mode of the file (e.g., S_IFREG, S_IFDIR, etc.).
 * @error: The error code, if any.
 * @src_path: The source path to the file.
 * @src_path_length: The length of the source path.
 * @dst_path: The destination path to the file.
 * @dst_path_length: The length of the destination path.
 */
__attribute__((always_inline)) static void submit_file_access_double(
    pid_t runner_pid,
    enum operation_type operation_type,
    enum kernel_function kernel_function,
    int pid,
    int child_pid,
    unsigned int mode,
    int error,
    char* src_path,
    int src_path_length,
    char* dst_path,
    int dst_path_length) {

    // Do not bother sending the path to user side if both paths are untracked
    if (is_path_untracked(runner_pid, src_path, src_path_length) &&
        is_path_untracked(runner_pid, dst_path, dst_path_length)) {
        return;
    }

    void *file_access_ring_buffer = bpf_map_lookup_elem(&file_access_per_pip, &runner_pid);
    if (file_access_ring_buffer == NULL) {
        report_file_access_buffer_not_found(runner_pid);
        return;
    }
    struct bpf_dynptr ptr;
    unsigned int reservation_size = sizeof(ebpf_event_metadata) + sizeof(int) + src_path_length + dst_path_length;
    if (bpf_ringbuf_reserve_dynptr(
            file_access_ring_buffer,
            reservation_size,
            /* flags */ 0,
            &ptr)) {
        report_buffer_reservation_failure(runner_pid, file_access_ring_buffer);
        bpf_ringbuf_discard_dynptr(&ptr, 0);
        return;
    }

    /* Expose a 'metadata' value from the underlying dynamic object just for ease of use */
    /* (we can do this because it is a fixed size) */
    ebpf_event_metadata* metadata = (ebpf_event_metadata*) bpf_dynptr_data(
        &ptr, /* offset */ 0, sizeof(ebpf_event_metadata));

    if (metadata == NULL) {
        report_ring_buffer_error(runner_pid, "[ERROR] Unable to retrieve metadata from dynptr");
        bpf_ringbuf_discard_dynptr(&ptr, 0);
        return;
    }
    metadata->event_type = DOUBLE_PATH;
    metadata->processor_id = bpf_get_smp_processor_id();
    // Double path events are not very common, so we don't do incremental encoding here
    // We might consider doing it in the future if we find a use case, but we'd probably also need
    // the equivalent length field for the dst path and an alternative metadata to not force this new field in
    // all other event types
    metadata->source_path_incremental_length = 0;
    metadata->kernel_function = kernel_function;
    metadata->pid = pid;
    metadata->child_pid = child_pid;
    metadata->operation_type = operation_type;
    metadata->mode = mode;
    metadata->error = error;

    // Write the src path length field, which is the immediate next one after the metadata in a double event
    if (bpf_dynptr_write(&ptr, sizeof(ebpf_event_metadata), &src_path_length, sizeof(int), /* flags*/ 0)) {
        report_ring_buffer_error(runner_pid, "[ERROR] Unable to write src path length to dynptr");
        bpf_ringbuf_discard_dynptr(&ptr, 0);
        return;
    }

    unsigned int offset = sizeof(ebpf_event_metadata) + sizeof(int);
    unsigned int max_offset = sizeof(ebpf_event_metadata) + sizeof(int) + PATH_MAX + PATH_MAX; // Upper bound for the offset

    // Write the src path to the dynamic structure
    // Some of these checks are redundant, but they are there to make the verifier happy
    if (src_path_length <= 0
        || src_path_length >= PATH_MAX
        || offset >= reservation_size
        || offset >= max_offset
        || bpf_dynptr_write(&ptr, offset, src_path, src_path_length, 0))
    {
        report_ring_buffer_error(runner_pid, "[ERROR] Unable to write src path to dynptr");
        bpf_ringbuf_discard_dynptr(&ptr, 0);
        return;
    }

    offset = sizeof(ebpf_event_metadata) + sizeof(int) + src_path_length;

    // Write the dst path to the dynamic structure
    if (dst_path_length <= 0
        || dst_path_length >= PATH_MAX
        || offset >= reservation_size
        || offset >= max_offset
        || bpf_dynptr_write(&ptr, offset, dst_path, dst_path_length, 0))
    {
        report_ring_buffer_error(runner_pid, "[ERROR] Unable to write dst path to dynptr");
        bpf_ringbuf_discard_dynptr(&ptr, 0);
        return;
    }

    bpf_ringbuf_submit_dynptr(&ptr, get_flags(file_access_ring_buffer));
}

/**
 * submit_exec() - Submits an exec event to the ring buffer for the given runner process.
 * Exec events are special because they carry two variable-length strings: the executable path and the arguments.
 * To handle this, we use a dynamic structure that can accommodate both strings.
 * @runner_pid: The PID of the runner process.
 * @syscall: The syscall that triggered the exec event.
 * @pid: The PID of the process that performed the exec.
 * @exe_path: The path to the executable.
 * @exe_path_length: The length of the executable path.
 * @args: The arguments passed to the executable.
 * @args_length: The length of the arguments string.
 */
__attribute__((always_inline)) static void submit_exec(
    pid_t runner_pid,
    enum kernel_function syscall,
    int pid,
    char* exe_path,
    int exe_path_length,
    char* args,
    int args_length) {

    // We explicitly don't check for untracked paths here. Execs are used on managed side to track process lifecycles
    // and we want to make sure we always get them, even if the executable is untracked.

   void *file_access_ring_buffer = bpf_map_lookup_elem(&file_access_per_pip, &runner_pid);
    if (file_access_ring_buffer == NULL) {
        report_file_access_buffer_not_found(runner_pid);
        return;
    }

    /* The event is a dynamic structure, so we need to reserve space for the metadata and the paths */
    /* Perform a dynamic reservation, enough for the metadata and the paths we are about to send */
    struct bpf_dynptr ptr;
    if (bpf_ringbuf_reserve_dynptr(
        file_access_ring_buffer,
        (sizeof(ebpf_event_metadata) + sizeof(int) + exe_path_length + args_length),
        /* flags*/ 0,
        &ptr)) {

        report_buffer_reservation_failure(runner_pid, file_access_ring_buffer);
        bpf_ringbuf_discard_dynptr(&ptr, 0);
        return;
    }

    /* Expose a 'metadata' value from the underlying dynamic object just for ease of use */
    /* (we can do this because it is a fixed size) */
    ebpf_event_metadata* metadata = (ebpf_event_metadata*) bpf_dynptr_data(
        &ptr, /* offset */ 0, sizeof(ebpf_event_metadata));

    if (metadata == NULL) {
        report_ring_buffer_error(runner_pid, "[ERROR] Unable to retrieve metadata from dynptr");
        bpf_ringbuf_discard_dynptr(&ptr, 0);
        return;
    }

    metadata->event_type = EXEC;
    metadata->processor_id = bpf_get_smp_processor_id();
    // Exec events are not very common, so we don't do incremental encoding here
    metadata->source_path_incremental_length = 0;
    metadata->kernel_function = syscall;
    metadata->operation_type = kExec;
    metadata->pid = pid;
    metadata->error = 0;
    metadata->mode = S_IFREG;

    // Write the path_length field, which is the immediate next one after the metadata in an exec event
    if (bpf_dynptr_write(&ptr, sizeof(ebpf_event_metadata), &exe_path_length, sizeof(int), /* flags*/ 0)) {
        report_ring_buffer_error(runner_pid, "[ERROR] Unable to write exe path length to dynptr");
        bpf_ringbuf_discard_dynptr(&ptr, 0);
        return;
    }

    // Write the exe path to the dynamic structure
    if (bpf_dynptr_write(&ptr, sizeof(ebpf_event_metadata) + sizeof(int), exe_path, exe_path_length & (PATH_MAX - 1), 0))
    {
        report_ring_buffer_error(runner_pid, "[ERROR] Unable to write exec path to dynptr");
        bpf_ringbuf_discard_dynptr(&ptr, 0);
        return;
    }

    // Write the args to the dynamic structure
    if (bpf_dynptr_write(&ptr, sizeof(ebpf_event_metadata) + sizeof(int) + exe_path_length, args, args_length & (PATH_MAX - 1), 0))
    {
        report_ring_buffer_error(runner_pid, "[ERROR] Unable to write args to dynptr");
        bpf_ringbuf_discard_dynptr(&ptr, 0);
        return;
    }

    bpf_ringbuf_submit_dynptr(&ptr, get_flags(file_access_ring_buffer));
}


#endif // __PUBLIC_SRC_SANDBOX_LINUX_EBPF_EBPFUTILITIES_H