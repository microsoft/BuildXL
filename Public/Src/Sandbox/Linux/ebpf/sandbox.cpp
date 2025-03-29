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

/**
 * Libbpf, bpf skeleton, and shared bpf includes
 */
#include "bpf/bpf.h"
#include "bpf/libbpf.h"
#include "ebpfcommon.h"
#include "sandbox.skel.h"

/**
 * BuildXL includes
 */
#include "bxl_observer.hpp"
#include "SyscallHandler.h"

/** Globals */
BxlObserver *g_bxl;
static volatile sig_atomic_t g_stop;
static volatile int g_exit_code;
int g_root_pid = 0;
struct ring_buffer *g_file_access_ring_buffer = NULL, *g_debug_ring_buffer = NULL;
int g_pid_map_fd = -1, g_breakaway_processes_map_fd = -1;
sem_t g_background_thread_semaphore;

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
        g_bxl->LogError(getpid(), format, args);

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

void LogDebugEvent(ebpf_event *event)
{
    switch (event->event_type) 
    {
        case EXEC: 
        {
            const ebpf_event_exec * exec_event = (const ebpf_event_exec *)event;
            g_bxl->LogDebug(
                exec_event->metadata.pid, 
                "kernel function: %s, operation: %s, exe path: '%s', args: '%s'",
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
                "kernel function: %s, operation: %s, S_ISREG: %d, S_ISDIR: %d, errno: %d %s, path: '%s'",
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
                "kernel function: %s, operation: %s, S_ISREG: %d, S_ISDIR: %d, errno: %d %s, source path: '%s', dest path '%s'",
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
        case DEBUG:
        {
            const ebpf_event_debug *debug_event = (const ebpf_event_debug *)event;
            g_bxl->LogDebug(debug_event->pid, "Debug message: %s", debug_event->message);
            break;
        }
        default:
        {
            break;
        }
    }
}

/**
 * Handles SigIntHandler signal.
 */
void SigIntHandler(int signo) {
    g_stop = 1;
}

/**
 * Whether a path is fully resolved (i.e. start with a '/')
 */
bool IsPathFullyResolved(const char* path)
{
    return path != NULL && path[0] == '/';
}

/**
 * Callback for file access ring buffer.
 */
int HandleEvent(void *ctx, void *data, size_t data_sz) {
    ebpf_event *event = (ebpf_event *)data;

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

/**
 * Start process and update PID table with root process PID.
 */
int RunRootProcess(int map_fd, const char *file, char *const argv[], char *const envp[]) {
    // Place a semaphore in shared memory, so both parent and child can see it
    sem_t* semaphore = (sem_t*) mmap(NULL, sizeof(*semaphore), PROT_READ | PROT_WRITE, MAP_SHARED | MAP_ANONYMOUS, -1, 0);
    if (semaphore == MAP_FAILED)
    {
        return -1;
    }

    // The semaphore is initialized locked
    if (sem_init(semaphore, /* shared */ 1, /* initial value */ 0) == -1)
    {
        g_bxl->LogDebug(getpid(), "Can't init semaphore");
        return -1;
    }

    pid_t pid = fork();
    if (pid == 0) {
        // Child
        // Wait on the semaphore to make sure the parent process has already populated the map
        sem_wait(semaphore);
        // Now the child's pid is populated and we can proceed
        sem_post(semaphore);
        sem_destroy(semaphore);

        execve(file, argv, envp);
        return -1;
    }
    else  {
        g_root_pid = pid;

        sem_post(&g_background_thread_semaphore);

        // Our managed side tracking expects a 'clone/fork' event before an exec in order to assign the right pids and update the active process collection. Doing
        // this on managed side is racy (since the pid to use will be available only after the root process has started and events may have arrived already)
        SendInitForkEvent(getpid(), getppid(), file);
        SendInitForkEvent(pid, getpid(), file);

        // Add child pid to map
        bpf_map_update_elem(map_fd, &g_root_pid, &g_root_pid, BPF_ANY);
        // Unlock the semaphore so the child process can proceed
        sem_post(semaphore);
    }

    return 0;
}

/**
 * Wait for root process to exit and perform clean up operations.
 */
void *WaitForRootProcessToExit(void *argv) {
    int status = 0;
    // TODO: assert that g_root_pid is not 0
    sem_wait(&g_background_thread_semaphore);

    // TODO: this waitpid should happen in a loop to handle other events such as signals, etc.
    waitpid(g_root_pid, &status, 0);

    if (WIFEXITED(status)) {
        g_exit_code = WEXITSTATUS(status);
    }
    else {
        // Something else happened and the child exited because of a signal/core dump/etc
        g_exit_code = -1;
    }

    // Consume any remaining items in the ring buffer
    ring_buffer__consume(g_file_access_ring_buffer);

    // Just being consistent with the injected exec event, we use the root exe
    // as the file path
    char* file = ((char**) argv)[1];
    // Inject an exit event for the launcher process before we tear down EBPF
    auto exit_event = buildxl::linux::SandboxEvent::ExitSandboxEvent(
        /* system_call */   "__teardown__exit",
        /* path */          file,
        /* pid */           getpid(),
        /* ppid */          getppid());
    exit_event.SetMode(g_bxl->get_mode(file));
    exit_event.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kDoNotResolve);
    g_bxl->CreateAndReportAccess(exit_event);

    g_stop = 1;

    return NULL;
}

/**
 * Populates a set of breakaway processes from the file access manifest to be used by the kernel side.
 */
void PopulateBreakawayProcessesMap() {
    auto breakaway_processes = g_bxl->GetBreakawayChildProcesses();
    auto breakaway_processes_count = std::min(breakaway_processes.size(), static_cast<size_t>(MAX_BREAKAWAY_PROCESSES));;

    for (int i = 0; i < breakaway_processes_count; i++) {
        breakaway_process process = {0};
        strncpy(process.tool, breakaway_processes[i].GetExecutable().c_str(), sizeof(process.tool));
        process.tool_len = breakaway_processes[i].GetExecutable().length();
        strncpy(process.arguments, breakaway_processes[i].GetRequiredArgs().c_str(), sizeof(process.arguments));
        process.arguments_len = breakaway_processes[i].GetRequiredArgs().length();
        process.args_ignore_case = breakaway_processes[i].GetRequiredArgsIgnoreCase();

        bpf_map_update_elem(g_breakaway_processes_map_fd, &i, &process, BPF_ANY);
    }
}

/**
 * Block on the debug event ring buffer and handle debug events.
 */
void *PollDebugBuffer(void *arg) {
    while (!g_stop) {
        int err = ring_buffer__poll(g_debug_ring_buffer, /* timeout_ms */ 60000);
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
 * Perform libbpf related cleanup.
 */
void Cleanup(struct sandbox_bpf *skel) {
    sandbox_bpf::destroy(skel);
}

int main(int argc, char **argv) {
    struct sandbox_bpf *skel;
    int err = 0;

    // Initialize the BxlObserver
    // We want to do this before we initialize libbpf because we want to redirect
    // libbpf messages to BxlObserver.
    g_bxl = BxlObserver::GetInstance();
    g_bxl->Init();

    auto start = std::chrono::high_resolution_clock::now();

    /* Set up libbpf errors and debug info callback */
    libbpf_set_print(LibBpfPrintFn);

    /* Open load and verify BPF application */
    skel = sandbox_bpf::open();
    if (!skel) {
        LogError("Failed to open BPF skeleton\n");
        return 1;
    }

    sandbox_bpf::load(skel);    

    /* Attach tracepoint handler */
    err = sandbox_bpf::attach(skel);
    if (err) {
        LogError("Failed to attach BPF skeleton\n");
        Cleanup(skel);
        return -err;
    }

    if (signal(SIGINT, SigIntHandler) == SIG_ERR) {
        LogError("Failed to set signal handler with error: %s\n", strerror(errno));
        Cleanup(skel);
        return -err;
    }

    // Set up file access event ring buffer
    g_file_access_ring_buffer = ring_buffer__new(bpf_map__fd(skel->maps.file_access_ring_buffer), HandleEvent, /* ctx */ NULL, /* opts */ NULL);
    if (!g_file_access_ring_buffer) {
        LogError("Failed to create ring buffer\n");
        Cleanup(skel);
        return -1;
    }

    // Set up debug event ring buffer
    g_debug_ring_buffer = ring_buffer__new(bpf_map__fd(skel->maps.debug_buffer), HandleEvent, /* ctx */ NULL, /* opts */ NULL);
    if (!g_debug_ring_buffer) {
        LogError("Failed to create debug ring buffer\n");
        Cleanup(skel);
        return -1;
    }

    // Set up pid map
    g_pid_map_fd = bpf_object__find_map_fd_by_name(skel->obj, "pid_map");
    if (g_pid_map_fd < 0) {
        LogError("Finding pid_map in bpf object failed.\n");
        Cleanup(skel);
        return -1;
    }

    g_breakaway_processes_map_fd = bpf_object__find_map_fd_by_name(skel->obj, "breakaway_processes");
    if (g_breakaway_processes_map_fd < 0) {
        LogError("Finding breakaway_processes map in bpf object failed.\n");
        Cleanup(skel);
        return -1;
    }

    PopulateBreakawayProcessesMap();

    // Initialize the background thread semaphore
    if(sem_init(&g_background_thread_semaphore, /* pshared */ 0, /* initial value */ 0) == -1) {
        LogError("Failed to initialize background thread semaphore with error %s\n", strerror(errno));
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

    auto end = std::chrono::high_resolution_clock::now();
    auto duration = std::chrono::duration_cast<std::chrono::milliseconds>(end - start);
    g_bxl->LogDebug(getpid(), "Sandbox load time: %d ms", duration.count());

    // Start root process
    int res = RunRootProcess(g_pid_map_fd, argv[1], &argv[1], environ);
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
        if (err == -EINTR) {
            err = 0;
            break;
        }
        if (err < 0) {
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