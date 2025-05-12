// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

/**
 * System includes
 */
#include <chrono>
#include <errno.h>
#include <signal.h>
#include <stdio.h>
#include <string.h>
#include <string>
#include <sys/mman.h>
#include <sys/resource.h>
#include <sys/wait.h>
#include <unistd.h>
#include <thread>

/**
 * Libbpf, bpf skeleton, and shared bpf includes
 */
#include "bpf/bpf.h"
#include "bpf/libbpf.h"
#include "ebpfcommon.h"
#include "sandbox.skel.h"
#include "bpf/btf.h"

/**
 * BuildXL includes
 */
#include "bxl_observer.hpp"
#include "SPSCQueue.h"
#include "SyscallHandler.h"

// Max size for the name of a bpf program
#define MAX_PROG_FULL_NAME 128

/** Constants */
const int PINNED_MAPS_SIZE = 6;

/** Globals */
BxlObserver *g_bxl;
static volatile sig_atomic_t g_stop;
static volatile int g_exit_code;
int g_root_pid = 0, g_runner_pid = 0;
struct ring_buffer *g_file_access_ring_buffer = nullptr, *g_debug_ring_buffer = nullptr;
int g_pid_map_fd, g_sandbox_options_per_pip_map_fd, g_file_access_per_pip_fd = -1;
int g_debug_buffer_per_pip_fd, g_breakaway_processes_map_fd, g_event_cache_per_pip_fd, g_breakaway_processes_per_pip_fd = -1;
sem_t g_root_process_populated_semaphore;
bool g_ebpf_already_loaded = false;
buildxl::common::SPSCQueue<ebpf_event *> g_event_queue;
pthread_t g_event_queue_thread;

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

       // We log everything from warning and above as an error for now
       g_bxl->LogErrorArgList(getpid(), format, args);

       return 1;
    }

    return 0;
}

/**
 * Error logger for this program.
 */
static int LogError(const char *fmt, ...) {
    va_list args;
    va_start(args, fmt);
    // There is no actual constant for errors. But we always treat anything above
    // a warning level (inclusive) as an error
    LibBpfPrintFn(LIBBPF_WARN, fmt, args);
    va_end(args);

    return 1;
}

/**
 * Perform libbpf related cleanup.
 */
void Cleanup(struct sandbox_bpf *skel) {
    // Unload EBPF programs if this runner was the one loading them to begin with
    if (!g_ebpf_already_loaded) {
        sandbox_bpf::destroy(skel);
    }
}

void LogDebugEvent(ebpf_event *event)
{
    switch (event->event_type) 
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
                exec_event->exe_path,
                exec_event->args);
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
                double_event->src_path,
                double_event->dst_path);
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
    // Forward signal to root process
    if (g_root_pid != 0) {
        kill(g_root_pid, signo);
    }
}

/**
 * Whether a path is fully resolved (i.e. start with a '/')
 */
bool IsPathFullyResolved(const char* path)
{
    return path != NULL && path[0] == '/';
}

/**
 * Handles a provided ebpf event.
 */
void HandleEvent(ebpf_event *event) {
    LogDebugEvent(event);

    switch (event->event_type) {
        case EXEC: {
            buildxl::linux::ebpf::SyscallHandler::HandleExecEvent(g_bxl, (const ebpf_event_exec *)event);
            break;
        }
        case SINGLE_PATH:
            // For some operations (e.g. memory files) our path translation returns an empty string. Those cases
            // should match with the ones we don't care about tracing. So do not send that event to managed side but
            // let the log debug event call above log it, so we can investigate otherwise.
            if (IsPathFullyResolved(event->src_path))
            {
                buildxl::linux::ebpf::SyscallHandler::HandleSingleEvent(g_bxl, event);                
            }
            break;
        case DOUBLE_PATH:
        {
            // Same consideration for fully resolved paths as in the single path case
            const ebpf_event_double* double_event = (const ebpf_event_double *)event;
            if (IsPathFullyResolved(double_event->src_path) && IsPathFullyResolved(double_event->dst_path))
            {
                buildxl::linux::ebpf::SyscallHandler::HandleDoubleEvent(g_bxl, double_event);
            }
            break;
        }
        case DEBUG:
            buildxl::linux::ebpf::SyscallHandler::HandleDebugEvent(g_bxl, (const ebpf_event_debug *)event);
            break;
        default:
            LogError("Unhandled event type %d", event->event_type);
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

        if (event == (ebpf_event*) -1) {
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

/**
 * Callback for file access/debug event ring buffer.
 * We use a queue so we can read from the ringbuffer as fast as possible, and offload processing to another thread.
 * This helps avoid reservation issues when the ring buffer gets stressed by fast IO which produces a ton of events.
 */
int HandleBpfRingBufferEvent(void *ctx, void *data, size_t data_sz) {
    // Copy event data to local queue to free space from the shared ring buffer for more kernel events.
    ebpf_event *new_event = (ebpf_event *)malloc(data_sz);

    if (!new_event) {
        LogError("Failed to allocate memory for event\n");
        return -1;
    }

    memcpy(new_event, data, data_sz);
    // Enqueue the copied event into the SPSC queue for background processing
    g_event_queue.Enqueue(new_event);

    return 0;
}

/**
 * Sends the initial fork event to the managed side for the provided pid and ppid.
 */
void SendInitForkEvent(pid_t pid, pid_t ppid, const char *file) {
    auto fork_event = buildxl::linux::SandboxEvent::CloneSandboxEvent(
        /* system_call */   "__init__fork",
        /* pid */           pid,
        /* ppid */          ppid,
        /* src_path */      file);
    fork_event.SetMode(g_bxl->get_mode(file));
    fork_event.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kDoNotResolve);
    g_bxl->CreateAndReportAccess(fork_event);
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
        g_bxl->LogDebug(getpid(), "Can't add options to map");
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
        // Signal that the root pid is already populated
        sem_post(&g_root_process_populated_semaphore);

        // Our managed side tracking expects a 'clone/fork' event before an exec in order to assign the right pids and update the active process collection. Doing
        // this on managed side is racy (since the pid to use will be available only after the root process has started and events may have arrived already)
        SendInitForkEvent(getpid(), getppid(), file);
        SendInitForkEvent(pid, getpid(), file);

        // Add child pid to the pid map, associating it with pid of the runner for this pip.
        int key = g_runner_pid;
        if (bpf_map_update_elem(g_pid_map_fd, &g_root_pid, &key, BPF_ANY))
        {
            g_bxl->LogDebug(getpid(), "Can't add new pip id to map");
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
 * Wait for root process to exit and perform clean up operations.
 */
void *WaitForRootProcessToExit(void *argv) {
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

    // Consume any remaining items in the ring buffers
    ring_buffer__consume(g_file_access_ring_buffer);
    ring_buffer__consume(g_debug_ring_buffer);

    // Add poison pill to signal the event queue thread to exit.
    g_event_queue.Enqueue((ebpf_event*) -1);

    // Wait for the event queue thread to finish consuming the rest of the ring buffer.
    pthread_join(g_event_queue_thread, NULL);

    // Remove the pip reference from the outer per-pip maps
    int key = g_runner_pid;
    if (bpf_map_delete_elem(g_file_access_per_pip_fd, &key))
    {
        LogError("Error deleting ring buffer for runner PID %d:%s\n", key, strerror(errno));
    }
    else
    {
        g_bxl->LogDebug(getpid(), "Removed file access ring buffer for runner PID %d", key);
    }

    if (bpf_map_delete_elem(g_debug_buffer_per_pip_fd, &key))
    {
        LogError("Error deleting debug ring buffer for runner PID %d:%s\n", key, strerror(errno));
    }
    else
    {
        g_bxl->LogDebug(getpid(), "Removed debug ring buffer for runner PID %d", key);
    }

    if (bpf_map_delete_elem(g_event_cache_per_pip_fd, &key))
    {
        LogError("Error deleting event cache for runner PID %d:%s\n", key, strerror(errno));
    }
    else
    {
        g_bxl->LogDebug(getpid(), "Removed event cache for runner PID %d", key);
    }

    if (bpf_map_delete_elem(g_breakaway_processes_per_pip_fd, &key))
    {
        LogError("Error deleting breakaway map for runner PID %d:%s\n", key, strerror(errno));
    }
    else
    {
        g_bxl->LogDebug(getpid(), "Removed breakaway map for runner PID %d", key);
    }

    // Just being consistent with the injected exec event, we use the root exe
    // as the file path
    char* file = ((char**) argv)[1];
    // Inject an exit event for the runner process before we tear down EBPF
    auto exit_event = buildxl::linux::SandboxEvent::ExitSandboxEvent(
        /* system_call */   "__teardown__exit",
        /* path */          file,
        /* pid */           getpid(),
        /* ppid */          getppid());
    exit_event.SetMode(g_bxl->get_mode(file));
    exit_event.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kDoNotResolve);

    // After sending this event it is not safe to write into the pipe anymore
    // since the pip will be considered terminated and the pipe removed
    g_bxl->CreateAndReportAccess(exit_event);

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

/** 
 * Creates the process event and debug ring buffer maps
 * */
int SetupMaps(struct sandbox_bpf *skel) {
    // If ebpf is already loaded, we need to reuse the pinned maps. This is something the ebpf helpers will do on load(), but
    // that logic is intertwined with loading the bpf object to the kernel, which is something that already happened and we want to avoid
    if (g_ebpf_already_loaded)
    {   
        // These are the `PINNED_MAPS_SIZE` pinned maps we have. Retrieve their pin paths and reuse them
        bpf_map* pinned_maps[PINNED_MAPS_SIZE] = {
            skel->maps.pid_map, 
            skel->maps.file_access_per_pip, 
            skel->maps.debug_buffer_per_pip,
            skel->maps.breakaway_processes_per_pip,
            skel->maps.sandbox_options_per_pip,
            skel->maps.event_cache_per_pip
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
    }

    // Retrieve the pid map
    g_pid_map_fd = bpf_object__find_map_fd_by_name(skel->obj, "pid_map");
    if (g_pid_map_fd < 0) {
        LogError("finding pid_map in obj file failed\n");
        Cleanup(skel);
        return -1;
    }

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

    LIBBPF_OPTS(bpf_map_create_opts, file_access_options);
    int file_access_fd =  bpf_map_create(BPF_MAP_TYPE_RINGBUF, "file_access_ring_buffer", 0, 0, 4096 *  2048  /* PATH_MAX * 2048 entries */, &file_access_options);
    if (file_access_fd < 0)
    {
        LogError("Failed to create ring buffer\n");
        Cleanup(skel);
        return -1;
    }

    g_file_access_ring_buffer = ring_buffer__new(file_access_fd, HandleBpfRingBufferEvent, /* ctx */ NULL, /* opts */ NULL);
    if (!g_file_access_ring_buffer) {
        LogError("Failed to create ring buffer manager\n");
        Cleanup(skel);
        return -1;
    }

    // Add the file access ring buffer to the per-pip outer map
    int key = g_runner_pid;
    if (bpf_map_update_elem(g_file_access_per_pip_fd, &key, &file_access_fd, BPF_ANY))
    {
        LogError("Failed to add file access ring buffer to outer map for runner PID %d: %s\n", key, strerror(errno));
        Cleanup(skel);
        return -1;
    }
    else
    {
        g_bxl->LogDebug(getpid(), "Added file access ring buffer for runner PID %d", key);
    }

    // Retrieve the per-pip debug ring buffer and create a debug ring buffer for the current pip
    g_debug_buffer_per_pip_fd =  bpf_object__find_map_fd_by_name(skel->obj, "debug_buffer_per_pip");
    if (g_debug_buffer_per_pip_fd < 0)
    {
        LogError("Failed to retrieve debug ring buffer per pip\n");
        Cleanup(skel);
        return -1;
    }
    
    LIBBPF_OPTS(bpf_map_create_opts, debug_buffer_options);
    int debug_buffer_fd =  bpf_map_create(BPF_MAP_TYPE_RINGBUF, "debug_ring_buffer", 0, 0, 4096 * 1024, &debug_buffer_options);
    if (debug_buffer_fd < 0)
    {
        LogError("Failed to create debug ring buffer: [%d]%s\n", errno, strerror(errno));
        Cleanup(skel);
        return -1;
    }

    g_debug_ring_buffer = ring_buffer__new(debug_buffer_fd, HandleBpfRingBufferEvent, /* ctx */ NULL, /* opts */ NULL);
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

    // Retrieve the per-pip event cache and create an event cache for the current pip
    g_event_cache_per_pip_fd =  bpf_object__find_map_fd_by_name(skel->obj, "event_cache_per_pip");
    if (g_event_cache_per_pip_fd < 0)
    {
        LogError("Failed to retrieve event cache per pip\n");
        Cleanup(skel);
        return -1;
    }

    LIBBPF_OPTS(bpf_map_create_opts, event_cache_options);
    int event_cache_fd =  bpf_map_create(BPF_MAP_TYPE_LRU_HASH, "event_cache", sizeof(struct cache_event_key), sizeof(short), 65535, &event_cache_options);
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

    // Retrieve the per-pip breakaway process map
    g_breakaway_processes_per_pip_fd = bpf_object__find_map_fd_by_name(skel->obj, "breakaway_processes_per_pip");
    if (g_breakaway_processes_per_pip_fd < 0) {
        LogError("Finding breakaway_processes per pip in bpf object failed.\n");
        Cleanup(skel);
        return -1;
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

    
    if (PopulateBreakawayProcessesMap())
    {
        Cleanup(skel);
        return -1;
    }

    return 0;
}

void CleanupPinnedMaps(struct sandbox_bpf *skel) {
    // We don't care about cleaning up the debug ring buffer map, ring buffers should get clean up automatically
    bpf_map* pinned_maps[PINNED_MAPS_SIZE] = {
        skel->maps.pid_map, 
        skel->maps.file_access_per_pip, 
        skel->maps.debug_buffer_per_pip, 
        skel->maps.breakaway_processes_per_pip,
        skel->maps.sandbox_options_per_pip,
        skel->maps.event_cache_per_pip
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

int main(int argc, char **argv) {
    struct sandbox_bpf *skel;
    int err = 0;

    // Initialize the BxlObserver
    // We want to do this before we initialize libbpf because we want to redirect
    // libbpf messages to BxlObserver.
    g_bxl = BxlObserver::GetInstance();
    g_bxl->Init();
    g_runner_pid = getpid();

    auto start = std::chrono::high_resolution_clock::now();

    /* Set up libbpf errors and debug info callback */
    libbpf_set_print(LibBpfPrintFn);

    g_ebpf_already_loaded = IsEbpfAlreadyLoaded();
    
    /* Open load and verify BPF application */
    skel = sandbox_bpf::open();
    if (!skel) {
        LogError("Failed to open BPF skeleton\n");
        return 1;
    }

    // If our EBPF programs are not loaded, we only want to do this once
    // since this is time consuming and we are on a hot path.
    // The general execution model is such that one 'daemon' is expected to be launched and remain active for the 
    // whole duration of a build. The daemon is just a regular sandboxed process, and will load EBPF for the rest of the build.
    // Subsequent pips (wrapped in this same runner) will just find EBPF loaded and will just reuse the same instance.
    // But observe that if e.g. a unit test that needs EBPF is ran outside of BuildXL, that should still work because each
    // runner instance is capable of loading EBPF if that's not already loaded.
    if (!g_ebpf_already_loaded)
    {
        // Set the number of entries in the per-pip outer map to be the number of physical cores
        // BuildXL should never schedule more concurrent process pips than the number of cores
        unsigned int max_procs = std::thread::hardware_concurrency();
        // A value of 0 means the number couldn't be retrieved
        if (max_procs == 0) {
            // Just use an arbitrary default in that case
            max_procs = 32;
        }

        g_bxl->LogDebug(getpid(), "Loading EBPF programs");
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
        CleanupPinnedMaps(skel);
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

    // Start child thread that waits for the root process to exit
    pthread_t thread;
    if (pthread_create(&thread, NULL, WaitForRootProcessToExit, (void *)argv) != 0) {
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

    while (!g_stop) {
        // Process Events
        // When the ring buffer is empty, poll will block for the specified timeout
        // If the timeout is hit, poll will return 0
        err = ring_buffer__poll(g_file_access_ring_buffer, /* timeout_ms */ 100);
        // We might get back an EINTR if the process gets any signal. But in this 
        // case we should keep polling. If any of those signals actually means that
        // the process has exited, we are controlling that from WaitForRootProcessToExit,
        // and that thread will set g_stop accordingly
        if (err < 0 && err != -EINTR) {
            LogError("Error polling ring buffer %d\n", err);
            break;
        }
    }

    start = std::chrono::high_resolution_clock::now();
    Cleanup(skel);
    end = std::chrono::high_resolution_clock::now();
    duration = std::chrono::duration_cast<std::chrono::milliseconds>(end - start);

    // Sometimes the pipe is gone already
    // g_bxl->LogDebug(getpid(), "Sandbox cleanup time: %d ms", duration.count());

    return g_exit_code;
}