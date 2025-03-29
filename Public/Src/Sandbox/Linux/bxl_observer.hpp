// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#ifndef BUILDXL_SANDBOX_LINUX_BXL_OBSERVER_H
#define BUILDXL_SANDBOX_LINUX_BXL_OBSERVER_H

// Linux headers
#include "dirent.h"
#include <sched.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <dlfcn.h>
#include <fcntl.h>
#include <unistd.h>
#include <limits.h>
#include <semaphore.h>
#include <stddef.h>
#include <sys/sendfile.h>
#include <sys/stat.h>
#include <sys/syscall.h>
#include <sys/time.h>
#include <sys/types.h>
#include <sys/uio.h>
#include <sys/vfs.h>
#include <utime.h>

// C++ headers
#include <ostream>
#include <sstream>
#include <chrono>
#include <mutex>
#include <unordered_set>
#include <unordered_map>
#include <vector>

// Project Headers
#include "common.h"
#include "FileAccessManifest.h"
#include "ReportBuilder.h"
#include "SandboxEvent.h"
#include "utils.h"

using namespace std;

extern const char *__progname;

static const char LD_PRELOAD_ENV_VAR_PREFIX[] = "LD_PRELOAD=";

static const char GLIBC_23[] = "GLIBC_2.3";

#define ARRAYSIZE(arr) (sizeof(arr)/sizeof(arr[0]))

/*
*  real_{fn} provides a way to call the real libc function fn that we interpose.
*  Note that calling these functions might modify the global variable errno
*  in a meaningful way, so we should proceed with caution when using them
*  as part of the operations of the sandbox in the middle of an interposing,
*  because the callers of interposed functions might interpret the value of errno
*  after the call we are interposing returns.
*  To call these functions and automatically preserve errno, use the internal_{fn} variants.
*/
#define GEN_FN_DEF_REAL(ret, name, ...)                                         \
    typedef ret (*fn_real_##name)(__VA_ARGS__);                                 \
    const fn_real_##name real_##name = (fn_real_##name)dlsym(RTLD_NEXT, #name);

#define GEN_FN_DEF_INTERNAL(ret, name, ...)                                     \
    template<typename ...TArgs> ret internal_##name(TArgs&& ...args)            \
    {                                                                           \
        int prevErrno = errno;                                                  \
        ret result = real_##name(std::forward<TArgs>(args)...);                 \
        errno = prevErrno;                                                      \
        return result;                                                          \
    }

/*
 * When interposing versioned symbols from glibc, dlsym will always pick up the oldest version by default.
 * This is done for compatibility, but can cause issues if a binary is expecting a newer version of the function.
 * To get around this, GEN_FN_DEF_REAL_VERSIONED can be used with the glibc version in the format GLIBC_<major>.<minor>.
 * An exact version must be passed as an argument here or else dlvsym will return NULL (this means the latest version cannot be passed all the time).
 * 
 * To check what version of a libc function a binary is using, dump it with the following command: objdump -t </path/to/binary> | grep <function_name>
 */
#define GEN_FN_DEF_REAL_VERSIONED(version, ret, name, ...)                      \
    typedef ret (*fn_real_##name)(__VA_ARGS__);                                 \
    const fn_real_##name real_##name = (fn_real_##name)dlvsym(RTLD_NEXT, #name, version);

#define MAKE_BODY(B) \
    B \
}

// It's important to have an option to bail out early, *before*
// the call to BxlObserver::GetInstance() because we might not
// have the process initialized far enough for that call to succeed.
#define INTERPOSE_SOMETIMES(ret, name, short_circuit_check, ...) \
    DLL_EXPORT ret name(__VA_ARGS__) {                           \
        short_circuit_check                                      \
        BxlObserver *bxl = BxlObserver::GetInstance();           \
        BXL_LOG_DEBUG(bxl, "Intercepted %s", #name);             \
        MAKE_BODY

#define INTERPOSE(ret, name, ...) \
    INTERPOSE_SOMETIMES(ret, name, ;, __VA_ARGS__)


// Linux libraries are required to set errno only when the operation fails. 
// In most cases, when the operation succeeds errno is set to a random value
// (or it does not get updated at all). Therefore, only report errno when   
// the operation fails, and otherwise return 0. This allows the managed     
// side of BuildXL to interpret when an operation succeeds or fails, and    
// to retrieve the details in case of the failure.
#define GEN_FN_DEF(ret, name, ...)                                              \
    GEN_FN_DEF_REAL(ret, name, __VA_ARGS__)                                     \
    GEN_FN_DEF_INTERNAL(ret, name, __VA_ARGS__)                                 \
    GEN_FN_FWD(ret, name, __VA_ARGS__)

#define GEN_FN_DEF_VERSIONED(version, ret, name, ...)                           \
    GEN_FN_DEF_REAL_VERSIONED(version, ret, name, __VA_ARGS__)                  \
    GEN_FN_DEF_INTERNAL(ret, name, __VA_ARGS__)                                 \
    GEN_FN_FWD(ret, name, __VA_ARGS__)

#define GEN_FN_FWD(ret, name, ...)                                              \
    template<typename ...TArgs> result_t<ret> fwd_##name(TArgs&& ...args)       \
    {                                                                           \
        ret result = real_##name(std::forward<TArgs>(args)...);                 \
        result_t<ret> return_value(result);                                     \
        LOG_DEBUG("Forwarded syscall %s (errno: %d)",                           \
            RenderSyscall(#name, result, std::forward<TArgs>(args)...).c_str(), \
            return_value.get_errno());                                          \
        return return_value;                                                    \
    }                                                                           \
    template<typename ...TArgs> result_t<ret> fwd_no_log_##name(TArgs&& ...args)\
    {                                                                           \
        ret result = real_##name(std::forward<TArgs>(args)...);                 \
        result_t<ret> return_value(result);                                     \
        return return_value;                                                    \
    }                                                                           \
    template<typename ...TArgs> ret check_fwd_and_report_##name(                \
        buildxl::linux::SandboxEvent& event,                                    \
        ret error_val,                                                          \
        TArgs&& ...args)                                                        \
    {                                                                           \
        if (!event.IsValid())                                                   \
        {                                                                       \
            return fwd_##name(args...).restore();                               \
        }                                                                       \
        AccessCheckResult check_result = event.GetEventAccessCheckResult();     \
        result_t<ret> return_value = should_deny(check_result)                  \
            ? result_t<ret>(error_val, EPERM)                                   \
            : event.IsLoggingDisabled()                                         \
                ? fwd_no_log_##name(args...)                                    \
                : fwd_##name(args...);                                          \
        event.SetErrno(return_value.get() == error_val                          \
            ? return_value.get_errno()                                          \
            : 0);                                                               \
        BxlObserver::GetInstance()->SendReport(event);                          \
        return return_value.restore();                                          \
    }                                                                           \
    template<typename ...TArgs> result_t<ret> fwd_and_report_##name(            \
        buildxl::linux::SandboxEvent& event,                                    \
        ret error_val,                                                          \
        TArgs&& ...args)                                                        \
    {                                                                           \
        result_t<ret> return_value = fwd_##name(args...);                       \
        if (event.IsValid())                                                    \
        {                                                                       \
            event.SetErrno(return_value.get() == error_val                      \
                ? return_value.get_errno()                                      \
                : 0);                                                           \
            BxlObserver::GetInstance()->SendReport(event);                      \
        }                                                                       \
        return return_value;                                                    \
    }                                                                           \

#define _fatal(fmt, ...) do { real_fprintf(stderr, "(%s) " fmt "\n", __func__, __VA_ARGS__); _exit(1); } while (0)
#define fatal(msg) _fatal("%s", msg)

#define _fatal_undefined_env(name)                                                                      \
    char** procenv = environ;                                                                           \
    std::stringstream ss;                                                                               \
    for (int i = 0; procenv[i] != NULL; i++) {                                                          \
        ss << procenv[i];                                                                               \
        if (procenv[i+1] != NULL) {                                                                     \
            ss << ",";                                                                                  \
        }                                                                                               \
    }                                                                                                   \
    _fatal("[%s] ERROR: Env var '%s' not set. Environment: [%s]\n", __func__, name, ss.str().c_str());  \

/**
 * Wraps the result of a syscall together with the current 'errno'.
 *
 * When 'restore' is called, if allowed, 'errno' is reset back to
 * the value that was captured in the constructor and the captured result is returned;
 * otherwise 'errno' is set to EPERM and the error value is returned.
 */
template <typename T>
class result_t final
{
private:
    int my_errno_;
    T result_;

public:
    result_t(T result) : result_(result), my_errno_(errno) {}
    result_t(T result, int error) : result_(result), my_errno_(error) {}

    /** Returns the remembered result and restores 'errno' to the value captured in the constructor. */
    inline T restore()
    {
        errno = my_errno_;
        return result_;
    }

    /** Returns the remembered result. */
    inline T get()
    {
        return result_;
    }

    /** Returns the remembered errno. */
    inline int get_errno()
    {
        return my_errno_;
    }
};

/**
 * Singleton class responsible for reporting accesses.
 *
 * Accesses are observed by intercepting syscalls.
 *
 * Accesses are reported to a file (can be a regular file or a FIFO)
 * at the location specified by the FileAccessManifest.
 */
class BxlObserver final
{
private:
    BxlObserver();
    ~BxlObserver();
    BxlObserver(const BxlObserver&) = delete;
    BxlObserver& operator = (const BxlObserver&) = delete;

    volatile int disposed_;
    int rootPid_;
    char progFullPath_[PATH_MAX];
    char detoursLibFullPath_[PATH_MAX];
    char famPath_[PATH_MAX];
    char forcedPTraceProcessNamesList_[PATH_MAX];
    char secondaryReportPath_[PATH_MAX];

    std::timed_mutex cacheMtx_;
    std::unordered_map<buildxl::linux::EventType, std::unordered_set<std::string>> cache_;

    // In a typical case, a process will not have more than 1024 open file descriptors at a time.
    // File descriptors start at 3 (1 and 2 are reserved for stdout and stderr).
    // Whenever a new file descriptor is created, the smallest available positive integer is assigned to it. 
    // Whenever a file descriptor is closed, its value is returned to the pool and will be used for new ones.
    // Setting the size of this table to 1024 should accommodate most of the common cases.
    // File descriptors can be greater than 1024, and if that happens we just won't cache their paths.
    static const int MAX_FD = 1024;
    std::string fdTable_[MAX_FD];
    const char* const empty_str_ = "";
    bool useFdTable_ = true;
    bool sandboxLoggingEnabled_ = false;

    /** File access manifest */
    buildxl::common::FileAccessManifest* fam_;

    // Cache for processes requiring ptrace in the form <timestamp>:<path>
    std::vector<std::pair<std::string, bool>> ptraceRequiredProcessCache_;
    std::vector<std::string> forcedPTraceProcessNames_;

    // Message counting
    sem_t *messageCountingSemaphore_ = nullptr;
    bool initializingSemaphore_ = false;

    bool bxlObserverInitialized_ = false;

    void InitFam(pid_t pid);
    void InitDetoursLibPath();
    bool Send(const char *buf, size_t bufsiz, bool useSecondaryPipe, bool countReport);
    bool IsCacheHit(buildxl::linux::EventType event, const string &path, const string &secondPath);
    bool CheckCache(buildxl::linux::EventType event, const string &path, bool addEntryIfMissing);
    char** ensure_env_value_with_log(char *const envp[], char const *envName, const char *envValue);
    ssize_t read_path_for_fd(int fd, char *buf, size_t bufsiz, pid_t associatedPid = 0);

    bool IsMonitoringChildProcesses() const { return !fam_ || CheckMonitorChildProcesses(fam_->GetFlags()); }
    bool IsPTraceEnabled() const { return fam_ && (CheckEnableLinuxPTraceSandbox(fam_->GetExtraFlags()) || CheckUnconditionallyEnableLinuxPTraceSandbox(fam_->GetExtraFlags())); }
    bool IsPTraceForced(const char *path);

    inline bool IsValid() const             { return fam_ != NULL; }

    void PrintArgs(std::stringstream& str, bool isFirst)
    {
    }

    template<typename TFirst, typename ...TRest>
    void PrintArgs(std::stringstream& str, bool isFirst, TFirst first, const TRest& ...rest)
    {
        if (!isFirst) str << ", ";
        str << first;
        PrintArgs(str, false, rest...);
    }

    template<typename TRet, typename ...TArgs>
    std::string RenderSyscall(const char *syscallName, const TRet& retVal, const TArgs& ...args)
    {
        std::stringstream str;
        str << syscallName << "(";
        PrintArgs(str, true, args...);
        str << ") = " << retVal;
        return str.str();
    }

    void relative_to_absolute(const char *pathname, int dirfd, int associatedPid, char *fullPath, const char *systemcall = "");
    void resolve_path(char *fullpath, bool followFinalSymlink, pid_t associatedPid, pid_t associatedParentPid);
    
    /**
     * If possible, resolves relative or file descriptor paths to absolute paths in a SandboxEvent, and returns true. 
     * This might not be possible if the event is a file-descriptor specified event and the file descriptors
     * do not correspond to real paths. In this case, the function returns false and the event is unmodified.
     * In both cases, the mode of the source path of the event is updated.
     */
    bool ResolveEventPaths(buildxl::linux::SandboxEvent& event);

    /**
     * Resolves the paths in a SandboxEvent if they are not already by following any symlinks if specified on the resolution requirements
     * in the SandboxEvent, and expanding references to /./, /../, and //
     */
    void ResolveEventPaths(buildxl::linux::SandboxEvent& event, char *src_path, char *dst_path);

    /**
     * Converts a file descriptor associated with the provided PID to a path.
     */
    void FileDescriptorToPath(int fd, pid_t pid, char *out_path_buffer, size_t buffer_size);

    static BxlObserver *sInstance;
    static AccessCheckResult sNotChecked;

#define BXL_LOG_DEBUG(bxl, fmt, ...) if (bxl->LogDebugEnabled()) { pid_t pid = getpid(); bxl->LogDebug(pid, "[%s:%d] " fmt, __progname, pid, __VA_ARGS__); }

#define LOG_DEBUG(fmt, ...) BXL_LOG_DEBUG(this, fmt, __VA_ARGS__)

public:
    static BxlObserver* GetInstance();

    // Performs additional initialization tasks after the static instance is initally constructed.
    void Init();
    bool IsPerformingInit()
    {
        return initializingSemaphore_;
    }

    bool SendReport(buildxl::linux::SandboxEvent &event, bool use_secondary_pipe = false);
    bool SendReport(buildxl::linux::SandboxEvent &event, buildxl::linux::AccessReport report, bool use_secondary_pipe);

    // Specialization for the exit report event. 
    // We may need to send an exit report on exit handlers after destructors
    // have been called. This method avoids accessing shared structures.
    bool SendExitReport(pid_t pid, pid_t ppid);
    char** ensureEnvs(char *const envp[]);
    char** removeEnvs(char *const envp[]);

    const char* GetProgramPath() { return progFullPath_; }
    const char* GetReportsPath() { int len; return IsValid() ? fam_->GetReportsPath(&len) : NULL; }
    const char* GetSecondaryReportsPath() { return secondaryReportPath_; }
    const char* GetDetoursLibPath() { return detoursLibFullPath_; }

    buildxl::common::FileAccessManifest* GetFileAccessManifest() { return fam_;}
    const std::vector<buildxl::common::BreakawayChildProcess>& GetBreakawayChildProcesses() const { return fam_->GetBreakawayChildProcesses(); };

    bool IsReportingProcessArgs() const { return !fam_ || CheckReportProcessArgs(fam_->GetFlags()); }

    void report_intermediate_symlinks(const char *pathname, pid_t associatedPid, pid_t associatedParentPid);

    // Removes detours path from LD_PRELOAD from the given environment and returns the modified environment
    inline char** RemoveLDPreloadFromEnv(char *const envp[])
    { 
        return remove_path_from_LDPRELOAD(envp, detoursLibFullPath_);
    }

    /**
     * Reads and returns the command line for the provided process ID provided that IsReportingProcessArgs is enabled.
    */
    std::string GetProcessCommandLine(pid_t pid);

    /**
     * Reads and returns the command line for the provided process ID.
    */
    std::string DoGetProcessCommandLine(pid_t pid);

    /**
     * Converts an argument vector containing the command line into a single string.
     * If an argc is not provided, it will be calculated based on the provided argv.
    */
    std::string GetProcessCommandLine(const char * const argv[]);

    /**
     * Performs an access check on the provided SandboxEvent and produces an access report.
     * If a file descriptor or relative paths are provided in the event, this function will also resolve those to full paths.
     * @param syscall_name The name of the syscall that triggered the access check.
     * @param event The SandboxEvent to check.
     * @param report The AccessReportGroup to populate with the access report.
     * @param check_cache Whether to cache events. Events are cached based on event type and path.
     * @param basedOnlyOnPolicy Whether the access check has to happen purely based on file access policies (as opposed of existence-based)
     */
    AccessCheckResult CreateAccess(buildxl::linux::SandboxEvent& event, bool check_cache = true, bool basedOnlyOnPolicy = false);

    /**
     * Sends a file access report to the managed side of the sandbox using the provided access report. 
     */
    void ReportAccess(buildxl::linux::SandboxEvent& event);

    /**
     * Creates and sends a file access report to the managed side based on a sandbox event, ignoring the result of the access check.
     * Used for interposings that we don't need to block (like stats) or where we can't block (like the PTrace sandbox) 
     */
    void CreateAndReportAccess(buildxl::linux::SandboxEvent& event, bool check_cache = true, bool basedOnlyOnPolicy = false);

    // Send a special message to managed code if the policy to override allowed writes based on file existence is set
    // and the write is allowed by policy
    void report_firstAllowWriteCheck(const char *full_path, int path_mode = -1, int pid = -1, int ppid = -1);
    void create_firstAllowWriteCheck(const char *full_path, int path_mode, int pid, int ppid, buildxl::linux::SandboxEvent& firstAllowWriteEvent);

    // Checks and reports when a process that requires ptrace is about to be executed
    // Observe that as soon as this method determines ptrace is required and sends the corresponding report
    // ptrace runner is started and will try to seize the current process under ptrace
    bool check_and_report_process_requires_ptrace(const char *path);
    bool check_and_report_process_requires_ptrace(int fd);
    bool is_statically_linked(const char *path);
    bool contains_capabilities(const char *path);
    std::string execute_and_pipe_stdout(const char *path, const char *process, char *const args[]);
    void set_ptrace_permissions();
    // Checks against the breakaway list to see whether there is a match
    // TODO: implement breakaway handling for ptrace
    bool SendBreakawayReportIfNeeded(const char *path, std::string &args, pid_t pid = -1, pid_t ppid = -1);
    bool SendBreakawayReportIfNeeded(const char *path, char *const argv[]);
    bool SendBreakawayReportIfNeeded(int fd, char *const argv[]);
    void SendBreakawayReport(const char *path, pid_t pid, pid_t ppid);

    // Clears the specified entry on the file descriptor table
    void reset_fd_table_entry(int fd);
    
    // Clears the entire file descriptor table
    void reset_fd_table();

    // Disables the FD table. Cannot be re-enabled for the remainder of the sandbox lifetime.
    void disable_fd_table();
    
    // Returns the path associated with the given file descriptor
    // Note: This function assumes fd is a file descriptor pointing to a regular file (that is, a file, directory or symlink, not a pipe/socket/etc). The reason for this assumption is that file descriptors
    // are cached and the corresponding invalidation is tied to opening handles against file names. We are currently not detouring pipe creation, so we run the risk of not invalidating the file descriptor
    // table properly for the case of pipes when we miss a close.
    std::string fd_to_path(int fd, pid_t associatedPid = 0);
    
    std::string normalize_path_at(int dirfd, const char *pathname, pid_t associatedPid, pid_t associatedParentPid, int oflags = 0, const char *systemcall = "");

    // Whether the given descriptor is a non-file (e.g., a pipe, or socket, etc.)
    static bool is_non_file(const mode_t mode);

    // Enumerates a specified directory
    bool EnumerateDirectory(std::string rootDirectory, bool recursive, std::vector<std::string>& filesAndDirectories);

    const char* getFamPath() const { return famPath_; };

    inline bool LogDebugEnabled()
    {
        if (fam_ == NULL)
        {
            // The observer isn't initialized yet. We're being defensive here,
            // in case someone adds a LOG_DEBUG in a place where it would cause a segfault. 
            return false;
        }

        return sandboxLoggingEnabled_;
    }

    void LogDebugMessage(pid_t pid, buildxl::linux::DebugEventSeverity severity, const char *fmt, va_list args);
    void LogDebug(pid_t pid, const char *fmt, ...);
    void LogError(pid_t pid, const char *fmt, ...);
    
    mode_t get_mode(const char *path)
    {
        struct stat buf;
#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
        mode_t result = internal___lxstat(1, path, &buf) == 0
#else
        mode_t result = internal_lstat(path, &buf) == 0
#endif
            ? buf.st_mode
            : 0;
        return result;
    }

    mode_t get_mode(int fd)
    {
        struct stat buf;
#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
        mode_t result = internal___fxstat(1, fd, &buf) == 0
#else
        mode_t result = internal_fstat(fd, &buf) == 0
#endif
            ? buf.st_mode
            : 0;
        return result;
    }

    char *getcurrentworkingdirectory(char *fullpath, size_t size, pid_t associatedPid = 0)
    {
        if (associatedPid == 0)
        {
            return getcwd(fullpath, size);
        }
        else
        {
            char linkPath[100] = {0};
            sprintf(linkPath, "/proc/%d/cwd", associatedPid);
            if (internal_readlink(linkPath, fullpath, size) == -1)
            {
                return NULL;
            }
            
            return fullpath;
        }
    }

    std::string normalize_path(const char *pathname, pid_t associatedPid, pid_t associatedParentPid, int oflags = 0)
    {
        if (pathname == nullptr)
        {
            return empty_str_;
        }

        return normalize_path_at(AT_FDCWD, pathname, associatedPid, associatedParentPid, oflags);
    }

    bool IsFailingUnexpectedAccesses()
    {
        return CheckFailUnexpectedFileAccesses(fam_->GetFlags());
    }

    /**
     * Returns whether the given access should be denied.
     *
     * This is true when
     *   - the given access is not permitted
     *   - the sandbox is configured to deny accesses that are not permitted
     */
    bool should_deny(AccessCheckResult &check)
    {
        // getpid() can be used for IsEnabled() here because this function will not be called for the ptrace sandbox
        return IsValid() && check.ShouldDenyAccess() && IsFailingUnexpectedAccesses();
    }

    /**
     * NOTE: when adding new system calls to interpose here, ensure that a matching unit test for that system call
     * is added to Public/Src/Sandbox/Linux/UnitTests/TestProcesses/TestProcess/main.cpp and Public/Src/Engine/UnitTests/Processes/LinuxSandboxProcessTests.cs
     */

    GEN_FN_DEF(void*, dlopen, const char *filename, int flags);
    GEN_FN_DEF(int, dlclose, void *handle);

    GEN_FN_DEF(pid_t, fork, void);
    GEN_FN_DEF(pid_t, vfork, void);
    GEN_FN_DEF(int, clone, int (*fn)(void *), void *child_stack, int flags, void *arg, ... /* pid_t *ptid, void *newtls, pid_t *ctid */ );
    GEN_FN_DEF_REAL(void, _exit, int);
    GEN_FN_DEF(int, fexecve, int, char *const[], char *const[]);
    GEN_FN_DEF(int, execv, const char *, char *const[]);
    GEN_FN_DEF(int, execve, const char *, char *const[], char *const[]);
    GEN_FN_DEF(int, execvp, const char *, char *const[]);
    GEN_FN_DEF(int, execvpe, const char *, char *const[], char *const[]);
    GEN_FN_DEF(int, execl, const char *, const char *, ...);
    GEN_FN_DEF(int, execlp, const char *, const char *, ...);
    GEN_FN_DEF(int, execle, const char *, const char *, ...);
#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
    GEN_FN_DEF(int, __lxstat, int, const char *, struct stat *);
    GEN_FN_DEF(int, __lxstat64, int, const char*, struct stat64*);
    GEN_FN_DEF(int, __xstat, int, const char *, struct stat *);
    GEN_FN_DEF(int, __xstat64, int, const char*, struct stat64*);
    GEN_FN_DEF(int, __fxstat, int, int, struct stat*);
    GEN_FN_DEF(int, __fxstatat, int, int, const char*, struct stat*, int);
    GEN_FN_DEF(int, __fxstat64, int, int, struct stat64*);
    GEN_FN_DEF(int, __fxstatat64, int, int, const char*, struct stat64*, int);
    GEN_FN_DEF(int, __xmknod, int, const char*, mode_t, dev_t*);
    GEN_FN_DEF(int, __xmknodat, int, int, const char*, mode_t, dev_t*);
#else
    GEN_FN_DEF(int, stat, const char *, struct stat *);
    GEN_FN_DEF(int, stat64, const char *, struct stat64 *);
    GEN_FN_DEF(int, lstat, const char *, struct stat *);
    GEN_FN_DEF(int, lstat64, const char *, struct stat64 *);
    GEN_FN_DEF(int, fstat, int, struct stat *);
    GEN_FN_DEF(int, fstat64, int, struct stat64 *);
#endif
    GEN_FN_DEF(FILE*, fdopen, int, const char *);
    GEN_FN_DEF(FILE*, fopen, const char *, const char *);
    GEN_FN_DEF(FILE*, fopen64, const char *, const char *);
    GEN_FN_DEF(FILE*, freopen, const char *, const char *, FILE *);
    GEN_FN_DEF(FILE*, freopen64, const char *, const char *, FILE *);
    GEN_FN_DEF(size_t, fread, void*, size_t, size_t, FILE*);
    GEN_FN_DEF(size_t, fwrite, const void*, size_t, size_t, FILE*);
    GEN_FN_DEF(int, fputc, int c, FILE *stream);
    GEN_FN_DEF(int, fputs, const char *s, FILE *stream);
    GEN_FN_DEF(int, putc, int c, FILE *stream);
    GEN_FN_DEF(int, putchar, int c);
    GEN_FN_DEF(int, puts, const char *s);
    GEN_FN_DEF(int, access, const char *, int);
    GEN_FN_DEF(int, faccessat, int, const char *, int, int);
    GEN_FN_DEF(int, creat, const char *, mode_t);
    GEN_FN_DEF(int, open64, const char *, int, mode_t);
    GEN_FN_DEF(int, open, const char *, int, mode_t);
    GEN_FN_DEF(int, openat, int, const char *, int, mode_t);
    GEN_FN_DEF(ssize_t, write, int, const void*, size_t);
    GEN_FN_DEF(ssize_t, writev, int fd, const struct iovec *iov, int iovcnt);
    GEN_FN_DEF(ssize_t, pwritev, int fd, const struct iovec *iov, int iovcnt, off_t offset);
    GEN_FN_DEF(ssize_t, pwritev2, int fd, const struct iovec *iov, int iovcnt, off_t offset, int flags);
    GEN_FN_DEF(ssize_t, pwrite, int fd, const void *buf, size_t count, off_t offset);
    GEN_FN_DEF(ssize_t, pwrite64, int fd, const void *buf, size_t count, off_t offset);
    GEN_FN_DEF(int, remove, const char *);
    GEN_FN_DEF(int, truncate, const char *path, off_t length);
    GEN_FN_DEF(int, ftruncate, int fd, off_t length);
    GEN_FN_DEF(int, truncate64, const char *path, off_t length);
    GEN_FN_DEF(int, ftruncate64, int fd, off_t length);
    GEN_FN_DEF(int, rmdir, const char *pathname);
    GEN_FN_DEF(int, rename, const char *, const char *);
    GEN_FN_DEF(int, renameat, int olddirfd, const char *oldpath, int newdirfd, const char *newpath);
    GEN_FN_DEF(int, renameat2, int olddirfd, const char *oldpath, int newdirfd, const char *newpath, unsigned int flags);
    GEN_FN_DEF(int, link, const char *, const char *);
    GEN_FN_DEF(int, linkat, int, const char *, int, const char *, int);
    GEN_FN_DEF(int, unlink, const char *pathname);
    GEN_FN_DEF(int, unlinkat, int dirfd, const char *pathname, int flags);
    GEN_FN_DEF(int, symlink, const char *, const char *);
    GEN_FN_DEF(int, symlinkat, const char *, int, const char *);
    GEN_FN_DEF(ssize_t, readlink, const char *, char *, size_t);
    GEN_FN_DEF(ssize_t, readlinkat, int, const char *, char *, size_t);
    // This version of realpath handles null on the second argument differently: this behavior is crucial for the callers,
    // and without explicit versioning dlsym would fall back to the older version which fails.
    GEN_FN_DEF_VERSIONED(GLIBC_23, char*, realpath, const char*, char*);
    GEN_FN_DEF(DIR*, opendir, const char*);
    GEN_FN_DEF(DIR*, fdopendir, int);
    GEN_FN_DEF(int, utime, const char *filename, const struct utimbuf *times);
    GEN_FN_DEF(int, utimes, const char *filename, const struct timeval times[2]);
    GEN_FN_DEF(int, utimensat, int, const char*, const struct timespec[2], int);
    GEN_FN_DEF(int, futimesat, int dirfd, const char *pathname, const struct timeval times[2]);
    GEN_FN_DEF(int, futimens, int, const struct timespec[2]);
    GEN_FN_DEF(int, mkdir, const char*, mode_t);
    GEN_FN_DEF(int, mkdirat, int, const char*, mode_t);
    GEN_FN_DEF(int, mknod, const char *pathname, mode_t mode, dev_t dev);
    GEN_FN_DEF(int, mknodat, int dirfd, const char *pathname, mode_t mode, dev_t dev);
    GEN_FN_DEF(int, printf, const char*, ...);
    GEN_FN_DEF(int, fprintf, FILE*, const char*, ...);
    GEN_FN_DEF(int, dprintf, int, const char*, ...);
    GEN_FN_DEF(int, vprintf, const char*, va_list);
    GEN_FN_DEF(int, vfprintf, FILE*, const char*, va_list);
    GEN_FN_DEF(int, vdprintf, int, const char*, va_list);
    GEN_FN_DEF(int, chmod, const char *pathname, mode_t mode);
    GEN_FN_DEF(int, fchmod, int fd, mode_t mode);
    GEN_FN_DEF(int, fchmodat, int dirfd, const char *pathname, mode_t mode, int flags);
    GEN_FN_DEF(int, chown, const char *pathname, uid_t owner, gid_t group);
    GEN_FN_DEF(int, fchown, int fd, uid_t owner, gid_t group);
    GEN_FN_DEF(int, lchown, const char *pathname, uid_t owner, gid_t group);
    GEN_FN_DEF(int, fchownat, int dirfd, const char *pathname, uid_t owner, gid_t group, int flags);
    GEN_FN_DEF(ssize_t, sendfile, int out_fd, int in_fd, off_t *offset, size_t count);
    GEN_FN_DEF(ssize_t, sendfile64, int out_fd, int in_fd, off_t *offset, size_t count);
    GEN_FN_DEF(ssize_t, copy_file_range, int fd_in, loff_t *off_in, int fd_out, loff_t *off_out, size_t len, unsigned int flags);
    GEN_FN_DEF(int, name_to_handle_at, int dirfd, const char *pathname, struct file_handle *handle, int *mount_id, int flags);
    GEN_FN_DEF(int, dup, int oldfd);
    GEN_FN_DEF(int, dup2, int oldfd, int newfd);
    GEN_FN_DEF(int, dup3, int oldfd, int newfd, int flags);
    GEN_FN_DEF(int, scandir, const char * dirp, struct dirent *** namelist, int (*filter)(const struct dirent *), int (*compar)(const struct dirent **, const struct dirent **));
    GEN_FN_DEF(int, scandir64, const char * dirp, struct dirent64 *** namelist, int (*filter)(const struct dirent64  *), int (*compar)(const dirent64 **, const dirent64 **));
    GEN_FN_DEF(int, scandirat, int dirfd, const char * dirp, struct dirent *** namelist, int (*filter)(const struct dirent *), int (*compar)(const struct dirent **, const struct dirent **));
    GEN_FN_DEF(int, scandirat64, int dirfd, const char * dirp, struct dirent64 *** namelist, int (*filter)(const struct dirent64  *), int (*compar)(const dirent64 **, const dirent64 **));
    GEN_FN_DEF(int, statx, int dirfd, const char * pathname, int flags, unsigned int mask, struct statx * statxbuf);
    GEN_FN_DEF(int, closedir, DIR *dirp);
    GEN_FN_DEF(struct dirent *, readdir, DIR *dirp);
    GEN_FN_DEF(struct dirent64 *, readdir64, DIR *dirp);
    GEN_FN_DEF(int, readdir_r, DIR *dirp, struct dirent *entry, struct dirent **result);
    GEN_FN_DEF(int, readdir64_r, DIR *dirp, struct dirent64 *entry, struct dirent64 **result);

    /* ============ don't need to be interposed ======================= */
    GEN_FN_DEF(int, close, int fd);
    GEN_FN_DEF(int, fclose, FILE *stream);
    GEN_FN_DEF(int, statfs, const char *, struct statfs *buf);
    GEN_FN_DEF(int, statfs64, const char *, struct statfs64 *buf);
    GEN_FN_DEF(int, fstatfs, int fd, struct statfs *buf);
    GEN_FN_DEF(int, fstatfs64, int fd, struct statfs64 *buf);
    GEN_FN_DEF(FILE*, popen, const char *command, const char *type);
    GEN_FN_DEF(int, pclose, FILE *stream);
    GEN_FN_DEF(sem_t *, sem_open, const char *, int, mode_t, unsigned int);
    GEN_FN_DEF(int, sem_close, sem_t *);
    GEN_FN_DEF(int, sem_post, sem_t *);
    /* =================================================================== */

    /* ============ old/obsolete/unavailable ==========================
    GEN_FN_DEF(int, execveat, int dirfd, const char *pathname, char *const argv[], char *const envp[], int flags);
    GEN_FN_DEF(int, getdents, unsigned int fd, struct linux_dirent *dirp, unsigned int count);
    GEN_FN_DEF(int, getdents64, unsigned int fd, struct linux_dirent64 *dirp, unsigned int count);
    =================================================================== */
};

#endif // BUILDXL_SANDBOX_LINUX_BXL_OBSERVER_H