// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: GPL-2.0 OR MIT

/**
 * vmlinux.h should always be the first include
 */
#include "vmlinux.h"

#include "bpf/bpf_core_read.h"
#include "bpf/bpf_helpers.h"
#include "bpf/bpf_tracing.h"
#include "ebpfcommon.h"
#include "ebpfutilities.h"
#include "eventcache.h"
#include "kernelfunctions.h"

char LICENSE[] SEC("license") = "Dual MIT/GPL";

// TODO: remove magic numbers?
const long wakeup_data_size = 4096 * 128;
static __always_inline long get_flags(void *ringbuffer) {
    long sz = bpf_ringbuf_query(ringbuffer, BPF_RB_AVAIL_DATA);
    return sz >= wakeup_data_size ? BPF_RB_FORCE_WAKEUP : BPF_RB_NO_WAKEUP;
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

__attribute__((always_inline)) static inline void report_event_cache_not_found(pid_t runner_pid)
{
    report_ring_buffer_error(runner_pid, "[ERROR] Could not find event cache.");
}

__attribute__((always_inline)) static inline void report_breakaway_map_not_found(pid_t runner_pid)
{
    report_ring_buffer_error(runner_pid, "[ERROR] Could not find breakaway map.");
}

// Call this function to report the free capacity of the ring buffer in the kernel debug pipe. For debugging purposes only.
__attribute__((always_inline)) static inline void debug_ringbuffer_capacity(pid_t runner_pid, void* ring_buffer)
{
    ulong avail = bpf_ringbuf_query(ring_buffer, BPF_RB_AVAIL_DATA);
    ulong size = bpf_ringbuf_query(ring_buffer, BPF_RB_RING_SIZE);

    ulong available_percentage = ((size - avail) * 100 )/ size;
    bpf_printk("[%d] Free capacity: %ld%%\n", runner_pid, available_percentage);
}

/**
 * Attempts to reserve the ring buffer for a file access.
 *
 * If the ring buffer reserve call fails, then a debug event is sent to the debug buffer.
 * If reserving the debug buffer also fails, then we write to the trace pipe.
 */
#define RESERVE_SUBMIT_FILE_ACCESS(runner_pid, path_length, code)                                               \
{                                                                                                               \
    if (path_length < 0 || path_length >= PATH_MAX) {                                                           \
        report_ring_buffer_error(runner_pid, "[ERROR] Path length is invalid");                                 \
        return 0;                                                                                               \
    }                                                                                                           \
                                                                                                                \
    void *file_access_ring_buffer = bpf_map_lookup_elem(&file_access_per_pip, &runner_pid);                     \
    if (file_access_ring_buffer == NULL) {                                                                      \
        report_file_access_buffer_not_found(runner_pid);                                                        \
        return 0;                                                                                               \
    }                                                                                                           \
                                                                                                                \
    /* Perform a dynamic reservation, enough for the metadata and the path length we are about to send */       \
    struct bpf_dynptr ptr;                                                                                      \
    if (bpf_ringbuf_reserve_dynptr(                                                                             \
        file_access_ring_buffer,                                                                                \
        (sizeof(ebpf_event_metadata) + path_length) & (PATH_MAX - 1),                                           \
        /* flags*/ 0,                                                                                           \
        &ptr)) {                                                                                                \
                                                                                                                \
        report_buffer_reservation_failure(runner_pid, file_access_ring_buffer);                                 \
        bpf_ringbuf_discard_dynptr(&ptr, 0);                                                                    \
        return 0;                                                                                               \
    }                                                                                                           \
                                                                                                                \
    /* Expose a 'metadata' value from the underlying dynamic object to the macro code just for ease of use */   \
    /* (we can do this because it is a fixed size) */                                                           \
    ebpf_event_metadata* metadata = (ebpf_event_metadata*) bpf_dynptr_data(                                     \
        &ptr, /* offset */ 0, sizeof(ebpf_event_metadata));                                                     \
                                                                                                                \
    if (metadata == NULL) {                                                                                     \
        report_ring_buffer_error(runner_pid, "[ERROR] Unable to retrieve metadata from dynptr");                \
        bpf_ringbuf_discard_dynptr(&ptr, 0);                                                                    \
        return 0;                                                                                               \
    }                                                                                                           \
                                                                                                                \
    metadata->event_type = SINGLE_PATH;                                                                         \
    code                                                                                                        \
                                                                                                                \
    bpf_ringbuf_submit_dynptr(&ptr, get_flags(file_access_ring_buffer));                                        \
}

/**
 * Writes a path to the dynamic structure that we are going to send out representing a single path file access.
 * Designed to be used inside the code block provided to RESERVE_SUBMIT_FILE_ACCESS.
 */
#define WRITE_SRC_PATH(path)                                                                                                                            \
    if (path_length < 0 || path_length >= PATH_MAX || bpf_dynptr_write(&ptr, sizeof(ebpf_event_metadata), path, path_length, 0))                        \
    {                                                                                                                                                   \
        report_ring_buffer_error(runner_pid, "[ERROR] Unable to write path to dynptr");                                                                 \
        bpf_ringbuf_discard_dynptr(&ptr, 0);                                                                                                            \
        return 0;                                                                                                                                       \
    }

/**
 * Check against the event cache for the given operation+path before reserving and sending out the event.
 * This is very useful for operations where the same path is sent multiple times within a short period of time.
 * E.g. writing to a given file using multiple write calls is a good example of this. We don't necessarily need
 * to make each traced call go through this, but it is generally a good idea assuming we have to the corresponding
 * struct path associated with the operation
 */
#define RESERVE_SUBMIT_FILE_ACCESS_WITH_CACHE(operation, path, path_length, runner_pid, code)                       \
{                                                                                                                   \
    if (should_send_path(runner_pid, operation, path))                                                              \
    {                                                                                                               \
        RESERVE_SUBMIT_FILE_ACCESS(runner_pid, path_length, code)                                                   \
    }                                                                                                               \
}

#define RESERVE_SUBMIT_FILE_ACCESS_DOUBLE(runner_pid, src_path_length, dst_path_length, code)                       \
{                                                                                                                   \
    void *file_access_ring_buffer = bpf_map_lookup_elem(&file_access_per_pip, &runner_pid);                         \
    if (file_access_ring_buffer == NULL) {                                                                          \
        report_file_access_buffer_not_found(runner_pid);                                                            \
        return 0;                                                                                                   \
    }                                                                                                               \
    struct bpf_dynptr ptr;                                                                                          \
    unsigned int reservation_size = sizeof(ebpf_event_metadata) + sizeof(int) + src_path_length + dst_path_length;  \
    if (bpf_ringbuf_reserve_dynptr(                                                                                 \
            file_access_ring_buffer,                                                                                \
            reservation_size,                                                                                       \
            /* flags*/ 0,                                                                                           \
            &ptr)) {                                                                                                \
        report_buffer_reservation_failure(runner_pid, file_access_ring_buffer);                                     \
        bpf_ringbuf_discard_dynptr(&ptr, 0);                                                                        \
        return 0;                                                                                                   \
    }                                                                                                               \
                                                                                                                    \
    /* Expose a 'metadata' value from the underlying dynamic object to the macro code just for ease of use */       \
    /* (we can do this because it is a fixed size) */                                                               \
    ebpf_event_metadata* metadata = (ebpf_event_metadata*) bpf_dynptr_data(                                         \
        &ptr, /* offset */ 0, sizeof(ebpf_event_metadata));                                                         \
                                                                                                                    \
    if (metadata == NULL) {                                                                                         \
        report_ring_buffer_error(runner_pid, "[ERROR] Unable to retrieve metadata from dynptr");                    \
        bpf_ringbuf_discard_dynptr(&ptr, 0);                                                                        \
        return 0;                                                                                                   \
    }                                                                                                               \
    metadata->event_type = DOUBLE_PATH;                                                                             \
                                                                                                                    \
    code                                                                                                            \
                                                                                                                    \
    bpf_ringbuf_submit_dynptr(&ptr, get_flags(file_access_ring_buffer));                                            \
}

/**
 * Check RESERVE_SUBMIT_FILE_ACCESS_WITH_CACHE for details.
 * In this case we send the event if at least one of the two paths are not in the cache
 */
#define RESERVE_SUBMIT_FILE_ACCESS_DOUBLE_WITH_CACHE(operation, path_src, path_dst, runner_pid, src_path_length, dst_path_length, code)     \
{                                                                                                                                           \
    if (should_send_path(runner_pid, operation, path_src) || should_send_path(runner_pid, operation, path_dst))                             \
    {                                                                                                                                       \
        RESERVE_SUBMIT_FILE_ACCESS_DOUBLE(runner_pid, src_path_length, dst_path_length, code)                                               \
    }                                                                                                                                       \
}

#define RESERVE_SUBMIT_EXEC(runner_pid, exe_path_length, args_length, code)                                     \
{                                                                                                               \
    void *file_access_ring_buffer = bpf_map_lookup_elem(&file_access_per_pip, &runner_pid);                     \
    if (file_access_ring_buffer == NULL) {                                                                      \
        report_file_access_buffer_not_found(runner_pid);                                                        \
        return 0;                                                                                               \
    }                                                                                                           \
                                                                                                                \
    /* The event is a dynamic structure, so we need to reserve space for the metadata and the paths */          \
    /* Perform a dynamic reservation, enough for the metadata and the paths we are about to send */             \
    struct bpf_dynptr ptr;                                                                                      \
    if (bpf_ringbuf_reserve_dynptr(                                                                             \
        file_access_ring_buffer,                                                                                \
        (sizeof(ebpf_event_metadata) + sizeof(int) + exe_path_length + args_length),                            \
        /* flags*/ 0,                                                                                           \
        &ptr)) {                                                                                                \
                                                                                                                \
        report_buffer_reservation_failure(runner_pid, file_access_ring_buffer);                                 \
        bpf_ringbuf_discard_dynptr(&ptr, 0);                                                                    \
        return 0;                                                                                               \
    }                                                                                                           \
                                                                                                                \
    /* Expose a 'metadata' value from the underlying dynamic object to the macro code just for ease of use */   \
    /* (we can do this because it is a fixed size) */                                                           \
    ebpf_event_metadata* metadata = (ebpf_event_metadata*) bpf_dynptr_data(                                     \
        &ptr, /* offset */ 0, sizeof(ebpf_event_metadata));                                                     \
                                                                                                                \
    if (metadata == NULL) {                                                                                     \
        report_ring_buffer_error(runner_pid, "[ERROR] Unable to retrieve metadata from dynptr");                \
        bpf_ringbuf_discard_dynptr(&ptr, 0);                                                                    \
        return 0;                                                                                               \
    }                                                                                                           \
                                                                                                                \
    metadata->event_type = EXEC;                                                                                \
                                                                                                                \
    code                                                                                                        \
                                                                                                                \
    bpf_ringbuf_submit_dynptr(&ptr, get_flags(file_access_ring_buffer));                                        \
}

/**
 * wake_up_new_task() - Hook for clone syscall on wake_up_new_task.
 *
 * We need to make sure we report the clone before the child process starts to avoid some race conditions:
 * - We need to guarantee that we see the process creation arriving as a report line before
 *   any other access report coming from the child (the process creation reported from the parent may non deterministically arrive later than reports from the child).
 *   If reports from the child arrive before the process start report, we won't know which executable to assign those reports to, and for example, allow list entries
 *   that operate on the exec name won't kick in.
 * - We need to make sure process creation is reported before the parent process is terminated. Otherwise, the active process count on managed side reaches 0, and we maybe haven't
 *   seen the child process creation report yet. In this case we'll send an EOM sentinel to the FIFO that we want to arrive *after* the process creation report, so we can actually be
 *   sure whether we can tear down the FIFO (if we reported on the child only, we could detect that the parent process is not alive anymore and send the sentinel only to get the process
 *   start report - reported by the child - after we decided that no more messages should arrive).
 * wake_up_new_task is called when a new task is about to be scheduled for execution. At this point the pid is already known and we know the new process/thread hasn't
 * started yet. At the same time, if the parent process decides to exit right after this, it still hasn't done so.
 */
SEC("fentry/wake_up_new_task")
int BPF_PROG(LOADING_WITNESS, struct task_struct *new_task)
{
    struct task_struct *current_task = (struct task_struct *)bpf_get_current_task();
    pid_t current_tgid = BPF_CORE_READ(current_task, tgid);
    pid_t new_tgid = BPF_CORE_READ(new_task, tgid);

    // We don't care about new threads, just about new processes.
    // Same thread group as the current task means this is just a new thread
    if (current_tgid == new_tgid)
    {
        return 0;
    }

    pid_t current_pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!(is_valid_pid(current_pid, &runner_pid))) {
        return 0;
    }

    pid_t new_pid = BPF_CORE_READ(new_task, pid);

    // If not monitoring child processes, 
    // then skip reporting and skip adding this PID to the pid_map
    if (!monitor_process(new_pid, runner_pid)) {
        // report_ring_buffer_error(runner_pid, "[INFO]: Not monitoring child processes");
        return 0;
    }

    // Add the child that is about to be woken up to the set of processes we care about
    // Observe the pip id is the same as its parent process, since this is running in the context of the same pip
    if (bpf_map_update_elem(&pid_map, &new_pid, &runner_pid, BPF_ANY))
    {
        report_ring_buffer_error(runner_pid, "[ERROR]: Could not update pid_map to add new pid");
        return 0;
    }

    char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!temp_path)
    {
        return 0;
    }

    unsigned int path_length = get_task_exec_path(new_task, temp_path) & (PATH_MAX - 1);
    if (path_length <= 0) {
        return 0;
    }

    // We don't want to cache clones, use unconditional reserve + submit macro
    RESERVE_SUBMIT_FILE_ACCESS(runner_pid, path_length,
        metadata->kernel_function = KERNEL_FUNCTION(wake_up_new_task);
        metadata->operation_type = kClone;
        metadata->pid = current_pid;
        metadata->child_pid = new_pid;
        // At this point task creation always succeed
        metadata->error = 0;
        WRITE_SRC_PATH(temp_path);
    )

    return 0;
}

/**
 * execve_common() - Common code for execve and execveat syscalls.
 */
__attribute__((always_inline)) static inline int execve_common(pid_t pid, enum kernel_function syscall, int fd, const char *filename, char *const *argv, pid_t runner_pid) {

    // Don't monitor child processes
    if (!monitor_process(pid, runner_pid)) {
        // The reason for deleting a pid on exec (rather than on the original fork) is to follow
        // the same convention we used for interpose and on Windows.
        // We want to trace up until the first execve call (not including the initial execve called from the runner process)
        // If we see another execve on the root process after the initial one, we want to stop monitoring it.
        // No exit reports need to be generated by the ebpf programs because the runner process will watch for termination of the root process.
        bpf_map_delete_elem(&pid_map, &pid);
        return 0;
    }

    // Retrieve the exec path
    char* exe_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!exe_path)
    {
        return 0;
    }
    // Since this is the entry point to execve/execveat, the arguments are in user memory
    unsigned int path_length = fd == 0
        // execve
        ? bpf_core_read_user_str(exe_path, PATH_MAX, filename)
        // execveat
        : fd_string_to_string(exe_path, fd, filename, /* user_strings */ true);

    if (path_length <= 0 || path_length >= PATH_MAX || exe_path[0] == '\0') {
        return 0;
    }

    // Retrieve the args
    char* args = bpf_map_lookup_elem(&tmp_paths, &ONE);
    if (!args)
    {
        return 0;
    }
    int args_length = argv_to_string(argv, args);

    if (args_length <= 0 || args_length >= PATH_MAX) {
        return 0;
    }

    // There seems to be a quirk in the verifier, where without this bogus declaration
    // it will complain with a 'Dynptr has to be an uninitialized dynptr' (corresponding to the 
    // dynptr that the reserve macro below will declare). Root cause is unclear. The pocket theory is that without this declaration 
    // on the stack there is some sort of compiler optimization that either manages to remove the dynptr
    // from the stack, or just make the verifier confused. Not any declaration seems to work, and a struct pointing to some
    // of the arguments seems necessary to make it work.
    struct path dummy_path = {.dentry = (struct dentry*)filename};

    // We don't want to cache execs, use unconditional reserve + submit macro
    RESERVE_SUBMIT_EXEC(runner_pid, path_length, args_length,
    {
        metadata->kernel_function = syscall;
        metadata->operation_type = kExec;
        metadata->pid = pid;
        metadata->error = 0;

        // Write the path_length field, which is the immediate next one after the metadata in an exec event
        // Note: this is the only place where we submit an exec event, so it is not worth adding macros for this and
        // just use the raw bpf_dynptr_write call.
        if (bpf_dynptr_write(&ptr, sizeof(ebpf_event_metadata), &path_length, sizeof(int), /* flags*/ 0)) {
            report_ring_buffer_error(runner_pid, "[ERROR] Unable to write exe path length to dynptr");
            bpf_ringbuf_discard_dynptr(&ptr, 0);
            return 0;
        }

        // Write the exe path to the dynamic structure
        if (bpf_dynptr_write(&ptr, sizeof(ebpf_event_metadata) + sizeof(int), exe_path, path_length & (PATH_MAX - 1), 0))
        {
            report_ring_buffer_error(runner_pid, "[ERROR] Unable to write exec path to dynptr");
            bpf_ringbuf_discard_dynptr(&ptr, 0);
            return 0;
        }
        
        // Write the args to the dynamic structure
        if (bpf_dynptr_write(&ptr, sizeof(ebpf_event_metadata) + sizeof(int) + path_length, args, args_length & (PATH_MAX - 1), 0))
        {
            report_ring_buffer_error(runner_pid, "[ERROR] Unable to write args to dynptr");
            bpf_ringbuf_discard_dynptr(&ptr, 0);
            return 0;
        }

        // Populate the exec event with the metadata, which is used to check for breakaway
        exec_event_metadata event_with_metadata = {0};
        event_with_metadata.exe_path_len = path_length;
        // TODO: check whether args are being reported using FAM flag
        event_with_metadata.args_len = args_length;
        event_with_metadata.exe_path = exe_path;
        event_with_metadata.args = args;

        // Validate whether the current pid will need to break away
        if (process_needs_breakaway(&event_with_metadata, runner_pid) != 0)
        {
            report_breakaway_map_not_found(runner_pid);
        }

        if (event_with_metadata.needs_breakaway) {
            // We need to breakaway, so we need to add the pid to the breakaway pids map
            bpf_map_update_elem(&breakaway_pids, &pid, &pid, BPF_ANY);
        }
        else {
            // In case this map was already populated with a stale entry, clean it up here
            bpf_map_delete_elem(&breakaway_pids, &pid);
        }
    })

    return 0;
}

/**
 * execve_ksys_enter() - High level kernel function for execve syscall.
 * 
 * kprobes are used here instead of fentry because reading the filename and argv
 * requires reading user memory which cannot be done in fentry programs
 * because they are not sleepable and the bpf_core_read_user_str helper is sleepable.
 */
SEC("ksyscall/execve")
int BPF_KPROBE_SYSCALL(execve_ksys_enter, const char *filename, char *const *argv, char *const *envp) {
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    return execve_common(pid, KERNEL_FUNCTION(execve), 0, filename, argv, runner_pid);
}

/**
 * execveat_ksys_enter() - High level kernel function for execveat syscall.
 * 
 * See execve_ksys_enter() for details on why we use kprobes instead of fentry.
 */
SEC("ksyscall/execveat")
int BPF_KPROBE_SYSCALL(execveat_ksys_enter, int fd, const char *filename, char *const *argv, char *const *envp, int flags) {
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    // TODO: do we need to do anything with flags passed into this syscall
    return execve_common(pid, KERNEL_FUNCTION(execveat), fd, filename, argv, runner_pid);
}

/**
 * security_bprm_committed_creds_enter() - Security hook for execve syscall on security_bprm_committed_creds.
 *
 * By the time this function is called, the execve syscall is successful.
 * There's no need to observe the exit value because we already know it's going to proceed with the exec.
 */
SEC("fentry/security_bprm_committed_creds")
int BPF_PROG(bprm_execve_enter, struct linux_binprm *bprm) {
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    // This is the point of no return for the execve syscall.
    // If this pid is marked for breakaway, we need to remove it from the breakaway pids map
    // And report it as a breakaway process to the user side.
    // We only need to send a breakaway report instead of an exec report because
    // the execve syscall was already reported by the ksyscall/execve[at] probe.
    int *result = bpf_map_lookup_elem(&breakaway_pids, &pid);

    if (result) {
        // We need to breakaway, so we will remove the pid from the pid map to ignore future file accesses
        bpf_map_delete_elem(&breakaway_pids, &pid);
        bpf_map_delete_elem(&pid_map, &pid);

        char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
        if (!temp_path)
        {
            return 0;
        }
        unsigned int path_length = bpf_core_read_str(temp_path, PATH_MAX, bprm->filename) & (PATH_MAX - 1);

        // We don't want to cache breakaway events, use unconditional reserve + submit macro
        RESERVE_SUBMIT_FILE_ACCESS(runner_pid, path_length, {
            metadata->kernel_function = KERNEL_FUNCTION(security_bprm_committed_creds);
            metadata->operation_type = kBreakAway;
            metadata->pid = pid;
            metadata->error = 0;
            metadata->mode = 0;
            WRITE_SRC_PATH(temp_path);
        })
    }

    return 0;
}

/**
 * taskstats_exit() - This function is called on the task's
 * exit path.
 *
 * Called by both syscall exit() and exit_group(). This call happens before releasing the mm
 * structure on the task, which we still need to inspect in order to get the path of
 * the executing process.
 */
SEC("fentry/taskstats_exit")
int BPF_PROG(taskstats_exit, struct task_struct *tsk, int group_dead)
{
    // We only care about reporting an exit when the thread group is determined to be dead
    if (!group_dead)
    {
        return 0;
    }

    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!temp_path)
    {
        return 0;
    }

    unsigned int path_length = get_task_exec_path(tsk, temp_path) & (PATH_MAX - 1);
    if (path_length <= 0) {
        return 0;
    }

    // We don't want to cache exits, use unconditional reserve + submit macro
    RESERVE_SUBMIT_FILE_ACCESS(runner_pid, path_length,
        metadata->pid = pid;
        metadata->kernel_function = KERNEL_FUNCTION(exit);
        metadata->operation_type = kExit;
        WRITE_SRC_PATH(temp_path);
        bpf_map_delete_elem(&pid_map, &pid);
    )

    return 0;
}

/**
 * security_path_rename() - Security hook for rename syscall.
 *
 * Rename can be a directory or a path that the user side will determine based
 * on the returned mode.
 */
SEC("fentry/security_path_rename")
int BPF_PROG(security_path_rename_enter, const struct path *old_dir, struct dentry *old_dentry,
    const struct path *new_dir, struct dentry *new_dentry, unsigned int flags)
{
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    struct path old_path = {.dentry = old_dentry, .mnt = BPF_CORE_READ(old_dir, mnt)};
    struct path new_path = {.dentry = new_dentry, .mnt = BPF_CORE_READ(new_dir, mnt)};

    char* src_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!src_path)
    {
        return 0;
    }
    unsigned int src_path_length = deref_path_info(src_path, old_dentry, old_dir->mnt) & (PATH_MAX - 1);

    char* dst_path = bpf_map_lookup_elem(&tmp_paths, &ONE);
    if (!dst_path)
    {
        return 0;
    }
    unsigned int dst_path_length = deref_path_info(dst_path, new_dentry, new_dir->mnt) & (PATH_MAX - 1);

    if (src_path_length <= 0 || src_path_length >= PATH_MAX || dst_path_length <= 0 || dst_path_length >= PATH_MAX) {
        return 0;
    }

    // See the comment in execve_common() for why we need this bogus declaration
    struct path dummy_path = {.dentry = old_dir->dentry};

    RESERVE_SUBMIT_FILE_ACCESS_DOUBLE_WITH_CACHE(kRename, &old_path, &new_path, runner_pid, src_path_length, dst_path_length,
        metadata->kernel_function = KERNEL_FUNCTION(security_path_rename);
        metadata->pid = pid;
        metadata->operation_type = kRename;
        // New file/directory doesn't exist yet, so we get the mode from old_dentry
        metadata->mode = get_mode(old_dentry);

        // Write the src path length field, which is the immediate next one after the metadata in a double event
        if (bpf_dynptr_write(&ptr, sizeof(ebpf_event_metadata), &src_path_length, sizeof(int), /* flags*/ 0)) {
            report_ring_buffer_error(runner_pid, "[ERROR] Unable to write src path length to dynptr");
            bpf_ringbuf_discard_dynptr(&ptr, 0);
            return 0;
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
            return 0;
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
            return 0;
        }
    )

	return 0;
}

/**
 * do_mkdirat_exit() - mkdirat syscall.
 */
SEC("fexit/do_mkdirat")
int BPF_PROG(do_mkdirat_exit, int dfd, struct filename *name, umode_t mode, int ret)
{
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    // We only care about the successful case. The unsuccesful one results in a probe,
    // which will be tracked by lookupat
    if (ret != 0)
    {
        return 0;
    }

    char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!temp_path)
    {
        return 0;
    }
    unsigned int path_length = fd_filename_to_string(temp_path, dfd, name) & (PATH_MAX - 1);

    // We don't want to cache mkdir as we need every successful operation on managed side
    RESERVE_SUBMIT_FILE_ACCESS(runner_pid, path_length,
        metadata->kernel_function = KERNEL_FUNCTION(do_mkdirat);
        metadata->operation_type = kCreate;
        metadata->pid = pid;
        metadata->error = 0;
        // The call succeeded, so the path is a directory
        metadata->mode = S_IFDIR;
        WRITE_SRC_PATH(temp_path);
    )

    return 0;
}

/**
 * vfs_rmdir_exit() - rmdir system call hook at the VFS layer.
 *
 * The exit code is used to determine whether the file that was accesssed was a directory or not.
 */
SEC("fexit/do_rmdir")
int BPF_PROG(do_rmdir_exit, int dfd, struct filename *name, int ret)
{
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    // We only care about the successful case. The unsuccesful one results in a probe,
    // which will be tracked by lookupat
    if (ret != 0)
    {
        return 0;
    }

    char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!temp_path)
    {
        return 0;
    }
    unsigned int path_length = fd_filename_to_string(temp_path, dfd, name) & (PATH_MAX - 1);

    // We don't want to cache rmdir as we need every successful operation on managed side
    RESERVE_SUBMIT_FILE_ACCESS(runner_pid, path_length,
        metadata->kernel_function = KERNEL_FUNCTION(do_rmdir);
        metadata->operation_type = kUnlink;
        metadata->pid = pid;
        metadata->error = ret;
        // if the call was successful, the directory is gone, so getting the mode will give us back
        // a 0, and it won't reflect that this pertained to a directory. Hardcode a regular directory
        // value in that case
        metadata->mode = S_IFDIR;
        WRITE_SRC_PATH(temp_path);
    )

    return 0;
}

/**
 * security_path_unlink_entry() - Security Hook for unlink syscall.
 */
SEC("fentry/security_path_unlink")
int BPF_PROG(security_path_unlink_enter, const struct path *dir, struct dentry *dentry)
{
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    // If what's being removed is not a file, we don't need to report anything
    mode_t mode = get_mode(dentry);
    if (is_non_file(mode))
    {
        return 0;
    }

    struct path path = {.dentry = dentry, .mnt = BPF_CORE_READ(dir, mnt)};

    char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!temp_path)
    {
        return 0;
    }
    unsigned int path_length = deref_path_info(temp_path, dentry, dir->mnt) & (PATH_MAX - 1);
    RESERVE_SUBMIT_FILE_ACCESS_WITH_CACHE(kGenericWrite, &path, path_length, runner_pid, 
        metadata->kernel_function = KERNEL_FUNCTION(security_path_unlink);
        metadata->pid = pid;
        metadata->operation_type = kGenericWrite;
        metadata->mode = mode;
        WRITE_SRC_PATH(temp_path);
    )

	return 0;
}

/**
 * security_path_link_entry() - Security hook for creating links.
 */
SEC("fentry/security_path_link")
int BPF_PROG(security_path_link_entry, struct dentry *old_dentry, const struct path *new_dir,
		       struct dentry *new_dentry)
{
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    const unsigned char *new_name = BPF_CORE_READ(new_dentry, d_name.name);
    if (!new_name) {
        return 0;
    }

    struct path new_path = {.dentry = new_dentry, .mnt = BPF_CORE_READ(new_dir, mnt)};

    char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!temp_path)
    {
        return 0;
    }
    path_to_string(temp_path, new_dir);
    unsigned int path_length = combine_paths(temp_path, (const char *)new_name) & (PATH_MAX - 1);

    // The link operation involves a write on the newly created link
    // Observe this operation involves a probe on the source as well (old_dentry). But that
    // access is going to get catch by path_lookupat (and reporting it here is harder because
    // we have the old dentry but not the old mount)
    RESERVE_SUBMIT_FILE_ACCESS_WITH_CACHE(kGenericWrite, &new_path, path_length, runner_pid, 
        metadata->kernel_function = KERNEL_FUNCTION(security_path_link);
        metadata->pid = pid;
        metadata->operation_type = kGenericWrite;
        metadata->mode = get_mode(new_dentry);
        WRITE_SRC_PATH(temp_path);
    )

	return 0;
}

/**
 * path_lookupat_exit() - Handles path resolutions.
 *
 * Used for tracing absent probes when called by system calls like `stat` or `chmod`. Present ones
 * are handled by security_inode_get_attr()
  */
SEC("fexit/path_lookupat")
int BPF_PROG(path_lookupat_exit, struct nameidata *nd, unsigned flags, struct path *path, int ret)
{
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    // We really only care about absent probes, as present ones will be handled by the security layer
    // So if the lookup succeeds (exit code 0) we don't send any event
    if (ret == 0)
    {
        return 0;
    }

    char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!temp_path)
    {
        return 0;
    }
    unsigned int path_length = nameidata_to_string(temp_path, nd) & (PATH_MAX - 1);

    // This operation is hard to check against the cache since for absent probes there is no in-memory structure to
    // represent the path, and using strings is not very performant. For now just keep them out
    // of the cache, we shouldn't get a big number of absent probes on the same path for the same process
    RESERVE_SUBMIT_FILE_ACCESS(runner_pid, path_length,
        metadata->kernel_function = KERNEL_FUNCTION(path_lookupat);
        metadata->pid = pid;
        metadata->operation_type = kGenericProbe;
        metadata->error = ret;
        metadata->mode = ret == 0 ? BPF_CORE_READ(nd, inode, i_mode) : 0;
        WRITE_SRC_PATH(temp_path);
    )

    return 0;
}

/**
 * path_parentat_exit() - Handles path resolutions.
 *
 * Returns the parent directory and final component to the caller.
 * Used for tracing absent probes when called by system calls like rmdir/mkdir
  */
SEC("fexit/path_parentat")
int BPF_PROG(path_parentat, struct nameidata *nd, unsigned flags, struct path *parent, int ret)
{
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    // We really only care about absent probes, as present ones will be handled by the security layer
    // So if the lookup succeeds (exit code 0) we don't send any event
    if (ret == 0)
    {
        return 0;
    }

    char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!temp_path)
    {
        return 0;
    }
    unsigned int path_length = nameidata_to_string(temp_path, nd) & (PATH_MAX - 1);

    // This operation is hard to cache since for absent probes there is no in-memory structure to
    // represent the path, and using strings is not very performant. For now just keep them out
    // of the cache, we shouldn't get a big number of absent probes on the same path for the same process
    RESERVE_SUBMIT_FILE_ACCESS(runner_pid, path_length,
        metadata->kernel_function = KERNEL_FUNCTION(path_parentat);
        metadata->pid = pid;
        metadata->error = ret;
        metadata->operation_type = kGenericProbe;
        metadata->mode = ret == 0 ? BPF_CORE_READ(nd, inode, i_mode) : 0;
        WRITE_SRC_PATH(temp_path);
    )

    return 0;
}

/**
 * path_openat_exit() - Handles path resolutions.
 *
 * Used for `open` system calls essentially on the final component.
 */
SEC("fexit/path_openat")
int BPF_PROG(path_openat_exit, struct nameidata *nd, const struct open_flags *op, unsigned flags, struct file * ret)
{
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    // Unclear if we can trust the mode when the file is not found, so pin the mode to 0 in that case so it represent a non-existent file
    mode_t mode = PTR_ERR(ret) != -ENOENT ? BPF_CORE_READ(nd, inode, i_mode) : 0;

    // Don't bother reporting writes to non-files. 
    if (is_non_file(mode))
    {
        return 0;
    }

    // When openat succeeded, the return value points to the corresponding file structure. Let's check the cache to see whether
    // we have sent this before.
    if (!IS_ERR(ret))
    {
        struct path path = BPF_CORE_READ(ret, f_path);
        if (!should_send_path(runner_pid, kGenericProbe, &path))
        {
            return 0;
        }
    }

    char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!temp_path)
    {
        return 0;
    }
    unsigned int path_length = nameidata_to_string(temp_path, nd) & (PATH_MAX - 1);

    // When this operation fails, it is hard to check the cache since for absent paths there is no in-memory structure to
    // represent them, and using strings is not very performant. For now just keep them out
    // of the cache, we shouldn't get a big number of failed opens on the same path for the same process
    RESERVE_SUBMIT_FILE_ACCESS(runner_pid, path_length,
        metadata->kernel_function = KERNEL_FUNCTION(path_openat);
        metadata->pid = pid;
        metadata->error = PTR_ERR(ret);
        metadata->operation_type = kGenericProbe;
        metadata->mode = mode;
        WRITE_SRC_PATH(temp_path);
    )

    return 0;
}

/**
 * security_file_open_enter() - Security hook for any system calls that may open a file.
 */
SEC("fentry/security_file_open")
int BPF_PROG(security_file_open_enter, struct file *file)
{
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    struct path path = BPF_CORE_READ(file, f_path);

    mode_t mode = get_mode_from_file(file);

    // Don't bother reporting writes to non-files
    if (is_non_file(mode))
    {
        return 0;
    }

    char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!temp_path)
    {
        return 0;
    }
    unsigned int path_length = path_to_string(temp_path, &path) & (PATH_MAX - 1);

    // Always send this as a probe, even if the open call ends up creating the file
    // For the latter, we will catch this in the mknod call.
    RESERVE_SUBMIT_FILE_ACCESS_WITH_CACHE(kGenericProbe, &path, path_length, runner_pid,
        metadata->kernel_function = KERNEL_FUNCTION(security_file_open);
        metadata->pid = pid;
        metadata->operation_type = kGenericProbe;
        metadata->mode = mode;
        WRITE_SRC_PATH(temp_path);
    )

    return 0;
}

/**
 * security_file_permission_enter() - Security hook for any system calls that may access an already open file.
 *
 * Depending on the mask, this can be a read or a write operation.
 */
SEC("fentry/security_file_permission")
int BPF_PROG(security_file_permission_enter, struct file *file, int mask)
{
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    struct path path = BPF_CORE_READ(file, f_path);

    mode_t mode = get_mode_from_file(file);

    // Don't bother reporting writes to non-files
    if (is_non_file(mode))
    {
        return 0;
    }

    // From all the possible values of mask, only MAY_READ and MAY_WRITE seem to be used by the kernel when calling
    // security_file_permission
    operation_type eventType = mask == MAY_READ ? kGenericRead : kGenericWrite;

    char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!temp_path)
    {
        return 0;
    }
    unsigned int path_length = path_to_string(temp_path, &path) & (PATH_MAX - 1);

    RESERVE_SUBMIT_FILE_ACCESS_WITH_CACHE(eventType, &path, path_length, runner_pid,
        metadata->kernel_function = KERNEL_FUNCTION(security_file_permission);
        metadata->pid = pid;
        metadata->operation_type = eventType;
        metadata->mode = mode;
        WRITE_SRC_PATH(temp_path);
    )

    return 0;
}

/**
 * security_path_symlink_enter() - Security hook for creating symlinks.
 */
SEC("fentry/security_path_symlink")
int BPF_PROG(security_path_symlink_enter, const struct path *parent_dir, struct dentry *dentry,
			  const char *old_name)
{
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    // Observe this operation does not imply a read/probe on the target of the symlink (old_name).
    // A traversal to the target is what we should care about
    const char *atom = (const char *)BPF_CORE_READ(dentry, d_name.name);
    if (!atom)
    {
        return 0;
    }

    struct path path = {.dentry = dentry, .mnt = BPF_CORE_READ(parent_dir, mnt)};
    
    char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!temp_path)
    {
        return 0;
    }
    path_to_string(temp_path, parent_dir);
    unsigned int path_length = combine_paths(temp_path, atom) & (PATH_MAX - 1);

    RESERVE_SUBMIT_FILE_ACCESS_WITH_CACHE(kGenericWrite, &path, path_length, runner_pid,
        metadata->kernel_function = KERNEL_FUNCTION(security_path_symlink);
        metadata->pid = pid;
        metadata->operation_type = kGenericWrite;
        metadata->mode = get_mode(dentry);
        WRITE_SRC_PATH(temp_path);
    )

    return 0;
}

/**
 * security_path_mknod_enter() - Checks permission for creating special files.
 *
 * Note that this hook is called even if mknod operation is being done for a regular file.
 */
SEC("fentry/security_path_mknod")
int BPF_PROG(security_path_mknod_enter, const struct path *parent_dir, struct dentry *dentry, umode_t mode, unsigned int dev)
{
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    // Don't bother reporting writes to non-files
    if (is_non_file(mode))
    {
        return 0;
    }

    struct path path = {.dentry = dentry, .mnt = BPF_CORE_READ(parent_dir, mnt)};

    char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!temp_path)
    {
        return 0;
    }
    unsigned int path_length = deref_path_info(temp_path, dentry, BPF_CORE_READ(parent_dir, mnt)) & (PATH_MAX - 1);

    RESERVE_SUBMIT_FILE_ACCESS_WITH_CACHE(kCreate, &path, path_length, runner_pid,
        metadata->kernel_function = KERNEL_FUNCTION(security_path_mknod);
        metadata->pid = pid;
        metadata->operation_type = kCreate;
        metadata->mode = mode;
        WRITE_SRC_PATH(temp_path);
    )

    return 0;
}

/**
 * security_inode_getattr_exit() - Checks permission for retrieving the attributes of an inode
 *
 * This function is traced to identify present probes
 */
SEC("fexit/security_inode_getattr")
int BPF_PROG(security_inode_getattr_exit, const struct path *path, int ret)
{
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    // We only care about reporting the successful cases. Unsuccesful ones are
    // covered by path_lookupat
    if (ret != 0)
    {
        return 0;
    }

    // Don't bother reporting writes to non-files
    mode_t mode = get_mode_from_path(path);
    if (is_non_file(mode))
    {
        return 0;
    }

    char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!temp_path)
    {
        return 0;
    }
    unsigned int path_length = path_to_string(temp_path, path) & (PATH_MAX - 1);

    // See the comment in execve_common() for why we need this bogus declaration
    struct path dummy_path = {.dentry = path->dentry};

    RESERVE_SUBMIT_FILE_ACCESS_WITH_CACHE(kGenericProbe, path, path_length, runner_pid,
        metadata->kernel_function = KERNEL_FUNCTION(security_inode_getattr);
        metadata->pid = pid;
        metadata->error = 0;
        metadata->operation_type = kGenericProbe;
        metadata->mode = mode;
        WRITE_SRC_PATH(temp_path);
    )

    return 0;
}

/**
 * do_readlink_exit() - Call for reading a symlink.
 * Unfortunately we cannot use security_inode_readlink because it only takes a dentry
 * and the mount is missing. Without the mount we cannot successfully resolve a path
 * Observe that both pathname and buf belong to user space in this case (__user on the kernel side)
 */
SEC("fexit/do_readlinkat")
int BPF_PROG(do_readlink_exit, int dfd, const char *pathname, char *buf, int bufsiz, int ret)
{
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    // If readlink failed with a file not found, we don't need to report anything since this access is going to get
    // caught by path_lookupat
    // Observe we are not tracing security_inode_readlink, so if the path exists (but it is not a symlink), we still
    // need to report it here.
    if (ret == -ENOENT)
    {
        return 0;
    }

    // Need to copy the filename from user space
    // retrieve temporary filepath storage
    char* temp_pathname = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!temp_pathname)
    {
        return 0;
    }
    int length = bpf_core_read_user_str(temp_pathname, PATH_MAX, pathname);
    if (length <= 0)
    {
        return 0;
    }

    char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ONE);
    if (!temp_path)
    {
        return 0;
    }
    unsigned int path_length = fd_string_to_string(temp_path, dfd, temp_pathname, /* user_strings */ false) & (PATH_MAX - 1);

    // This operation is hard to check against the cache since its arguments don't give us any in-memory structure to
    // represent the path, and using strings is not very performant. For now just keep them out
    // of the cache
    RESERVE_SUBMIT_FILE_ACCESS(runner_pid, path_length,
        metadata->kernel_function = KERNEL_FUNCTION(do_readlinkat);
        metadata->pid = pid;
        // When successful, the function returns the number of bytes copied, and negative on error.
        // On error, we report a probe, since the path was not actually read.
        metadata->operation_type = ret < 0 ? kGenericProbe : kGenericRead;
        // If the call was successful, it means the symlink is legit (and therefore a regular file)
        // If the call failed, we set the mode to 0 since we don't really know what it is. The mode
        // will be retrieved and set on user side, which is not ideal (since there is the small chance of a race, 
        // where the status of the path changed), but this is probably good enough.
        metadata->mode = ret < 0 ? 0 : S_IFREG;
        // If the call failed, we set the error code to the return value, otherwise we set it to 0 since in that
        // case ret just represents the number of bytes copied
        metadata->error = ret < 0 ? ret : 0;
        WRITE_SRC_PATH(temp_path);
    )

    return 0;
}

/**
 * step_into_exit() - symlink traversal
 * we cannot use security_inode_follow_link because it only takes a dentry and we are missing the mount
 * To traverse the symlink, step_into() will call pick_link().
 * This tracepoint is triggered potentially after pick_link() has already been called.
 * In some cases pick_link() is skipped if the dentry is not a symlink.
 * 
 * NOTE: Used for kernels older than 6.8, where pick_link() does not have BTF type information.
 *  Autoattach is disabled, user side must attach it manually.
 */
SEC("?fexit/step_into")
int BPF_PROG(step_into_exit, struct nameidata *nd, int flags,
		     struct dentry *dentry, char *ret)
{
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    // We don't care about tracing this if it fails. Probes should be caught by lookupat
    // CODESYNC: https://github.com/torvalds/linux/blob/02adc1490e6d8681cc81057ed86d123d0240909b/fs/namei.c#L1971
    // The exit conditions here are based on whether step_into() will call into pick_link() or not.
    // We want to ignore any branches that don't call into pick_link() because they represent intermediate
    // calls that are not symlinks.
    // This is done because kernels older than 6.8 do not provide BTF type information for pick_link(),
    // so we can't hook into it directly.
    // TODO: this function releases the dentry at the end (?), not sure if it's possible that it's invalid here
    bool skip_step_into_not_symlink = 
        !d_is_symlink(dentry)
        || ((flags & WALK_TRAILING) && !(BPF_CORE_READ(nd, flags) & LOOKUP_FOLLOW))
        || (flags & WALK_NOFOLLOW)
        // Paths with no final component return NULL when pick_link() is called
        // https://github.com/torvalds/linux/blob/02adc1490e6d8681cc81057ed86d123d0240909b/Documentation/filesystems/path-lookup.rst#symlinks-with-no-final-component
        || ret == NULL;

    if (skip_step_into_not_symlink || IS_ERR(ret))
    {
        return 0;
    }

    char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!temp_path)
    {
        return 0;
    }

    unsigned int path_length = nameidata_to_string(temp_path, nd) & (PATH_MAX - 1);
    struct path link = BPF_CORE_READ(nd, path);

    RESERVE_SUBMIT_FILE_ACCESS_WITH_CACHE(kGenericRead, &link, path_length, runner_pid,
        metadata->kernel_function = KERNEL_FUNCTION(pick_link_enter);
        metadata->pid = pid;
        metadata->operation_type = kGenericRead;
        metadata->mode = get_mode_from_path(&link);
        WRITE_SRC_PATH(temp_path);
    )

    return 0;
}

/**
 * pick_link_exit() - symlink traversal
 * we cannot use security_inode_follow_link because it only takes a dentry and we are missing the mount.
 * 
 * NOTE: Used for kernels 6.8 and newer, where pick_link() has BTF type information.
 *  Autoattach is disabled, user side must attach it manually.
 */
SEC("?fexit/pick_link")
int BPF_PROG(pick_link_exit, struct nameidata *nd, struct path *link,
    struct inode *inode, int flags, char * ret)
{
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    // We don't care about tracing this if it fails. Probes should be caught by lookupat
    if (IS_ERR(ret))
    {
        return 0;
    }

    char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!temp_path)
    {
        return 0;
    }
    unsigned int path_length = path_to_string(temp_path, link) & (PATH_MAX - 1);

    // See the comment in execve_common() for why we need this bogus declaration
    struct path dummy_path = {.dentry = link->dentry};

    RESERVE_SUBMIT_FILE_ACCESS_WITH_CACHE(kGenericRead, link, path_length, runner_pid,
        metadata->kernel_function = KERNEL_FUNCTION(pick_link_enter);
        metadata->pid = pid;
        metadata->operation_type = kGenericRead;
        metadata->mode = get_mode_from_path(link);
        WRITE_SRC_PATH(temp_path);
    )

    return 0;
}

/**
 * security_path_chown() - Security hook for chown.
 */
SEC("fentry/security_path_chown")
int BPF_PROG(security_path_chown, const struct path *path)
{
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    // Don't bother reporting writes to non-files
    mode_t mode = get_mode_from_path(path);
    if (is_non_file(mode))
    {
        return 0;
    }

    char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!temp_path)
    {
        return 0;
    }
    unsigned int path_length = path_to_string(temp_path, path) & (PATH_MAX - 1);

    // See the comment in execve_common() for why we need this bogus declaration
    struct path dummy_path = {.dentry = path->dentry};

    RESERVE_SUBMIT_FILE_ACCESS_WITH_CACHE(kGenericWrite, path, path_length, runner_pid,
        metadata->kernel_function = KERNEL_FUNCTION(security_path_chown);
        metadata->pid = pid;
        metadata->operation_type = kGenericWrite;
        metadata->mode = mode;
        WRITE_SRC_PATH(temp_path);
    )

	return 0;
}

/**
 * security_path_chmod() - Security hook for chmod.
 */
SEC("fentry/security_path_chmod")
int BPF_PROG(security_path_chmod, const struct path *path)
{
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    // Don't bother reporting writes to non-files
    mode_t mode = get_mode_from_path(path);
    if (is_non_file(mode))
    {
        return 0;
    }
    
    char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!temp_path)
    {
        return 0;
    }
    unsigned int path_length = path_to_string(temp_path, path) & (PATH_MAX - 1);

    // See the comment in execve_common() for why we need this bogus declaration
    struct path dummy_path = {.dentry = path->dentry};

    RESERVE_SUBMIT_FILE_ACCESS_WITH_CACHE(kGenericWrite, path, path_length, runner_pid,
        metadata->kernel_function = KERNEL_FUNCTION(security_path_chmod);
        metadata->pid = pid;
        metadata->operation_type = kGenericWrite;
        metadata->mode = mode;
        WRITE_SRC_PATH(temp_path);
    )

	return 0;
}

/**
 * security_file_truncate() - Security hook for truncate.
 */
SEC("fentry/security_file_truncate")
int BPF_PROG(security_file_truncate, struct file *file)
{
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    struct path path = BPF_CORE_READ(file, f_path);

    // Don't bother reporting writes to non-files
    mode_t mode = get_mode_from_path(&path);
    if (is_non_file(mode))
    {
        return 0;
    }

    char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!temp_path)
    {
        return 0;
    }
    unsigned int path_length = path_to_string(temp_path, &path) & (PATH_MAX - 1);

    RESERVE_SUBMIT_FILE_ACCESS_WITH_CACHE(kGenericWrite, &path, path_length, runner_pid,
        metadata->kernel_function = KERNEL_FUNCTION(security_file_truncate);
        metadata->pid = pid;
        WRITE_SRC_PATH(temp_path);
        // A truncate always involves a write operation
        metadata->operation_type = kGenericWrite;
        metadata->mode = mode;
    )

    return 0;
}

/**
 * vfs_utimes() - Security hook for utimes family.
 */
SEC("fentry/vfs_utimes")
int BPF_PROG(vfs_utimes, const struct path *path)
{
    pid_t pid = bpf_get_current_pid_tgid() >> 32;
    pid_t runner_pid;
    if (!is_valid_pid(pid, &runner_pid)) {
        return 0;
    }

    // Don't bother reporting writes to non-files
    mode_t mode = get_mode_from_path(path);
    if (is_non_file(mode))
    {
        return 0;
    }

    char* temp_path = bpf_map_lookup_elem(&tmp_paths, &ZERO);
    if (!temp_path)
    {
        return 0;
    }
    unsigned int path_length = path_to_string(temp_path, path) & (PATH_MAX - 1);

    // See the comment in execve_common() for why we need this bogus declaration
    struct path dummy_path = {.dentry = path->dentry};

    RESERVE_SUBMIT_FILE_ACCESS_WITH_CACHE(kGenericWrite, path, path_length, runner_pid,
        metadata->kernel_function = KERNEL_FUNCTION(vfs_utimes);
        metadata->pid = pid;
        WRITE_SRC_PATH(temp_path);
        metadata->operation_type = kGenericWrite;
        metadata->mode = mode;
    )

    return 0;
}