// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

/**
 * System includes
 */
#include <chrono>
#include <errno.h>
#include <linux/version.h>
#include <signal.h>
#include <stdio.h>
#include <string.h>
#include <string>
#include <sys/mman.h>
#include <sys/resource.h>
#include <sys/utsname.h>
#include <sys/wait.h>
#include <unistd.h>
#include <thread>
#include <atomic>

/**
 * Libbpf, bpf skeleton, and shared bpf includes
 */
#include "bpf/bpf.h"
#include "bpf/libbpf.h"
#include "sandbox.skel.h"
#include "bpf/btf.h"

/**
 * BuildXL includes
 */
#include "EventRingBuffer.hpp"
#include "bxl_observer.hpp"
#include "ConcurrentQueue.h"
#include "SyscallHandler.h"
#include "ebpfcommon.h"

// Max size for the name of a bpf program
#define MAX_PROG_FULL_NAME 128

// The poison pill is used to signal the event queue thread to stop
#define POISON_PILL (ebpf_event*) -1


/** Constants */
const int PINNED_MAPS_SIZE = 9;

/** Globals */
BxlObserver *g_bxl;
buildxl::linux::ebpf::SyscallHandler *g_syscallHandler;
static volatile sig_atomic_t g_stop;
static volatile int g_exit_code;
static volatile sig_atomic_t g_exit_signal_received = 0;
static volatile sig_atomic_t g_root_process_exited = 0;
int g_root_pid = 0, g_runner_pid = 0;
struct ring_buffer *g_debug_ring_buffer = nullptr;
int g_pid_map_fd, g_sandbox_options_per_pip_map_fd, g_stats_per_pip_map_fd, g_file_access_per_pip_fd, g_last_path_per_pip_fd = -1;
int g_debug_buffer_per_pip_fd, g_breakaway_processes_map_fd, g_event_cache_per_pip_fd, g_string_cache_per_pip_fd, g_breakaway_processes_per_pip_fd = -1;
sem_t g_root_process_populated_semaphore;
bool g_ebpf_already_loaded, g_ebpf_should_force_ebpf_loading = false;
buildxl::common::ConcurrentQueue<ebpf_event *> g_event_queue;
pthread_t g_event_queue_thread;
void RingBufferOutOfSpaceCallback(buildxl::linux::ebpf::EventRingBuffer *eventRingBuffer);

// The active ring buffer is used to store the current ring buffer that is being polled.
std::atomic<buildxl::linux::ebpf::EventRingBuffer *> g_active_ring_buffer;

/**
 * Error logger for this program.
 */
static int LogError(const char *fmt, ...) {
    va_list args;
    va_start(args, fmt);
    g_bxl->LogErrorArgList(getpid(), fmt, args);
    va_end(args);

    return 1;
}

/**
 * Callback function that is called when the ring buffer capacity is exceeded.
 * It creates a new overflow event buffer and replaces the current ring buffer in the outer map
 * with the new overflow buffer.
 */
void RingBufferOutOfSpaceCallback(buildxl::linux::ebpf::EventRingBuffer *eventRingBuffer) {

    // Create a new overflow buffer to handle the overflow of the current ring buffer.
    buildxl::linux::ebpf::OverflowEventRingBuffer *overflow_buffer = new buildxl::linux::ebpf::OverflowEventRingBuffer(
        g_bxl, 
        &g_root_process_exited, 
        g_event_queue, 
        RingBufferOutOfSpaceCallback,
        eventRingBuffer);

    // If the overflow failed to initialize, we just return without doing the swapping. The build might still succeed, since the ringbuffer has not overflowed yet.
    // This is a very unlikely scenario though, an initialization failure is usually caused by not being able to create a new ring buffer due to out of memory or similar issues.
    if (overflow_buffer->Initialize())
    {
        delete overflow_buffer;
        return;
    }

    int ring_buffer_fd = overflow_buffer->GetRingBufferFd();

    // Replace the file access ring buffer. This action immediately alleviates the pressure on the current ring buffer.
    int key = g_runner_pid;
    if (bpf_map_update_elem(g_file_access_per_pip_fd, &key, &ring_buffer_fd, BPF_ANY))
    {
        LogError("Failed to replace file access ring buffer to outer map for runner PID %d: %s\n", key, strerror(errno));
        overflow_buffer->NotifyDeactivated();
        delete overflow_buffer;
        return;
    }
    else
    {
        g_bxl->LogDebug(getpid(), "Swapped file access ring buffer for runner PID %d from %d to %d", key, eventRingBuffer->GetId(), overflow_buffer->GetId());
    }

    // Start the overflow buffer polling thread to start emptying the new ring buffer.
    overflow_buffer->NotifyActivated();

    // Swap the active ring buffer to the new overflow buffer. We keep the active ring buffer on this global variable so we can finally
    // wait for it to be done when the runner is about to exit.
    g_active_ring_buffer.store(overflow_buffer);

    // Notify the previous buffer that it has been deactivated.
    // This will cause it to wait for the grace period to be over and then move the events from the overflow queue to the main event queue.
    // After the grace period is over, the overflow buffer will automatically release the associated ring buffer.
    eventRingBuffer->NotifyDeactivated();
}

/**
 * Forward messages emitted by libbpf to BuildXL.
 *
 * Fallback to stderr in case BxlObserver is not initialized.
 */
static int LibBpfPrintFn(enum libbpf_print_level level, const char *format, va_list args) {
    // We only care about warnings and errors
    if (level <= LIBBPF_WARN) {
        if (g_bxl == nullptr) {
            // fallback to stderr if bxl is not initialized
            return vfprintf(stderr, format, args);
        }

        // libbpf log messages occasionally contain new line characters.
        // BuildXL does not send these, but it's necessary to render these
        // so that the log message is readable.
        // Render the formatted message, split it and send it line by line.
        va_list args_copy;
        va_copy(args_copy, args); // calling vsprintf will consume the va_list, copy it to use again.
        int buffer_size = vsnprintf(nullptr, 0, format, args);

        char *buffer = new char[buffer_size + 1];
        vsnprintf(buffer, buffer_size, format, args_copy);
        va_end(args_copy);

        // Split the buffer by new lines and send each line separately
        std::stringstream stream(buffer);
        std::string line;
        while (std::getline(stream, line, '\n')) {
            // We log everything from warning and above as an error for now
            g_bxl->LogError(getpid(), "%s", line.c_str());
        }

        delete[] buffer;

        return buffer_size;
    }

    return 0;
}

/**
 * Perform libbpf related cleanup.
 */
void Cleanup(struct sandbox_bpf *skel) {
    // Unload EBPF programs if this runner was the one loading them to begin with
    if (g_ebpf_should_force_ebpf_loading || !g_ebpf_already_loaded) {
        sandbox_bpf::destroy(skel);
    }
}

void LogDebugEvent(ebpf_event *event)
{
    switch (event->metadata.event_type)
    {
        case EXEC: 
        {
            const ebpf_event_exec * exec_event = (const ebpf_event_exec *)event;
            g_bxl->LogDebug(
                exec_event->metadata.pid, 
                "[%d] kernel function: %s, operation: %s, exe path: '%s', args: '%s'",
                exec_event->metadata.pid,
                kernel_function_to_string(exec_event->metadata.kernel_function), 
                operation_type_to_string(exec_event->metadata.operation_type),
                get_exe_path(exec_event),
                get_args(exec_event));
            break;
        }
        case SINGLE_PATH:
        {
            g_bxl->LogDebug(
                event->metadata.pid, 
                "[%d] kernel function: %s, operation: %s, S_ISREG: %d, S_ISDIR: %d, errno: %d %s, path: '%s'",
                event->metadata.pid, 
                kernel_function_to_string(event->metadata.kernel_function),
                operation_type_to_string(event->metadata.operation_type),
                S_ISREG(event->metadata.mode), 
                S_ISDIR(event->metadata.mode),
                event->metadata.error,
                // Internal functions return errno as a negative number
                strerror(abs(event->metadata.error)),
                event->src_path);
            break;
        }
        case DOUBLE_PATH:
        {
            const ebpf_event_double * double_event = (const ebpf_event_double *)event;
            g_bxl->LogDebug(
                double_event->metadata.pid, 
                "[%d] kernel function: %s, operation: %s, S_ISREG: %d, S_ISDIR: %d, errno: %d %s, source path: '%s', dest path '%s'",
                event->metadata.pid, 
                kernel_function_to_string(double_event->metadata.kernel_function),
                operation_type_to_string(double_event->metadata.operation_type),
                S_ISREG(event->metadata.mode), 
                S_ISDIR(event->metadata.mode),
                double_event->metadata.error,
                strerror(double_event->metadata.error * -1),
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

static inline __u64 ptr_to_u64(const void *ptr)
{
	return (__u64)(unsigned long)ptr;
}

/** Retrieves the program full name of a given bpf_prog_info */
void GetProgramFullName(const struct bpf_prog_info *prog_info, int prog_fd, char *name_buff, size_t buff_len)
{
    const char *prog_name = prog_info->name;
    const struct btf_type *func_type;
    struct bpf_func_info finfo = {};
    struct bpf_prog_info info = {};
    __u32 info_len = sizeof(info);
    struct btf *prog_btf = NULL;

    // If the name is 16 chars or left, it is already contained in the info object
    if (buff_len <= BPF_OBJ_NAME_LEN || strlen(prog_info->name) < BPF_OBJ_NAME_LEN - 1) {
        goto copy_name;
    }

    if (!prog_info->btf_id || prog_info->nr_func_info == 0) {
        goto copy_name;
    }

    info.nr_func_info = 1;
    info.func_info_rec_size = prog_info->func_info_rec_size;
    if (info.func_info_rec_size > sizeof(finfo)) {
        info.func_info_rec_size = sizeof(finfo);
    }
    info.func_info = ptr_to_u64(&finfo);

    // Retrieve full info of the program
    if (bpf_prog_get_info_by_fd(prog_fd, &info, &info_len)) {
        goto copy_name;
    }

    // Load corresponding BTF object
    prog_btf = btf__load_from_kernel_by_id(info.btf_id);
    if (!prog_btf) {
        goto copy_name;
    }

    // Retrieve the function associated to the program and get the name
    func_type = btf__type_by_id(prog_btf, finfo.type_id);
    if (!func_type || !btf_is_func(func_type)) {
        goto copy_name;
    }

    prog_name = btf__name_by_offset(prog_btf, func_type->name_off);

    copy_name:
    snprintf(name_buff, buff_len, "%s", prog_name);

    if (prog_btf) {
        btf__free(prog_btf);
    }
}

bool ShouldForceEBPFLoading()
{
    // If the environment variable is set, we always load EBPF. Mostly for testing purposes.
    if (getenv(BxlUnconditionallyLoadEBPF) != nullptr) {
        g_bxl->LogDebug(getpid(), "Unconditionally loading EBPF programs because environment variable %s is set", BxlUnconditionallyLoadEBPF);
        return true;
    }

    return false;
}

/**
 * Whether EBPF loading already happened (by running an instance of this runner)
 */
bool IsEbpfAlreadyLoaded()
{
    __u32 id = 0;
    int err, fd = 0;
    char prog_name[MAX_PROG_FULL_NAME];

    // Iterate over all bpf programs
    while (true) {
        err = bpf_prog_get_next_id(id, &id);
        if (err) {
            break;
        }

        fd = bpf_prog_get_fd_by_id(id);
        if (fd < 0) {
            continue;
        }

        // We got a program with a valid file descriptor, retrieve its info
        struct bpf_prog_info info = {};
        __u32 len = sizeof(info);

        err = bpf_obj_get_info_by_fd(fd, &info, &len);
        if (err || !info.name)
        {
            continue;
        }
        // Check whether we find a program that is our loading witness
        // (this is just an arbitrarily picked program among all the ones we load)
        GetProgramFullName(&info, fd, prog_name, sizeof(prog_name));

        if (strcmp(prog_name, EXPAND_AND_STRINGIFY(LOADING_WITNESS)) == 0) {
            close(fd);
            return true;
        }

        close(fd);
	}

    return false;
}

/**
 * Handles SigIntHandler signal.
 */
void SigIntHandler(int signo) {
    g_exit_signal_received = 1;
    // If the root process exited, we might be waiting for orphaned children to exit.
    // In that case, we don't want to forward the signal to the root process, since by
    // setting g_exit_signal_received above, the runner will shortly end naturally.
    if (g_root_process_exited) {
        return;
    }

    // Otherwise, if the root process hasn't exited, forward the signal to it
    if (g_root_pid != 0) {
        kill(g_root_pid, signo);
    }
}

/**
 * Handles a provided ebpf event.
 */
void HandleEvent(ebpf_event *event) {
    LogDebugEvent(event);

    switch (event->metadata.event_type) {
        case EXEC: {
            g_syscallHandler->HandleExecEvent((const ebpf_event_exec *)event);
            break;
        }
        case SINGLE_PATH:
            g_syscallHandler->HandleSingleEvent(event);                
            break;
        case DOUBLE_PATH:
        {
            const ebpf_event_double* double_event = (const ebpf_event_double *)event;
            g_syscallHandler->HandleDoubleEvent(double_event);
            break;
        }
        case DEBUG:
            g_syscallHandler->HandleDebugEvent((const ebpf_event_debug *)event);
            break;
        default:
            LogError("Unhandled event type %d", event->metadata.event_type);
            break;
    }
}

/**
 * Callback function for event handling thread
 */
void *HandleEventQueue(void *arg) {
    while (true) {
        ebpf_event* event = nullptr;
        while (!g_event_queue.Dequeue(event)) {
        }

        if (event == POISON_PILL) {
            // Received poison pill: exit the thread.
            break;
        }

        if (event) {
            HandleEvent(event);
            free(event);
        }
    }

    pthread_exit(NULL);
}

/* Consumes any remaining items in the debug ring buffer */
void FlushDebugRingBufferEvents()
{
    int res;
    // Let's account for interrupted system calls
    // and retry until we consume everything
    do {
        res = ring_buffer__consume(g_debug_ring_buffer);
    } while (res == -EINTR);
}

int PopulateOptionsMapFromFam()
{
    // TODO: assert that pid fd is valid
    // TODO: assert root_pid is valid
    int key = g_runner_pid;
    sandbox_options options = {
        .root_pid = g_root_pid,
        .is_monitoring_child_processes = g_bxl->IsMonitoringChildProcesses()
    };

    if (bpf_map_update_elem(g_sandbox_options_per_pip_map_fd, &key, &options, BPF_ANY))
    {
        g_bxl->LogDebug(getpid(), "Can't add options to map: %s", strerror(errno));
        return 1;
    }

    return 0;
}

/**
 * Start process and update PID table with root process PID.
 */
int RunRootProcess(const char *file, char *const argv[], char *const envp[]) {
    // Place a semaphore in shared memory, so both parent and child can see it
    // This semaphore is used so the pid representing the root of the pip can be retrieved
    // and added into the bpf pid map before the pip root actually starts running
    sem_t* pid_added_semaphore = (sem_t*) mmap(NULL, sizeof(*pid_added_semaphore), PROT_READ | PROT_WRITE, MAP_SHARED | MAP_ANONYMOUS, -1, 0);
    if (pid_added_semaphore == MAP_FAILED)
    {
        return -1;
    }

    // The semaphore is initialized locked
    if (sem_init(pid_added_semaphore, /* shared */ 1, /* initial value */ 0) == -1)
    {
        g_bxl->LogDebug(getpid(), "Can't init semaphore");
        return -1;
    }

    pid_t pid = fork();
    if (pid == 0) {
        // Child
        // Wait on the semaphore to make sure the parent process has already populated the map
        sem_wait(pid_added_semaphore);
        // Now the child's pid is populated and we can proceed
        sem_post(pid_added_semaphore);
        sem_destroy(pid_added_semaphore);

        execve(file, argv, envp);
        return -1;
    }
    else  {
        g_root_pid = pid;
        g_syscallHandler = new buildxl::linux::ebpf::SyscallHandler(g_bxl, g_root_pid, g_runner_pid, file, &g_active_ring_buffer, g_stats_per_pip_map_fd);
        // Signal that the root pid is already populated
        sem_post(&g_root_process_populated_semaphore);

        // Add child pid to the pid map, associating it with pid of the runner for this pip.
        int key = g_runner_pid;
        if (bpf_map_update_elem(g_pid_map_fd, &g_root_pid, &key, BPF_ANY))
        {
            g_bxl->LogDebug(getpid(), "Can't add new pip id to map: %s", strerror(errno));
            return 1;
        }
        
        // Root pid must be set before calling this function
        if (PopulateOptionsMapFromFam())
        {
            // Error has been logged already
            return 1;
        }
        
        // Unlock the semaphore so the child process can proceed
        sem_post(pid_added_semaphore);
    }

    return 0;
}

/**
 * Deletes a per-pip map for the provided key.
 * This is used to remove the reference to the pip from the outer per-pip maps.
 * If the map is not found, we log an error only if emitErrors is true.
 */
int DeletePerPipMap(int map_per_pip_fd, int key, const char *description)
{
    if (bpf_map_delete_elem(map_per_pip_fd, &key))
    {
            LogError("Error deleting map '%s' for runner PID %d:%s\n", description, key, strerror(errno));
            return 1;
    }

    return 0;
}

/**
 * Wait for the process tree to exit and perform clean up operations.
 * Observe that process tree is defined as the root process and all its children that were ever spawned under that tree.
 * This means we will also wait for any orphaned children that is not technically part of the OS process tree.
 */
void *WaitForProcessTreeToExit(void *argv) {
    int status = 0;

    // Wait until the root pid has been populated
    sem_wait(&g_root_process_populated_semaphore);
    assert(g_root_pid != 0);

    while (true) {
        int ret = waitpid(g_root_pid, &status, 0);

        if (ret == -1) {
            // this case shouldn't happen, but it usually means the child process
            // has already exit without us knowing about it
            g_exit_code = 0;
            break;
        }

        // Handle normal termination and termination by signal
        // WIFEXITED indicates a normal exit
        // WIFSIGNALED indicates an abnormal exit by signal
        // Other signals such as SIGSTOP and SIGCONT are ignored here because
        // they do not result in program termination.
        if (WIFEXITED(status)) {
            // Root process had a normal exit
            g_exit_code = WEXITSTATUS(status);
            break;
        }
        else if (WIFSIGNALED(status)) {
            // Root process was terminated by a signal
            // The exit code is set to that signal
            g_exit_code = WTERMSIG(status);
            break;
        }
    }

    g_root_process_exited = 1;

    // Now wait for all the children to exit. This include orphaned children. But it does not include breakaway processes
    // (the syscall handler takes care of tracking all this)
    // If an exit signal is received, let the loop exit so the runner can exit gracefully, preserving the original exit code 
    // (since we don't want the runner to be killed because of orphaned children, which, as seen from the outside, are not part of the 
    // process tree anymore)
    while (!g_exit_signal_received) {
        // Since we control the ring buffer wake up frequency from the kernel side, there is always the chance
        // of a tail of events that are waiting to be flushed.
        if (g_syscallHandler->WaitForNoActiveProcesses(100) == 0) {
            // No more children to wait for
            break;
        };
    }
    
    g_stop = 1;

    return NULL;
}

/**
 * Populates a set of breakaway processes from the file access manifest to be used by the kernel side.
 */
int PopulateBreakawayProcessesMap() {
    auto breakaway_processes = g_bxl->GetBreakawayChildProcesses();
    auto breakaway_processes_count = std::min(breakaway_processes.size(), static_cast<size_t>(MAX_BREAKAWAY_PROCESSES));;

    for (int i = 0; i < breakaway_processes_count; i++) {
        breakaway_process process = {0};
        strncpy(process.tool, breakaway_processes[i].GetExecutable().c_str(), sizeof(process.tool));
        process.tool_len = breakaway_processes[i].GetExecutable().length();
        strncpy(process.arguments, breakaway_processes[i].GetRequiredArgs().c_str(), sizeof(process.arguments));
        process.arguments_len = breakaway_processes[i].GetRequiredArgs().length();
        process.args_ignore_case = breakaway_processes[i].GetRequiredArgsIgnoreCase();

        if (bpf_map_update_elem(g_breakaway_processes_map_fd, &i, &process, BPF_ANY))
        {
            LogError("Could not add breakaway process");
            return -1;            
        }
    }

    return 0;
}

/**
 * Block on the debug event ring buffer and handle debug events.
 */
void *PollDebugBuffer(void *arg) {
    while (!g_stop) {
        int err = ring_buffer__poll(g_debug_ring_buffer, /* timeout_ms */ 1000);
        if (err == -EINTR) {
            err = 0;
            break;
        }
        if (err < 0) {
            LogError("Error polling debug ring buffer %d\n", err);
            break;
        }
    }

    return NULL;
}

// Reuses the pinned maps, assuming bpf is already loaded.
int ReuseMaps(struct sandbox_bpf *skel) {
    // These are the `PINNED_MAPS_SIZE` pinned maps we have. Retrieve their pin paths and reuse them
    bpf_map* pinned_maps[PINNED_MAPS_SIZE] = {
        skel->maps.pid_map, 
        skel->maps.file_access_per_pip, 
        skel->maps.debug_buffer_per_pip,
        skel->maps.breakaway_processes_per_pip,
        skel->maps.sandbox_options_per_pip,
        skel->maps.event_cache_per_pip,
        skel->maps.string_cache_per_pip,
        skel->maps.stats_per_pip,
        skel->maps.last_path_per_pip
    };

    for (int i = 0 ; i < PINNED_MAPS_SIZE; i++)
    {
        bpf_map* pinned_map = pinned_maps[i];
        int pin_fd = bpf_obj_get(bpf_map__get_pin_path(pinned_map));
        if (pin_fd < 0)
        {
            LogError("Error getting pin path: %s\n", strerror(errno));
            return -1;    
        }
        int err = bpf_map__reuse_fd(pinned_map, pin_fd);
        close(pin_fd);
        if (err)
        {
            LogError("Cannot reuse pinned map\n");
            return -1;    
        }
    }

    return 0;
}

// Sets up the global file descriptors for the per-pip maps.
int BindPerPipMaps(struct sandbox_bpf *skel) {
    // Retrieve sandbox options map
    g_sandbox_options_per_pip_map_fd = bpf_object__find_map_fd_by_name(skel->obj, "sandbox_options_per_pip");
    if (g_sandbox_options_per_pip_map_fd < 0) {
        LogError("finding sandbox_options_per_pip in obj file failed\n");
        Cleanup(skel);
        return -1;
    }

    // Retrieve the per-pip file access outer map and create the file access ring buffer
    g_file_access_per_pip_fd = bpf_object__find_map_fd_by_name(skel->obj, "file_access_per_pip");
    if (g_file_access_per_pip_fd < 0) {
        LogError("finding file_access_per_pip in obj file failed\n");
        Cleanup(skel);
        return -1;
    }

    // Retrieve the per-pip debug ring buffer and create a debug ring buffer for the current pip
    g_debug_buffer_per_pip_fd =  bpf_object__find_map_fd_by_name(skel->obj, "debug_buffer_per_pip");
    if (g_debug_buffer_per_pip_fd < 0)
    {
        LogError("Failed to retrieve debug ring buffer per pip\n");
        Cleanup(skel);
        return -1;
    }

    // Retrieve the per-pip event cache and create an event cache for the current pip
    g_event_cache_per_pip_fd =  bpf_object__find_map_fd_by_name(skel->obj, "event_cache_per_pip");
    if (g_event_cache_per_pip_fd < 0)
    {
        LogError("Failed to retrieve event cache per pip\n");
        Cleanup(skel);
        return -1;
    }

    // Retrieve the per-pip string cache and create a string cache for the current pip
    g_string_cache_per_pip_fd = bpf_object__find_map_fd_by_name(skel->obj, "string_cache_per_pip");
    if (g_string_cache_per_pip_fd < 0)
    {
        LogError("Failed to retrieve string cache per pip\n");
        Cleanup(skel);
        return -1;
    }

     // Retrieve the per-pip string cache and create a string cache for the current pip
    g_stats_per_pip_map_fd = bpf_object__find_map_fd_by_name(skel->obj, "stats_per_pip");
    if (g_stats_per_pip_map_fd < 0)
    {
        LogError("Failed to retrieve stats per pip\n");
        Cleanup(skel);
        return -1;
    }

    // Retrieve the per-pip breakaway process map
    g_breakaway_processes_per_pip_fd = bpf_object__find_map_fd_by_name(skel->obj, "breakaway_processes_per_pip");
    if (g_breakaway_processes_per_pip_fd < 0) {
        LogError("Finding breakaway_processes per pip in bpf object failed.\n");
        Cleanup(skel);
        return -1;
    }

    g_last_path_per_pip_fd = bpf_object__find_map_fd_by_name(skel->obj, "last_path_per_pip");
    if (g_last_path_per_pip_fd < 0) {
        LogError("Finding last_path_per_pip in bpf object failed.\n");
        Cleanup(skel);
        return -1;
    }

    return 0;
}

/**
 * Callback for debug event ring buffer.
 */
int HandleDebugEvents(void *ctx, void *data, size_t data_sz) {
    // Copy event data to local queue to free space from the shared ring buffer for more kernel events.
    ebpf_event *new_event = (ebpf_event *)malloc(data_sz);

    if (!new_event) {
        LogError("Failed to allocate memory for event\n");
        return -1;
    }

    memcpy(new_event, data, data_sz);
    // Enqueue the copied event into the concurrent queue for background processing
    g_event_queue.Enqueue(new_event);

    return 0;
}

/** 
 * Creates the process event and debug ring buffer maps. Adds the maps this runner needs to all the per-pip outer maps.
 * */
int SetupMaps(struct sandbox_bpf *skel) {
    // If ebpf is already loaded, we need to reuse the pinned maps. This is something the ebpf helpers will do on load(), but
    // that logic is intertwined with loading the bpf object to the kernel, which is something that already happened and we want to avoid
    if (g_ebpf_should_force_ebpf_loading || g_ebpf_already_loaded)
    {
        if (ReuseMaps(skel))
        {
            Cleanup(skel);
            return -1;
        }
    }

    if (BindPerPipMaps(skel))
    {
        Cleanup(skel);
        return -1;
    }

    // Retrieve the pid map
    g_pid_map_fd = bpf_object__find_map_fd_by_name(skel->obj, "pid_map");
    if (g_pid_map_fd < 0) {
        LogError("finding pid_map in obj file failed\n");
        Cleanup(skel);
        return -1;
    }

    g_active_ring_buffer.store(new buildxl::linux::ebpf::EventRingBuffer(g_bxl, &g_root_process_exited, &g_stop, g_event_queue, RingBufferOutOfSpaceCallback));
    
    g_bxl->LogDebug(getpid(), "Creating ring buffer instance with counter %d", g_active_ring_buffer.load()->GetId());
    
    if (g_active_ring_buffer.load()->Initialize())
    {
        Cleanup(skel);
        return -1;
    }

    int ring_buffer_fd = g_active_ring_buffer.load()->GetRingBufferFd();

    // Add the file access ring buffer to the per-pip outer map
    int key = g_runner_pid;
    if (bpf_map_update_elem(g_file_access_per_pip_fd, &key, &ring_buffer_fd, BPF_ANY))
    {
        LogError("Failed to add file access ring buffer to outer map for runner PID %d: %s\n", key, strerror(errno));
        Cleanup(skel);
        return -1;
    }
    else
    {
        g_bxl->LogDebug(getpid(), "Added file access ring buffer for runner PID %d", key);
    }
    
    LIBBPF_OPTS(bpf_map_create_opts, debug_buffer_options);
    int debug_buffer_fd =  bpf_map_create(BPF_MAP_TYPE_RINGBUF, "debug_ring_buffer", 0, 0, DEBUG_RINGBUFFER_SIZE, &debug_buffer_options);
    if (debug_buffer_fd < 0)
    {
        LogError("Failed to create debug ring buffer: [%d]%s\n", errno, strerror(errno));
        Cleanup(skel);
        return -1;
    }

    g_debug_ring_buffer = ring_buffer__new(debug_buffer_fd, HandleDebugEvents, /* ctx */ NULL, /* opts */ NULL);
    if (!g_debug_ring_buffer) {
        LogError("Failed to create debug ring buffer manager\n");
        Cleanup(skel);
        return -1;
    }

    // Add the debug ring buffer to the per-pip outer map
    if (bpf_map_update_elem(g_debug_buffer_per_pip_fd, &key, &debug_buffer_fd, BPF_ANY))
    {
        LogError("Failed to add debug ring buffer to outer map for runner PID %d: %s\n", key, strerror(errno));
        Cleanup(skel);
        return -1;
    }
    else
    {
        g_bxl->LogDebug(getpid(), "Added debug ring buffer for runner PID %d", key);
    }

    LIBBPF_OPTS(bpf_map_create_opts, event_cache_options);
    int event_cache_fd =  bpf_map_create(BPF_MAP_TYPE_LRU_HASH, "event_cache", sizeof(struct cache_event_key), sizeof(short), EVENT_CACHE_MAP_SIZE, &event_cache_options);
    if (event_cache_fd < 0)
    {
        LogError("Failed to event cache: [%d]%s\n", errno, strerror(errno));
        Cleanup(skel);
        return -1;
    }

    // Add the event cache to the per-pip outer map
    if (bpf_map_update_elem(g_event_cache_per_pip_fd, &key, &event_cache_fd, BPF_ANY))
    {
        LogError("Failed to add event cache to outer map for runner PID %d: %s\n", key, strerror(errno));
        Cleanup(skel);
        return -1;
    }
    else
    {
        g_bxl->LogDebug(getpid(), "Added event cache for runner PID %d", key);
    }

    LIBBPF_OPTS(bpf_map_create_opts, string_cache_options);
    int string_cache_fd =  bpf_map_create(BPF_MAP_TYPE_LRU_HASH, "string_cache", sizeof(char[STRING_CACHE_PATH_MAX]), sizeof(short), STRING_CACHE_MAP_SIZE, &string_cache_options);
    if (string_cache_fd < 0)
    {
        LogError("Failed to create string cache: [%d]%s\n", errno, strerror(errno));
        Cleanup(skel);
        return -1;
    }

    // Add the string cache to the per-pip outer map
    if (bpf_map_update_elem(g_string_cache_per_pip_fd, &key, &string_cache_fd, BPF_ANY))
    {
        LogError("Failed to add string cache to outer map for runner PID %d: %s\n", key, strerror(errno));
        Cleanup(skel);
        return -1;
    }
    else
    {
        g_bxl->LogDebug(getpid(), "Added string cache for runner PID %d", key);
    }

    LIBBPF_OPTS(bpf_map_create_opts, breakaway_processes_options);
    g_breakaway_processes_map_fd =  bpf_map_create(BPF_MAP_TYPE_ARRAY, "breakaway_processes", sizeof(uint32_t), sizeof(breakaway_process), MAX_BREAKAWAY_PROCESSES, &breakaway_processes_options);
    if (g_breakaway_processes_map_fd < 0)
    {
        LogError("Failed to create breakaway process map: [%d]%s\n", errno, strerror(errno));
        Cleanup(skel);
        return -1;
    }

    // Add the breakaway process map to the per-pip outer map
    if (bpf_map_update_elem(g_breakaway_processes_per_pip_fd, &key, &g_breakaway_processes_map_fd, BPF_ANY))
    {
        LogError("Failed to add breakaway process map to outer map for runner PID %d: %s\n", key, strerror(errno));
        Cleanup(skel);
        return -1;
    }
    else
    {
        g_bxl->LogDebug(getpid(), "Added breakaway process map %d", key);
    }
    
    LIBBPF_OPTS(bpf_map_create_opts, last_path_per_cpu_options);
    // The number of entries in the last path per cpu map is set to the number of CPUs
    int max_concurrency = std::thread::hardware_concurrency();
    
    int last_path_per_cpu =  bpf_map_create(BPF_MAP_TYPE_HASH, "last_path_per_cpu", sizeof(__u32), sizeof(char[PATH_MAX]), max_concurrency, &last_path_per_cpu_options);
    if (last_path_per_cpu < 0)
    {
        LogError("Failed to create last path per cpu map: [%d]%s\n", errno, strerror(errno));
        Cleanup(skel);
        return -1;
    }

    // Add the last path per cpu map to the per-pip outer map
    if (bpf_map_update_elem(g_last_path_per_pip_fd, &key, &last_path_per_cpu, BPF_ANY))
    {
        LogError("Failed to add last path per cpu map with max entries %d to outer map for runner PID %d: %s\n", max_concurrency, key, strerror(errno));
        Cleanup(skel);
        return -1;
    }
    else
    {   
        g_bxl->LogDebug(getpid(), "Added last path per cpu map %d", key);
    }

    if (PopulateBreakawayProcessesMap())
    {
        Cleanup(skel);
        return -1;
    }

    return 0;
}

// Defensively cleans up all the pinned maps that we have created.
void CleanupPinnedMaps(struct sandbox_bpf *skel) {
    // We don't care about cleaning up the debug ring buffer map, ring buffers should get clean up automatically
    bpf_map* pinned_maps[PINNED_MAPS_SIZE] = {
        skel->maps.pid_map, 
        skel->maps.file_access_per_pip, 
        skel->maps.debug_buffer_per_pip, 
        skel->maps.breakaway_processes_per_pip,
        skel->maps.sandbox_options_per_pip,
        skel->maps.event_cache_per_pip,
        skel->maps.string_cache_per_pip,
        skel->maps.stats_per_pip,
        skel->maps.last_path_per_pip
    };

    for (int i = 0 ; i < PINNED_MAPS_SIZE; i++) {
        bpf_map* pinned_map = pinned_maps[i];

        // Retrieve the key size and create a buffer to store it
        __u32 key_size = bpf_map__key_size(pinned_map);
        // use char so alignment is respected
        auto key = new char[key_size];

        int res = bpf_map__get_next_key(pinned_map, NULL, key, key_size);
        while (res != -ENOENT)
        {
            if (bpf_map__delete_elem(pinned_map, key, key_size, /* flags */ 0))
            {
                // We don't really care if the deletion fails, we are doing this defensively anyway.
                // But make sure we don't loop forever
                break;
            }

            res = bpf_map__get_next_key(pinned_map, NULL, key, key_size);
        }

        delete[] key;
    }
}

unsigned int GetMaxConcurrency()
{
    unsigned int max_procs;
    // Check whether max concurrency is set in the designated environment variable. Its presence means that
    // BuildXL is hinting this value to the runner.
    const char *maxConcurrency = getenv(BxlMaxConcurrency);
    if (is_null_or_empty(maxConcurrency))
    {
        // This variable might not be set if the runner is not launched by BuildXL.
        // In this case we try to use the number of physical cores
        max_procs = std::thread::hardware_concurrency();
        // A value of 0 means the number couldn't be retrieved
        if (max_procs == 0) {
            // Just use an arbitrary default in that case
            max_procs = 32;
        }
    }
    else
    {
        max_procs = std::atoi(maxConcurrency);
    }

    return max_procs;
}

// Configures the per-pip maps sizes based on the maximum concurrency.
int ConfigurePerPipMapSizes(struct sandbox_bpf *skel) {

    unsigned int concurrency = GetMaxConcurrency();

    // In this case we are going to load EBPF programs, but we still share the same per-pip maps with an already loaded instance.
    // We cannot resize maps to a different value, so retrieve the existing size and use that.
    if (g_ebpf_already_loaded && g_ebpf_should_force_ebpf_loading)
    {
        // Any per-pip map size is fine, since we use the same size for all of them
        const char* pin_path = bpf_map__get_pin_path(skel->maps.file_access_per_pip);
        if (pin_path == nullptr) {
            LogError("Failed to retrieve pin path for map file_access_per_pip\n");
            return 1;
        }
        int pin_fd = bpf_obj_get(pin_path);
        if (pin_fd < 0) {
            LogError("Failed to get pin fd for map file_access_per_pip: %s\n", strerror(errno));
            return 1;
        }

        struct bpf_map_info map_info;
    	__u32 map_info_len = sizeof(map_info);
	    int err;
    	memset(&map_info, 0, map_info_len);
	    err = bpf_map_get_info_by_fd(pin_fd, &map_info, &map_info_len);
        if (err) {
            LogError("Failed to get map info for file_access_per_pip: %s\n", strerror(errno));
            close(pin_fd);
            return 1;
        }
        
        g_bxl->LogDebug(getpid(), "EBPF was force loaded. Concurrency was originally requested to be '%u', but the existing one '%d' was used", concurrency, map_info.max_entries);

        concurrency = map_info.max_entries;
        close(pin_fd);
    }
    
    bpf_map* per_pip_maps[] = {
        skel->maps.file_access_per_pip, 
        skel->maps.debug_buffer_per_pip, 
        skel->maps.breakaway_processes_per_pip,
        skel->maps.sandbox_options_per_pip,
        skel->maps.event_cache_per_pip,
        skel->maps.string_cache_per_pip,
        skel->maps.stats_per_pip,
        skel->maps.last_path_per_pip
    };

    for (int i = 0 ; i < 8; i++) {
        bpf_map* per_pip_map = per_pip_maps[i];

        if (bpf_map__set_max_entries(per_pip_map, concurrency))
        {
            return 1;
        }
    }

    g_bxl->LogDebug(getpid(), "EBPF map sizes set to '%u'", concurrency);

    return 0;
}

void DeletePerPipMaps(sandbox_bpf *skel, pid_t runner_pid) {
    // Remove the pip reference from the outer per-pip maps
    DeletePerPipMap(g_file_access_per_pip_fd, runner_pid, "file access");
    DeletePerPipMap(g_debug_buffer_per_pip_fd, runner_pid, "debug buffer");
    DeletePerPipMap(g_event_cache_per_pip_fd, runner_pid, "event cache");
    DeletePerPipMap(g_string_cache_per_pip_fd, runner_pid, "string cache");
    DeletePerPipMap(g_breakaway_processes_per_pip_fd, runner_pid, "breakaway processes");
    DeletePerPipMap(g_sandbox_options_per_pip_map_fd, runner_pid, "sandbox options");
    DeletePerPipMap(g_stats_per_pip_map_fd, runner_pid, "stats");
    DeletePerPipMap(g_last_path_per_pip_fd, runner_pid, "last path");
}

int Start(sandbox_bpf *skel, char **argv) {
    auto start = std::chrono::high_resolution_clock::now();
    
    g_ebpf_already_loaded = IsEbpfAlreadyLoaded();
    g_ebpf_should_force_ebpf_loading = ShouldForceEBPFLoading();
    
    int err = 0;

    // If our EBPF programs are not loaded, we only want to do this once
    // since this is time consuming and we are on a hot path.
    // The general execution model is such that one 'daemon' is expected to be launched and remain active for the 
    // whole duration of a build. The daemon is just a regular sandboxed process, and will load EBPF for the rest of the build.
    // Subsequent pips (wrapped in this same runner) will just find EBPF loaded and will just reuse the same instance.
    // But observe that if e.g. a unit test that needs EBPF is ran outside of BuildXL, that should still work because each
    // runner instance is capable of loading EBPF if that's not already loaded.
    if (g_ebpf_should_force_ebpf_loading || !g_ebpf_already_loaded)
    {
        g_bxl->LogDebug(getpid(), "Loading EBPF programs");

        // Configure the per-pip maps sizes based on the maximum concurrency
        if (ConfigurePerPipMapSizes(skel))
        {
            LogError("Failed to configure per-pip map sizes\n");
            Cleanup(skel);
            return -1;
        }

        err = sandbox_bpf::load(skel);
        if (err)
        {
            LogError("Failed to load BPF skeleton\n");
            Cleanup(skel);
            return -err;
        }

        /* Attach tracepoint handler */
        err = sandbox_bpf::attach(skel);
        if (err) {
            LogError("Failed to attach BPF skeleton\n");
            Cleanup(skel);
            return -err;
        }

        // Being defensive: we just loaded EBPF, so make sure
        // pinned maps are clean (which could have been left with data from
        // some unhandled/unclean exit).
        // If we are in a forced loading scenario, we don't want to clean up the pinned maps since this is likely a test running as part of a build
        if (!g_ebpf_already_loaded)
        {
            CleanupPinnedMaps(skel);
        }
    }
    else {
        g_bxl->LogDebug(getpid(), "EBPF programs already loaded");
    }

    if (signal(SIGINT, SigIntHandler) == SIG_ERR || signal(SIGTERM, SigIntHandler) == SIG_ERR || signal(SIGQUIT, SigIntHandler) == SIG_ERR) {
        LogError("Failed to set signal handler with error: %s\n", strerror(errno));
        Cleanup(skel);
        return -err;
    }

    // Create the maps needed by the runner
    if(SetupMaps(skel))
    {
        // Errors are logged already
        Cleanup(skel);
        return -1;
    }

    // Initialize the semaphore that signals that the root pid has been populated
    if(sem_init(&g_root_process_populated_semaphore, /* pshared */ 0, /* initial value */ 0) == -1) {
        LogError("Failed to initialize root pid semaphore with error %s\n", strerror(errno));
        Cleanup(skel);
        return -1;
    }

    // Start child thread that waits for the  process tree to exit
    pthread_t waitForProcessTreeExitThread;
    if (pthread_create(&waitForProcessTreeExitThread, NULL, WaitForProcessTreeToExit, (void *)argv) != 0) {
        LogError("Process exit monitoring thread failed to start %s\n", strerror(errno));
        Cleanup(skel);
        return -1;
    }

    // Child thread listening for debug events
    pthread_t debug_message_thread;
    if (pthread_create(&debug_message_thread, NULL, PollDebugBuffer, NULL) != 0) {
        LogError("Debug message thread failed to start %s\n", strerror(errno));
        Cleanup(skel);
        return -1;
    }

    // This thread waits on `g_event_queue` which receives events from the file access and debug ring buffers
    // written to by eBPF programs..
    // It is responsible for processing those events and sending them to the managed side.
    if (pthread_create(&g_event_queue_thread, NULL, HandleEventQueue, NULL) != 0) {
        LogError("Event queue message thread failed to start %s\n", strerror(errno));
        Cleanup(skel);
        return -1;
    }

    auto end = std::chrono::high_resolution_clock::now();
    auto duration = std::chrono::duration_cast<std::chrono::milliseconds>(end - start);
    g_bxl->LogDebug(getpid(), "Sandbox load time: %d ms", duration.count());

    // TODO: [pgunasekara] this shouldn't be necessary to do, figure out why TMPDIR is missing
    // If the TMPDIR environment variable is not set, but TMP is set, then set TMPDIR to TMP
    if (getenv("TMPDIR") == nullptr && getenv("TMP") != nullptr) {
        setenv("TMPDIR", getenv("TMP"), 1);
    }

    // Start root process
    int res = RunRootProcess(argv[1], &argv[1], environ);
    if (res != 0) {
        LogError("Failed to start root process\n");
        Cleanup(skel);
        return -1;
    }

    g_bxl->LogDebug(getpid(), "Activating the ring buffer for runner PID %d", g_runner_pid);
    if (g_active_ring_buffer.load()->NotifyActivated())
    {
        Cleanup(skel);
        return -1;
    }

    // Wait for the process tree exit thread to finish
    pthread_join(waitForProcessTreeExitThread, NULL);

    // ** Do not send any log messages after this point, managed side may start the tear down and the FIFO might be gone **

    // Here the assumption is that after the whole process tree has exited, no new overflow notifications will be sent (since no new events should be emitted).
    // So we shouldn't hit a race where the active ring buffer is being swapped while we are trying to deactivate/complete it.
    // After the process tree has exited, we can safely terminate the active buffer, there shouldn't be any more events to process.
    g_active_ring_buffer.load()->Terminate();

    FlushDebugRingBufferEvents();
    
    // Notify the event queue thread to stop processing events and wait for it to finish
    g_event_queue.Enqueue(POISON_PILL);
    pthread_join(g_event_queue_thread, NULL);

    delete g_active_ring_buffer.load();
    
    // Not particularly necessary, but let's do due diligence.
    // If we never saw the root process exit event, the handler will emit it on the destructor. But in theory it should
    // not be possible to reach this point without the root process exiting.
    delete g_syscallHandler;

    // At this point all the process tree should have exited, and the event queue thread should have processed all the events.
    // Let's remove the entries from the per-pip maps that we have created for this pip.
    DeletePerPipMaps(skel, g_runner_pid);

    Cleanup(skel);

    return g_exit_code;
}

int SetAutoLoad(struct sandbox_bpf *skel) {
    // Get the Linux kernel major, minor, and patch version numbers.
    struct utsname uts;
    if (uname(&uts) < 0) {
        LogError("Failed to get kernel version: %s\n", strerror(errno));
        return -1;
    }

    char *ptr = uts.release;
    int versions[3] = {0};
    for (int i = 0; i < 3 && ptr != NULL;) {
        if (isdigit(*ptr) != 0) {
            versions[i] = strtol(ptr, &ptr, 10);
            i++;
        }
        else {
            ptr++;
        }
    }

    int currentVersion = KERNEL_VERSION(versions[0], versions[1], versions[2]);
    if (currentVersion < KERNEL_VERSION(6, 8, 0)) {
        // Enable auto loading for programs that are used on older kernels
        bpf_program__set_autoload(skel->progs.step_into_exit, true);
    }
    else {
        // Enable auto loading for programs that are used on newer kernels
        bpf_program__set_autoload(skel->progs.pick_link_exit, true);
    }

    return 0;
}

// Main function for the sandbox runner.
int main(int argc, char **argv) {
    struct sandbox_bpf *skel;

    // Initialize the BxlObserver
    // We want to do this before we initialize libbpf because we want to redirect
    // libbpf messages to BxlObserver.
    g_bxl = BxlObserver::GetInstance();
    g_bxl->Init();
    g_runner_pid = getpid();

    /* Set up libbpf errors and debug info callback */
    libbpf_set_print(LibBpfPrintFn);

    /* Open load and verify BPF application */
    skel = sandbox_bpf::open();
    if (!skel) {
        LogError("Failed to open BPF skeleton\n");
        return 1;
    }

    // Autoload must be set after calling open and before loading the BPF skeleton.
    if (SetAutoLoad(skel) != 0) {
        LogError("Failed to set auto load for BPF programs\n");
        Cleanup(skel);
        return 1;
    }

    return Start(skel, argv);
}