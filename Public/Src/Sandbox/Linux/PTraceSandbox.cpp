// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include "PTraceSandbox.hpp"
#include <linux/filter.h>
#include <linux/seccomp.h>
#include <sys/prctl.h>
#include <sys/ptrace.h>
#include <sys/reg.h>
#include <sys/wait.h>

#define SYSCALL_NAME_TO_NUMBER(name) __NR_##name
#define SYSCALL_NAME_STRING(name) #name

// This macro fills in the seccomp_data data structure
// The first statement checks if the current syscall matches the syscall number provided.
// If so, then it will not modify the program counter (the 0 set in the third arg is for PC + 0)
// If the syscall number does not match, then it will increment the program counter (PC + 1) and jump to skip over the next statement that has the SECCOMP_RET_TRACE
// The BPF statement with SECCOMP_RET_TRACE indicates that we should invoke the tracer (ie: the parent process will be signalled by ptrace)
#define TRACE_SYSCALL(name) \
        BPF_JUMP(BPF_JMP+BPF_JEQ+BPF_K, SYSCALL_NAME_TO_NUMBER(name), 0, 1), \
        BPF_STMT(BPF_RET+BPF_K, SECCOMP_RET_TRACE)

// There are "new" versions of certain syscalls (such as fstatat).
// The name of the function does not include the "new" bit, but the name in the kernel includes this prefix
// Use this macro to trace those syscalls
// The other "new" variants of the macros in this file achieve the same thing
#define TRACE_SYSCALL_NEW(name) TRACE_SYSCALL(new##name)

#define HANDLER_FUNCTION(syscallName) void PTraceSandbox::MAKE_HANDLER_FN_NAME(syscallName) ()
#define HANDLER_FUNCTION_NEW(syscallName) HANDLER_FUNCTION(new##syscallName)

#define CHECK_AND_CALL_HANDLER(syscallName) \
        case SYSCALL_NAME_TO_NUMBER(syscallName): \
            PTraceSandbox::MAKE_HANDLER_FN_NAME(syscallName) (); \
            break
#define CHECK_AND_CALL_HANDLER_NEW(syscallName) CHECK_AND_CALL_HANDLER(new##syscallName)

PTraceSandbox::PTraceSandbox(BxlObserver *bxl)
{
    m_bxl = bxl;
    m_traceePid = 0;
    m_emptyStr = "";
}

PTraceSandbox::~PTraceSandbox()
{
}

int PTraceSandbox::ExecuteWithPTraceSandbox(const char *file, int fd, char *const argv[], char *const envp[])
{
    // Using ptrace requires a separate process
    // The forked child process will continue to do the exec, while the parent process will become the tracee
    // The child process will register itself as the tracee by using PTRACE_TRACEME
    // The parent process will pause until it is invoked by the tracee by using waitpid
    m_traceePid = m_bxl->real_fork();

    // a return value of 0 from fork() indicates that this is the child process
    if (m_traceePid == 0)
    {
        int execResult = ChildProcess(file, fd, argv, envp);

        if (execResult == -1)
        {
            execResult = errno;
        }

        // Child process has finished executing so we can exit here
        m_bxl->real__exit(execResult);
    }
    else
    {
        m_pidStr = std::to_string(m_traceePid);
        // Disabling the fd table for now to avoid inaccurate reports because the logic to clear the entries isn't set up properly on the ptrace sandbox
        m_bxl->disable_fd_table(); // TODO: [pgunasekara] re-enable this later

        auto status = ParentProcess();

        // Since the process called exec, once the forked process exits the main process can exit as well
        // The exit of the child should have already been reported, so we can directly call _exit here
        // Since this is the parent process theres nothing to report when exiting here.
        _exit(status);
    }

    // We should never hit this unless the fork call failed for some reason
    return -1;
}

int PTraceSandbox::ChildProcess(const char *file, int fd, char *const argv[], char *const envp[])
{
    // Filter for the syscalls that BXL is interested in tracing
    // Only the syscalls in here will be signalled to the main process by seccomp
    // List of available syscalls to ptrace: https://github.com/torvalds/linux/blob/master/arch/x86/entry/syscalls/syscall_64.tbl
    // NOTE: The set of syscalls here are not equivalent to the set of functions that are interposed by the regular sandbox
    // This is expected because not all of the interposed functions map directly to system calls in the kernel.
    // This set should capture all of the file accesses we already observe on the interpose sandbox.
    struct sock_filter filter[] = {
        // This statement loads the syscall number (seccomp_data.nr) into the accumulator
        BPF_STMT(BPF_LD+BPF_W+BPF_ABS, offsetof(struct seccomp_data, nr)),
        // The next set of statements indicates that we should stop the tracee if one of these syscalls are detected
        TRACE_SYSCALL(fork),
        TRACE_SYSCALL(clone),
        TRACE_SYSCALL(execveat),
        TRACE_SYSCALL(execve),
        TRACE_SYSCALL(stat),
        TRACE_SYSCALL(lstat),
        TRACE_SYSCALL(fstat),
        TRACE_SYSCALL_NEW(fstatat),
        TRACE_SYSCALL(access),
        TRACE_SYSCALL(faccessat),
        TRACE_SYSCALL(creat),
        TRACE_SYSCALL(open),
        TRACE_SYSCALL(openat),
        TRACE_SYSCALL(write),
        TRACE_SYSCALL(writev),
        TRACE_SYSCALL(pwritev),
        TRACE_SYSCALL(pwritev2),
        TRACE_SYSCALL(pwrite64),
        TRACE_SYSCALL(truncate),
        TRACE_SYSCALL(ftruncate),
        TRACE_SYSCALL(rmdir),
        TRACE_SYSCALL(rename),
        TRACE_SYSCALL(renameat),
        TRACE_SYSCALL(link),
        TRACE_SYSCALL(linkat),
        TRACE_SYSCALL(unlink),
        TRACE_SYSCALL(unlinkat),
        TRACE_SYSCALL(symlink),
        TRACE_SYSCALL(symlinkat),
        TRACE_SYSCALL(readlink),
        TRACE_SYSCALL(readlinkat),
        TRACE_SYSCALL(utime),
        TRACE_SYSCALL(utimes),
        TRACE_SYSCALL(utimensat),
        TRACE_SYSCALL(futimesat),
        TRACE_SYSCALL(mkdir),
        TRACE_SYSCALL(mkdirat),
        TRACE_SYSCALL(mknod),
        TRACE_SYSCALL(mknodat),
        TRACE_SYSCALL(chmod),
        TRACE_SYSCALL(fchmod),
        TRACE_SYSCALL(fchmodat),
        TRACE_SYSCALL(chown),
        TRACE_SYSCALL(fchown),
        TRACE_SYSCALL(lchown),
        TRACE_SYSCALL(fchownat),
        TRACE_SYSCALL(sendfile),
        TRACE_SYSCALL(copy_file_range),
        TRACE_SYSCALL(name_to_handle_at),
        TRACE_SYSCALL(exit),
        // SECCOMP_RET_ALLOW tells seccomp to allow all of the calls that were being filtered above (as opposed to killing them)
        // This would happen if none of the syscall numbers above get matched, and therefore should not stop the tracee
        BPF_STMT(BPF_RET+BPF_K, SECCOMP_RET_ALLOW),
    };

    struct sock_fprog prog = {
        .len = (unsigned short) (sizeof(filter)/sizeof(filter[0])),
        .filter = filter,
    };

    // Indicate to ptrace that this child process is the tracee
    ptrace(PTRACE_TRACEME, 0, NULL, NULL);

    // This prctl call prevents the child process from having a higher privilege than its parent
    // It is necessary to make the next PR_SET_SECCOMP call work (or else the parent process would need to run as root)
    if (prctl(PR_SET_NO_NEW_PRIVS, 1, 0, 0, 0) == -1) {
        m_bxl->real_printf("prctl(PR_SET_NO_NEW_PRIVS) failed\n");
        m_bxl->real__exit(1);
    }

    // Sets the seccomp filter
    if (prctl(PR_SET_SECCOMP, SECCOMP_MODE_FILTER, &prog) == -1) {
        m_bxl->real_printf("PR_SET_SECCOMP with SECCOMP_MODE_FILTER failed\n");
        m_bxl->real__exit(1);
    }

    // Send the initial SIGSTOP signal so that the parent process is unblocked on waitpid to set the PTRACE_O_TRACESECCOMP option
    kill(getpid(), SIGSTOP);

    // Finally perform the exec syscall, this call to exec along with the syscalls from the child process should be filtered and reported to the tracer by seccomp
    return fd == -1 ? m_bxl->real_execvpe(file, argv, envp) : m_bxl->real_fexecve(fd, argv, envp);
}

int PTraceSandbox::ParentProcess()
{
    BXL_LOG_DEBUG(m_bxl, "Starting ptrace for PID %d", m_traceePid);
    int status;

    // Wait for initial SIGSTOP from child
    waitpid(m_traceePid, &status, 0);
    // Enable seccomp tracing option
    ptrace(PTRACE_SETOPTIONS, m_traceePid, 0, PTRACE_O_TRACESECCOMP);
    // Resume child
    ptrace(PTRACE_SYSCALL, m_traceePid, 0, 0);

    // Main loop that handles signals from the child
    // waitpid should get signalled from the following:
    //  1. seccomp event
    //  2. Child process exited with status code
    //  3. Child process exited with signal
    while (true)
    {
        // Wait for signal from seccomp
        waitpid(m_traceePid, &status, 0);
        
        // Handle cases where the child processes has exited
        if (WIFEXITED(status))
        {
            BXL_LOG_DEBUG(m_bxl, "Child process exited with status '%d'", WEXITSTATUS(status));
            break;
        }
        else if (WIFSIGNALED(status))
        {
            BXL_LOG_DEBUG(m_bxl, "Child process exited with signal '%d'", WTERMSIG(status));
            break;
        }
        else if (!WIFSTOPPED(status))
        {
            BXL_LOG_DEBUG(m_bxl, "waitpid() returned bad status '%d'", status);
            break;
        }

        // Handle signal from seccomp
        if (status >> 8 == (SIGTRAP | (PTRACE_EVENT_SECCOMP << 8)))
        {
            long syscallNumber = ptrace(PTRACE_PEEKUSER, m_traceePid, sizeof(long) * ORIG_RAX, NULL);
            HandleSysCallGeneric(syscallNumber);
        }

        // Resume child process
        // Don't use PTRACE_SYSCALL here because we don't want to be interrupted again until the next filtered syscall is hit
        // This also means that we will not see the return value of the syscall. If it's necessary to do that, then in the handler
        // Call ptrace with PTRACE_SYSCALL and waitpid before returning back to this function (see PTraceSandbox::HandleChildProcess as an example of this)
        ptrace(PTRACE_CONT, m_traceePid, NULL, NULL);
    }

    return WEXITSTATUS(status);
}

void *PTraceSandbox::GetArgumentAddr(int index)
{
    long addr = sizeof(long);

    // Order of first 6 arguments: %rdi, %rsi, %rdx, %rcx, %r8, and %r9
    switch (index) {
        case 0: // Return value
            addr *= ORIG_RAX;
            break;
        case 1:
            addr *= RDI;
            break;
        case 2:
            addr *= RSI;
            break;
        case 3:
            addr *= RDX;
            break;
        case 4:
            addr *= R10;
            break;
        case 5:
            addr *= R8;
            break;
        case 6:
            addr *= R9;
            break;
        default:
            // Remaining arguments should be on the stack, but for what we need
            // the above 6 should be good enough and we should never hit this case
            addr = 0L;
            break;
    }

    return (void *)addr;
}

std::string PTraceSandbox::ReadArgumentString(int argumentIndex, bool nullTerminated, int length)
{
    void *addr = GetArgumentAddr(argumentIndex);
    // We are only interested in reading paths from the arguments so PATH_MAX (+1 for null terminator) should be safe to use here
    char argument[PATH_MAX + 1];
    char *addrRegValue = (char *)ptrace(PTRACE_PEEKUSER, m_traceePid, addr, 0);
    int currentStringLength = 0;

    while (true)
    {
        long addrMemoryLocation = ptrace(PTRACE_PEEKTEXT, m_traceePid, addrRegValue, NULL);
        if (addrMemoryLocation == -1)
        {
            BXL_LOG_DEBUG(m_bxl, "Error occured while executing PTRACE_PEEKTEXT: '%s'", strerror(errno));
        }

        addrRegValue += sizeof(long);

        char *currentArgReadChar = (char *)&addrMemoryLocation;
        bool finishedReadingArgument = false;

        for (int i = 0; i < sizeof(long); i++)
        {
            argument[currentStringLength] = *currentArgReadChar;
            currentArgReadChar++;
            currentStringLength++;

            if ((nullTerminated && *currentArgReadChar == '\0') || (length > 0 && currentStringLength == length))
            {
                finishedReadingArgument = true;
                break;
            }
        }

        if (finishedReadingArgument)
        {
            argument[currentStringLength] = '\0';
            break;
        }
    }

    return std::string(argument);
}

unsigned long PTraceSandbox::ReadArgumentLong(int argumentIndex)
{
    void *addr = GetArgumentAddr(argumentIndex);
    return ptrace(PTRACE_PEEKUSER, m_traceePid, addr, NULL);
}

// Handlers for each syscall
void PTraceSandbox::HandleSysCallGeneric(int syscallNumber)
{
    switch (syscallNumber)
    {
        CHECK_AND_CALL_HANDLER(fork);
        CHECK_AND_CALL_HANDLER(clone);
        CHECK_AND_CALL_HANDLER(execveat);
        CHECK_AND_CALL_HANDLER(execve);
        CHECK_AND_CALL_HANDLER(stat);
        CHECK_AND_CALL_HANDLER(lstat);
        CHECK_AND_CALL_HANDLER(fstat);
        CHECK_AND_CALL_HANDLER_NEW(fstatat);
        CHECK_AND_CALL_HANDLER(access);
        CHECK_AND_CALL_HANDLER(faccessat);
        CHECK_AND_CALL_HANDLER(creat);
        CHECK_AND_CALL_HANDLER(open);
        CHECK_AND_CALL_HANDLER(openat);
        CHECK_AND_CALL_HANDLER(write);
        CHECK_AND_CALL_HANDLER(writev);
        CHECK_AND_CALL_HANDLER(pwritev);
        CHECK_AND_CALL_HANDLER(pwritev2);
        CHECK_AND_CALL_HANDLER(pwrite64);
        CHECK_AND_CALL_HANDLER(truncate);
        CHECK_AND_CALL_HANDLER(ftruncate);
        CHECK_AND_CALL_HANDLER(rmdir);
        CHECK_AND_CALL_HANDLER(rename);
        CHECK_AND_CALL_HANDLER(renameat);
        CHECK_AND_CALL_HANDLER(link);
        CHECK_AND_CALL_HANDLER(linkat);
        CHECK_AND_CALL_HANDLER(unlink);
        CHECK_AND_CALL_HANDLER(unlinkat);
        CHECK_AND_CALL_HANDLER(symlink);
        CHECK_AND_CALL_HANDLER(symlinkat);
        CHECK_AND_CALL_HANDLER(readlink);
        CHECK_AND_CALL_HANDLER(readlinkat);
        CHECK_AND_CALL_HANDLER(utime);
        CHECK_AND_CALL_HANDLER(utimes);
        CHECK_AND_CALL_HANDLER(utimensat);
        CHECK_AND_CALL_HANDLER(futimesat);
        CHECK_AND_CALL_HANDLER(mkdir);
        CHECK_AND_CALL_HANDLER(mkdirat);
        CHECK_AND_CALL_HANDLER(mknod);
        CHECK_AND_CALL_HANDLER(mknodat);
        CHECK_AND_CALL_HANDLER(chmod);
        CHECK_AND_CALL_HANDLER(fchmod);
        CHECK_AND_CALL_HANDLER(fchmodat);
        CHECK_AND_CALL_HANDLER(chown);
        CHECK_AND_CALL_HANDLER(fchown);
        CHECK_AND_CALL_HANDLER(lchown);
        CHECK_AND_CALL_HANDLER(fchownat);
        CHECK_AND_CALL_HANDLER(sendfile);
        CHECK_AND_CALL_HANDLER(copy_file_range);
        CHECK_AND_CALL_HANDLER(name_to_handle_at);
        CHECK_AND_CALL_HANDLER(exit);
        default:
            // This should not happen in theory with filtering enabled
            // However if it does occur, we can ignore this syscall and log a message for debugging if necessary
            BXL_LOG_DEBUG(m_bxl, "Unsupported syscall caught by ptrace '%d'", syscallNumber);
            break;
    }
}

void PTraceSandbox::ReportOpen(std::string path, int oflag, std::string syscallName)
{
    int status = 0;
    mode_t pathMode = m_bxl->get_mode(path.c_str());
    bool pathExists = pathMode != 0;
    bool isCreate = !pathExists && (oflag & (O_CREAT|O_TRUNC));
    bool isWrite = pathExists && (oflag & (O_CREAT|O_TRUNC) && (oflag & O_WRONLY));
    IOEvent event(
        isCreate ? ES_EVENT_TYPE_NOTIFY_CREATE : isWrite ? ES_EVENT_TYPE_NOTIFY_WRITE : ES_EVENT_TYPE_NOTIFY_OPEN,
        ES_ACTION_TYPE_NOTIFY,
        path, m_bxl->GetProgramPath(), pathMode, false);

    m_bxl->report_access(syscallName.c_str(), event);
}

void PTraceSandbox::ReportCreate(std::string syscallName, int dirfd, const char *pathname, mode_t mode)
{
    IOEvent event(ES_EVENT_TYPE_NOTIFY_CREATE, ES_ACTION_TYPE_NOTIFY, m_bxl->normalize_path_at(dirfd, pathname, /*oflags*/0, m_pidStr.c_str()), m_bxl->GetProgramPath(), mode);
    m_bxl->report_access(syscallName.c_str(), event);
}

// Syscall Handlers
// TODO: [pgunasekara] combine these handlers with the ones in detours.cpp
void PTraceSandbox::HandleChildProcess(const char *syscall)
{
    int status = 0;

    ptrace(PTRACE_SYSCALL, m_traceePid, NULL, NULL);
    waitpid(m_traceePid, &status, 0);

    if (WSTOPSIG(status) == SIGTRAP)
    {
        long childPid = ReadArgumentLong(0);
        string exePath(m_bxl->GetProgramPath());
        IOEvent event(m_traceePid, childPid, getpid(), ES_EVENT_TYPE_NOTIFY_FORK, ES_ACTION_TYPE_NOTIFY, exePath, std::string(""), exePath, 0, false);
        
        m_bxl->report_access(syscall, event);
    }
    else
    {
        BXL_LOG_DEBUG(m_bxl, "Unable to get result of fork call to report child PID for parent process '%d'", m_traceePid);
    }
}

HANDLER_FUNCTION(fork)
{
    HandleChildProcess(SYSCALL_NAME_STRING(fork));
}

HANDLER_FUNCTION(clone)
{
    HandleChildProcess(SYSCALL_NAME_STRING(clone));
}

HANDLER_FUNCTION(execveat)
{
    // TODO: Is this syscall obsolete?
    int dirfd = ReadArgumentLong(1);
    std::string pathname = ReadArgumentString(2, /*nullTerminated*/ true);
    int flags = ReadArgumentLong(5);

    int oflags = (flags & AT_SYMLINK_NOFOLLOW) ? O_NOFOLLOW : 0;
    std::string exePath = m_bxl->normalize_path_at(dirfd, pathname.c_str(), oflags, m_pidStr.c_str());
    char mutableExePath[exePath.length() + 1];

    strcpy(mutableExePath, exePath.c_str());

    m_bxl->report_exec(SYSCALL_NAME_STRING(execve), basename(mutableExePath), exePath.c_str());
}

HANDLER_FUNCTION(execve)
{
    std::string file = ReadArgumentString(1, /*nullTerminated*/ true);
    char mutableFilePath[file.length() + 1];

    strcpy(mutableFilePath, file.c_str());

    m_bxl->report_exec(SYSCALL_NAME_STRING(execve), basename(mutableFilePath), file.c_str());
}

HANDLER_FUNCTION(stat)
{
    auto pathname = ReadArgumentString(1, /*nullTerminated*/ true);
    m_bxl->report_access(SYSCALL_NAME_STRING(stat), ES_EVENT_TYPE_NOTIFY_STAT, pathname.c_str(), /*mode*/ 0, O_NOFOLLOW);
}

HANDLER_FUNCTION(lstat)
{
    auto pathname = ReadArgumentString(1, /*nullTerminated*/ true);
    m_bxl->report_access(SYSCALL_NAME_STRING(lstat), ES_EVENT_TYPE_NOTIFY_STAT, pathname.c_str(), /*mode*/ 0, O_NOFOLLOW);
}

HANDLER_FUNCTION(fstat)
{
    auto fd = ReadArgumentLong(1);
    HandleReportAccessFd(SYSCALL_NAME_STRING(fstat), fd, ES_EVENT_TYPE_NOTIFY_STAT);
}

// NOTE: This stat function is not interposed by the Linux sandbox normally
// However, when calling stat, the final call to the kernel may be this one rather than stat which is why we intercept this
HANDLER_FUNCTION_NEW(fstatat)
{
    auto dirfd = ReadArgumentLong(1);
    auto pathname = ReadArgumentString(2, /*nullTerminated*/ true);
    auto flags = ReadArgumentLong(4);

    m_bxl->report_access_at(SYSCALL_NAME_STRING(fstatat), ES_EVENT_TYPE_NOTIFY_STAT, dirfd, pathname.c_str(), flags, /*getModeWithFd*/ false, m_pidStr.c_str());
}

HANDLER_FUNCTION(access)
{
    auto pathname = ReadArgumentString(1, /*nullTerminated*/ true);
    m_bxl->report_access(SYSCALL_NAME_STRING(access), ES_EVENT_TYPE_NOTIFY_ACCESS, pathname.c_str());
}

HANDLER_FUNCTION(faccessat)
{
    auto dirfd = ReadArgumentLong(1);
    auto pathname = ReadArgumentString(2, /*nullTerminated*/ true);
    m_bxl->report_access_at(SYSCALL_NAME_STRING(faccessat), ES_EVENT_TYPE_NOTIFY_ACCESS, dirfd, pathname.c_str(), /*oflags*/ 0, /*getModeWithFd*/ false, m_pidStr.c_str());
}

HANDLER_FUNCTION(creat)
{
    auto path = m_bxl->normalize_path(ReadArgumentString(1, /*nullTerminated*/ true).c_str(), /*oflags*/0, m_pidStr.c_str());
    auto oflag = O_CREAT | O_WRONLY | O_TRUNC;
    ReportOpen(path, oflag, SYSCALL_NAME_STRING(creat));

}

HANDLER_FUNCTION(open)
{
    auto path = m_bxl->normalize_path(ReadArgumentString(1, /*nullTerminated*/ true).c_str(), /*oflags*/0, m_pidStr.c_str());
    auto oflag = ReadArgumentLong(2);
    ReportOpen(path, oflag, SYSCALL_NAME_STRING(open));
}

HANDLER_FUNCTION(openat)
{
    auto dirfd = ReadArgumentLong(1);
    auto path = m_bxl->normalize_path_at(dirfd, ReadArgumentString(2, /*nullTerminated*/ true).c_str(), /*oflags*/0, m_pidStr.c_str());
    auto flags = ReadArgumentLong(3);
    ReportOpen(path, flags, SYSCALL_NAME_STRING(openat));
}

void PTraceSandbox::HandleReportAccessFd(const char *syscall, int fd, es_event_type_t event /*ES_EVENT_TYPE_NOTIFY_WRITE*/)
{
    auto path = m_bxl->fd_to_path(fd, m_pidStr.c_str());

    // Readlink returns type:[inode] if the path is not a file (files will return absolute paths)
    if (path[0] == '/')
    {
        m_bxl->report_access(syscall, event, path, m_emptyStr, /*mode*/0);
    }
}

HANDLER_FUNCTION(write)
{
    auto fd = ReadArgumentLong(1);
    HandleReportAccessFd(SYSCALL_NAME_STRING(write), fd);
}

HANDLER_FUNCTION(writev)
{
    auto fd = ReadArgumentLong(1);
    HandleReportAccessFd(SYSCALL_NAME_STRING(writev), fd);
}

HANDLER_FUNCTION(pwritev)
{
    auto fd = ReadArgumentLong(1);
    HandleReportAccessFd(SYSCALL_NAME_STRING(pwritev), fd);
}

HANDLER_FUNCTION(pwritev2)
{
    auto fd = ReadArgumentLong(1);
    HandleReportAccessFd(SYSCALL_NAME_STRING(pwritev2), fd);
}

HANDLER_FUNCTION(pwrite64)
{
    auto fd = ReadArgumentLong(1);
    HandleReportAccessFd(SYSCALL_NAME_STRING(pwrite64), fd);
}

HANDLER_FUNCTION(truncate)
{
    auto path = ReadArgumentString(1, /*nullTerminated*/ true);
    m_bxl->report_access(SYSCALL_NAME_STRING(truncate), ES_EVENT_TYPE_NOTIFY_WRITE, path.c_str());
}

HANDLER_FUNCTION(ftruncate)
{
    auto fd = ReadArgumentLong(1);
    HandleReportAccessFd(SYSCALL_NAME_STRING(ftruncate), fd);
}

HANDLER_FUNCTION(rmdir)
{
    auto path = ReadArgumentString(1, /*nullTerminated*/ true);
    m_bxl->report_access(SYSCALL_NAME_STRING(rmdir), ES_EVENT_TYPE_NOTIFY_UNLINK, path.c_str());
}

HANDLER_FUNCTION(rename)
{
    auto oldpath = ReadArgumentString(1, /*nullTerminated*/ true);
    auto newpath = ReadArgumentString(2, /*nullTerminated*/ true);

    HandleRenameGeneric(SYSCALL_NAME_STRING(rename), AT_FDCWD, oldpath.c_str(), AT_FDCWD, newpath.c_str());
}

HANDLER_FUNCTION(renameat)
{
    auto olddirfd = ReadArgumentLong(1);
    auto oldpath = ReadArgumentString(2, /*nullTerminated*/ true);
    auto newdirfd = ReadArgumentLong(3);
    auto newpath = ReadArgumentString(4, /*nullTerminated*/ true);
    
    HandleRenameGeneric(SYSCALL_NAME_STRING(renameat), olddirfd, oldpath.c_str(), newdirfd, newpath.c_str());
}

void PTraceSandbox::HandleRenameGeneric(const char *syscall, int olddirfd, const char *oldpath, int newdirfd, const char *newpath)
{
    string oldStr = m_bxl->normalize_path_at(olddirfd, oldpath, O_NOFOLLOW, m_pidStr.c_str());
    string newStr = m_bxl->normalize_path_at(newdirfd, newpath, O_NOFOLLOW, m_pidStr.c_str());

    mode_t mode = m_bxl->get_mode(oldStr.c_str());    
    std::vector<std::string> filesAndDirectories;
    
    if (S_ISDIR(mode))
    {
        bool enumerateResult = m_bxl->EnumerateDirectory(oldStr, /*recursive*/ true, filesAndDirectories);
        if (enumerateResult)
        {
            for (auto fileOrDirectory : filesAndDirectories)
            {
                // Source
                auto mode = m_bxl->get_mode(fileOrDirectory.c_str());
                m_bxl->report_access(syscall, ES_EVENT_TYPE_NOTIFY_UNLINK, fileOrDirectory.c_str(), mode, O_NOFOLLOW);

                // Destination
                fileOrDirectory.replace(0, oldStr.length(), newStr);
                ReportOpen(fileOrDirectory, O_CREAT, std::string(syscall));
            }
        }
    }
    else
    {
        auto mode = m_bxl->get_mode(oldStr.c_str());
        // Source
        m_bxl->report_access(syscall, ES_EVENT_TYPE_NOTIFY_UNLINK, oldStr.c_str(), mode, O_NOFOLLOW);

        // Destination
        ReportOpen(newStr, O_CREAT, std::string(syscall));
    }
}

HANDLER_FUNCTION(link)
{
    auto oldpath = ReadArgumentString(1, /*nullTerminated*/ true);
    auto newpath = ReadArgumentString(2, /*nullTerminated*/ true);

    m_bxl->report_access(
        SYSCALL_NAME_STRING(link),
        ES_EVENT_TYPE_NOTIFY_LINK,
        m_bxl->normalize_path(oldpath.c_str(), O_NOFOLLOW, m_pidStr.c_str()),
        m_bxl->normalize_path(newpath.c_str(), O_NOFOLLOW, m_pidStr.c_str()));
}

HANDLER_FUNCTION(linkat)
{
    auto olddirfd = ReadArgumentLong(1);
    auto oldpath = ReadArgumentString(2, /*nullTerminated*/ true);
    auto newdirfd = ReadArgumentLong(3);
    auto newpath = ReadArgumentString(4, /*nullTerminated*/ true);

    m_bxl->report_access(
        SYSCALL_NAME_STRING(linkat),
        ES_EVENT_TYPE_NOTIFY_LINK,
        m_bxl->normalize_path_at(olddirfd, oldpath.c_str(), O_NOFOLLOW, m_pidStr.c_str()),
        m_bxl->normalize_path_at(newdirfd, newpath.c_str(), O_NOFOLLOW, m_pidStr.c_str()));
}

HANDLER_FUNCTION(unlink)
{
    auto path = ReadArgumentString(1, /*nullTerminated*/ true);

    if (path[0] != '\0')
    {
        m_bxl->report_access(SYSCALL_NAME_STRING(unlink), ES_EVENT_TYPE_NOTIFY_UNLINK, path.c_str(), /*mode*/ 0, O_NOFOLLOW);
    }
}

HANDLER_FUNCTION(unlinkat)
{
    auto dirfd = ReadArgumentLong(1);
    auto path = ReadArgumentString(2, /*nullTerminated*/ true);
    auto flags = ReadArgumentLong(3);

    if (dirfd != AT_FDCWD && path[0] != '\0')
    {
        int oflags = (flags & AT_REMOVEDIR) ? 0 : O_NOFOLLOW;
        m_bxl->report_access_at(SYSCALL_NAME_STRING(unlinkat), ES_EVENT_TYPE_NOTIFY_UNLINK, dirfd, path.c_str(), oflags, /*getModeWithFd*/ false, m_pidStr.c_str());
    }
}

HANDLER_FUNCTION(symlink)
{
    auto linkPath = ReadArgumentString(2, /*nullTerminated*/ true);

    IOEvent event(ES_EVENT_TYPE_NOTIFY_CREATE, ES_ACTION_TYPE_NOTIFY, m_bxl->normalize_path(linkPath.c_str(), O_NOFOLLOW, m_pidStr.c_str()), m_bxl->GetProgramPath(), S_IFLNK);
    m_bxl->report_access(SYSCALL_NAME_STRING(symlink), event);
}

HANDLER_FUNCTION(symlinkat)
{
    auto dirfd = ReadArgumentLong(2);
    auto linkPath = ReadArgumentString(3, /*nullTerminated*/ true);

    IOEvent event(ES_EVENT_TYPE_NOTIFY_CREATE, ES_ACTION_TYPE_NOTIFY, m_bxl->normalize_path_at(dirfd, linkPath.c_str(), O_NOFOLLOW, m_pidStr.c_str()), m_bxl->GetProgramPath(), S_IFLNK);
    m_bxl->report_access(SYSCALL_NAME_STRING(symlinkat), event);

}

HANDLER_FUNCTION(readlink)
{
    auto path = ReadArgumentString(1, /*nullTerminated*/ true);

    m_bxl->report_access(SYSCALL_NAME_STRING(readlink), ES_EVENT_TYPE_NOTIFY_READLINK, path.c_str(), /*mode*/ 0, O_NOFOLLOW);
}

HANDLER_FUNCTION(readlinkat)
{
    auto fd = ReadArgumentLong(1);
    auto path = ReadArgumentString(2, /*nullTerminated*/ true);

    m_bxl->report_access_at(SYSCALL_NAME_STRING(readlinkat), ES_EVENT_TYPE_NOTIFY_READLINK, fd, path.c_str(), O_NOFOLLOW, /*getModeWithFd*/ false, m_pidStr.c_str());
}

HANDLER_FUNCTION(utime)
{
    auto filename = ReadArgumentString(1, /*nullTerminated*/ true);

    m_bxl->report_access(SYSCALL_NAME_STRING(utime), ES_EVENT_TYPE_NOTIFY_SETTIME, filename.c_str());
}

HANDLER_FUNCTION(utimes)
{
    Handleutime();
}

HANDLER_FUNCTION(utimensat)
{
    auto dirfd = ReadArgumentLong(1);
    auto pathname = ReadArgumentString(2, /*nullTerminated*/ true);

    m_bxl->report_access_at(SYSCALL_NAME_STRING(utimensat), ES_EVENT_TYPE_NOTIFY_SETTIME, dirfd, pathname.c_str(), /*oflags*/ 0, /*getModeWithFd*/ false, m_pidStr.c_str());
}

HANDLER_FUNCTION(futimesat)
{
    auto dirfd = ReadArgumentLong(1);
    auto pathname = ReadArgumentString(2, /*nullTerminated*/ true);

    m_bxl->report_access_at(SYSCALL_NAME_STRING(futimesat), ES_EVENT_TYPE_NOTIFY_SETTIME, dirfd, pathname.c_str(), /*oflags*/ 0, /*getModeWithFd*/ false, m_pidStr.c_str());
}

HANDLER_FUNCTION(mkdir)
{
    auto path = ReadArgumentString(1, /*nullTerminated*/ true);

    ReportCreate(SYSCALL_NAME_STRING(mkdir), AT_FDCWD, path.c_str(), S_IFDIR);
}

HANDLER_FUNCTION(mkdirat)
{
    auto dirfd = ReadArgumentLong(1);
    auto path = ReadArgumentString(2, /*nullTerminated*/ true);

    ReportCreate(SYSCALL_NAME_STRING(mkdirat), dirfd, path.c_str(), S_IFDIR);
}

HANDLER_FUNCTION(mknod)
{
    auto path = ReadArgumentString(1, /*nullTerminated*/ true);

    ReportCreate(SYSCALL_NAME_STRING(mknod), AT_FDCWD, path.c_str(), S_IFREG);
}

HANDLER_FUNCTION(mknodat)
{
    auto dirfd = ReadArgumentLong(1);
    auto path = ReadArgumentString(2, /*nullTerminated*/ true);

    ReportCreate(SYSCALL_NAME_STRING(mknodat), dirfd, path.c_str(), S_IFREG);

}

HANDLER_FUNCTION(chmod)
{
    auto path = ReadArgumentString(1, /*nullTerminated*/ true);

    m_bxl->report_access(SYSCALL_NAME_STRING(chmod), ES_EVENT_TYPE_NOTIFY_SETMODE, path.c_str());
}

HANDLER_FUNCTION(fchmod)
{
    auto fd = ReadArgumentLong(1);
    HandleReportAccessFd(SYSCALL_NAME_STRING(fchmod), fd, ES_EVENT_TYPE_NOTIFY_SETMODE);
}

HANDLER_FUNCTION(fchmodat)
{
    auto dirfd = ReadArgumentLong(1);
    auto pathname = ReadArgumentString(2, /*nullTerminated*/ true);
    auto flags = ReadArgumentLong(4);

    int oflags = (flags & AT_SYMLINK_NOFOLLOW) ? O_NOFOLLOW : 0;
    m_bxl->report_access_at(SYSCALL_NAME_STRING(fchmodat), ES_EVENT_TYPE_NOTIFY_SETMODE, dirfd, pathname.c_str(), oflags, /*getModeWithFd*/ false, m_pidStr.c_str());
}

HANDLER_FUNCTION(chown)
{
    auto pathname = ReadArgumentString(1, /*nullTerminated*/ true);
    m_bxl->report_access(SYSCALL_NAME_STRING(chown), ES_EVENT_TYPE_AUTH_SETOWNER, pathname.c_str());
}

HANDLER_FUNCTION(fchown)
{
    auto fd = ReadArgumentLong(1);
    HandleReportAccessFd(SYSCALL_NAME_STRING(fchown), fd, ES_EVENT_TYPE_AUTH_SETOWNER);
}

HANDLER_FUNCTION(lchown)
{
    auto pathname = ReadArgumentString(1, /*nullTerminated*/ true);
    m_bxl->report_access(SYSCALL_NAME_STRING(lchown), ES_EVENT_TYPE_AUTH_SETOWNER, pathname.c_str(), /*mode*/ 0, O_NOFOLLOW);
}

HANDLER_FUNCTION(fchownat)
{
    auto dirfd = ReadArgumentLong(1);
    auto pathname = ReadArgumentString(2, /*nullTerminated*/ true);
    auto flags = ReadArgumentLong(5);

    int oflags = (flags & AT_SYMLINK_NOFOLLOW) ? O_NOFOLLOW : 0;
    auto check = m_bxl->report_access_at(SYSCALL_NAME_STRING(fchownat), ES_EVENT_TYPE_AUTH_SETOWNER, dirfd, pathname.c_str(), oflags, /*getModeWithFd*/ false, m_pidStr.c_str());
}

HANDLER_FUNCTION(sendfile)
{
    auto out_fd = ReadArgumentLong(1);
    HandleReportAccessFd(SYSCALL_NAME_STRING(sendfile), out_fd);
}

HANDLER_FUNCTION(copy_file_range)
{
    auto fd_out = ReadArgumentLong(3);
    HandleReportAccessFd(SYSCALL_NAME_STRING(copy_file_range), fd_out);
}

HANDLER_FUNCTION(name_to_handle_at)
{
    auto dirfd = ReadArgumentLong(1);
    auto pathname = ReadArgumentString(2, /*nullTerminated*/ true);
    auto flags = ReadArgumentLong(5);

    int oflags = (flags & AT_SYMLINK_FOLLOW) ? 0 : O_NOFOLLOW;
    string pathStr = m_bxl->normalize_path_at(dirfd, pathname.c_str(), oflags, m_pidStr.c_str());
    ReportOpen(pathStr, oflags, SYSCALL_NAME_STRING(name_to_handle_at));
}

HANDLER_FUNCTION(exit)
{
    m_bxl->report_access("on_exit", ES_EVENT_TYPE_NOTIFY_EXIT, std::string(""), std::string(""));
}
