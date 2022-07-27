// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#pragma once

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
#include <stddef.h>
#include <sys/sendfile.h>
#include <sys/stat.h>
#include <sys/syscall.h>
#include <sys/time.h>
#include <sys/types.h>
#include <sys/uio.h>
#include <sys/vfs.h>
#include <utime.h>

#include <ostream>
#include <sstream>
#include <chrono>
#include <mutex>
#include <unordered_set>
#include <unordered_map>

#include "Sandbox.hpp"
#include "SandboxedPip.hpp"
#include "utils.h"

/*
 * We want to compile against glibc 2.17 so that we are compatible with a broad range of Linux distributions. (e.g., starting from CentOS7)
 *
 * Compiling against that version of glibc does not prevent us from interposing the system calls that are present only in newer versions
 * (e.g., copy_file_range), as long as we provide appropriate INTERPOSE definitions for those system calls in detours.cpp.
 */
#if __GLIBC__ > 2 || (__GLIBC__ == 2 && __GLIBC_MINOR__ > 17)
    #warning This library must support glibc 2.17.  Please compile against at most glibc 2.17 before publishing a new version of this library.
#endif

/*
 * This header is compiled into two different libraries: libDetours.so and libAudit.so.
 *
 * When compiling libDetours.so, the ENABLE_INTERPOSING macro is defined, otherwise it is not.
 *
 * When ENABLE_INTERPOSING is defined, we do not need static declarations for the system calls of interest, because
 * we resolve those dynamically via `dlsym(name)` calls.  That means that, even though we compile libDetours.so against
 * glibc 2.17 (where, for example, `copy_file_range` is not defined), when our libDetours.so is loaded into a process that
 * runs against a newer version of glibc, `dlsym("copy_file_range")` will still return a valid function pointer and we 
 * will be able to interpose system calls that are not necessarily present in the glibc 2.17.
 * 
 * When ENABLE_INTERPOSING is not defined, we need static definitions for all the system calls we reference.  Therefore, 
 * here we need to add fake definitions for the calls that we want to reference (because of libDetours) which are not present
 * in glibc we are compiling against.  Adding empty definitions here is fine as long as in our code we never explicitly call
 * the corresponding real_<missing-syscall> instance methods in the BxlObserver class.
 */
#ifndef ENABLE_INTERPOSING
    // Library support for copy_file_range was added in glibc 2.27 (https://man7.org/linux/man-pages/man2/copy_file_range.2.html)
    #if __GLIBC__ < 2 || (__GLIBC__ == 2 && __GLIBC_MINOR__ < 27)
    inline ssize_t copy_file_range(int fd_in, loff_t *off_in, int fd_out, loff_t *off_out, size_t len, unsigned int flags) {
        return -1;
    }
    #endif

    // Library support for pwritev2 was added in glibc 2.26 (https://man7.org/linux/man-pages/man2/pwritev2.2.html)
    #if __GLIBC__ < 2 || (__GLIBC__ == 2 && __GLIBC_MINOR__ < 26)
    inline ssize_t pwritev2(int fd, const struct iovec *iov, int iovcnt, off_t offset, int flags) {
        return -1;
    }
    #endif
#endif

using namespace std;

extern const char *__progname;

// CODESYNC: Public/Src/Engine/Processes/SandboxConnectionLinuxDetours.cs
#define BxlEnvFamPath "__BUILDXL_FAM_PATH"
#define BxlEnvLogPath "__BUILDXL_LOG_PATH"
#define BxlEnvRootPid "__BUILDXL_ROOT_PID"
#define BxlEnvDetoursPath "__BUILDXL_DETOURS_PATH"

static const char LD_PRELOAD_ENV_VAR_PREFIX[] = "LD_PRELOAD=";

#define ARRAYSIZE(arr) (sizeof(arr)/sizeof(arr[0]))

#ifdef ENABLE_INTERPOSING
    #define GEN_FN_DEF_REAL(ret, name, ...)                                         \
        typedef ret (*fn_real_##name)(__VA_ARGS__);                                 \
        const fn_real_##name real_##name = (fn_real_##name)dlsym(RTLD_NEXT, #name);

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
#else
    #define GEN_FN_DEF_REAL(ret, name, ...)         \
        typedef ret (*fn_real_##name)(__VA_ARGS__); \
        const fn_real_##name real_##name = (fn_real_##name)name;

    #define IGNORE_BODY(B)

    #define INTERPOSE(ret, name, ...) IGNORE_BODY
#endif

#define GEN_FN_DEF(ret, name, ...)                                              \
    GEN_FN_DEF_REAL(ret, name, __VA_ARGS__)                                     \
    template<typename ...TArgs> result_t<ret> fwd_##name(TArgs&& ...args)       \
    {                                                                           \
        ret result = real_##name(std::forward<TArgs>(args)...);                 \
        result_t<ret> return_value(result);                                     \
        LOG_DEBUG("Forwarded syscall %s (errno: %d)",                           \
            RenderSyscall(#name, result, std::forward<TArgs>(args)...).c_str(), \
            return_value.get_errno());                                          \
        return return_value;                                                    \
    }                                                                           \
    template<typename ...TArgs> ret check_and_fwd_##name(AccessCheckResult &check, ret error_val, TArgs&& ...args) \
    {                                                                           \
        if (should_deny(check))                                                 \
        {                                                                       \
            errno = EPERM;                                                      \
            return error_val;                                                   \
        }                                                                       \
        else                                                                    \
        {                                                                       \
            return fwd_##name(args...).restore();                               \
        }                                                                       \
    }

#define _fatal(fmt, ...) do { real_fprintf(stderr, "(%s) " fmt "\n", __func__, __VA_ARGS__); _exit(1); } while (0)
#define fatal(msg) _fatal("%s", msg)

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
    ~BxlObserver() { disposed_ = true; }
    BxlObserver(const BxlObserver&) = delete;
    BxlObserver& operator = (const BxlObserver&) = delete;

    volatile int disposed_;
    int rootPid_;
    char progFullPath_[PATH_MAX];
    char logFile_[PATH_MAX];
    char detoursLibFullPath_[PATH_MAX];

    std::timed_mutex cacheMtx_;
    std::unordered_map<es_event_type_t, std::unordered_set<std::string>> cache_;

    // In a typical case, a process will not have more than 1024 open file descriptors at a time.
    // File descriptors start at 3 (1 and 2 are reserved for stdout and stderr).
    // Whenever a new file descriptor is created, the smallest available positive integer is assigned to it. 
    // Whenever a file descriptor is closed, its value is returned to the pool and will be used for new ones.
    // Setting the size of this table to 1024 should accommodate most of the common cases.
    // File descriptors can be greater than 1024, and if that happens we just won't cache their paths.
    static const int MAX_FD = 1024;
    std::string fdTable_[MAX_FD];
    std::string empty_str_;

    std::shared_ptr<SandboxedPip> pip_;
    std::shared_ptr<SandboxedProcess> process_;
    Sandbox *sandbox_;

    void InitFam();
    void InitLogFile();
    void InitDetoursLibPath();
    bool Send(const char *buf, size_t bufsiz);
    bool IsCacheHit(es_event_type_t event, const string &path, const string &secondPath);
    char** ensure_env_value_with_log(char *const envp[], char const *envName);

    ssize_t read_path_for_fd(int fd, char *buf, size_t bufsiz);

    bool IsMonitoringChildProcesses() const { return !pip_ || CheckMonitorChildProcesses(pip_->GetFamFlags()); }
    inline bool IsValid() const             { return sandbox_ != NULL; }
    inline bool IsEnabled() const
    {
        return
            // successfully initialized
            IsValid() &&
            // NOT (child processes should break away AND this is a child process)
            !(pip_->AllowChildProcessesToBreakAway() && getpid() != rootPid_);
    }

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

    void resolve_path(char *fullpath, bool followFinalSymlink);

    static BxlObserver *sInstance;
    static AccessCheckResult sNotChecked;

#if _DEBUG
    #define BXL_LOG_DEBUG(bxl, fmt, ...) if (bxl->LogDebugEnabled()) bxl->LogDebug("[%s:%d] " fmt "\n", __progname, getpid(), __VA_ARGS__);
#else
    #define BXL_LOG_DEBUG(bxl, fmt, ...)
#endif

#define LOG_DEBUG(fmt, ...) BXL_LOG_DEBUG(this, fmt, __VA_ARGS__)

public:
    static BxlObserver* GetInstance();

    bool SendReport(AccessReport &report);
    char** ensureEnvs(char *const envp[]);

    const char* GetProgramPath() { return progFullPath_; }
    const char* GetReportsPath() { int len; return IsValid() ? pip_->GetReportsPath(&len) : NULL; }
    const char* GetDetoursLibPath() { return detoursLibFullPath_; }

    void report_exec(const char *syscallName, const char *procName, const char *file);
    void report_audit_objopen(const char *fullpath)
    {
        IOEvent event(ES_EVENT_TYPE_NOTIFY_OPEN, ES_ACTION_TYPE_NOTIFY, fullpath, progFullPath_, S_IFREG);
        report_access("la_objopen", event, /* checkCache */ true);
    }

    AccessCheckResult report_access(const char *syscallName, IOEvent &event, bool checkCache = true);
    AccessCheckResult report_access(const char *syscallName, es_event_type_t eventType, const char *pathname, int oflags = 0);
    AccessCheckResult report_access(const char *syscallName, es_event_type_t eventType, const std::string &reportPath, const std::string &secondPath);

    AccessCheckResult report_access_fd(const char *syscallName, es_event_type_t eventType, int fd);
    AccessCheckResult report_access_at(const char *syscallName, es_event_type_t eventType, int dirfd, const char *pathname, int oflags = 0);

    // Send a special message to managed code if the policy to override allowed writes based on file existence is set
    // and the write is allowed by policy
    AccessCheckResult report_firstAllowWriteCheck(const char *fullPath);

    void reset_fd_table_entry(int fd);
    std::string fd_to_path(int fd);
    std::string normalize_path_at(int dirfd, const char *pathname, int oflags = 0);

    inline bool LogDebugEnabled()
    {
        return logFile_ && *logFile_;
    }

    inline void LogDebug(const char *fmt, ...)
    {
        if (LogDebugEnabled())
        {
            FILE* f = real_fopen(logFile_, "a");
            if (f)
            {
                va_list args;
                va_start(args, fmt);
                real_vfprintf(f, fmt, args);
                va_end(args);
                real_fclose(f);
            }
        }
    }

    mode_t get_mode(const char *path)
    {
        int old = errno;
        struct stat buf;
        mode_t result = real___lxstat(1, path, &buf) == 0
            ? buf.st_mode
            : 0;
        errno = old;
        return result;
    }

    std::string normalize_path(const char *pathname, int oflags = 0)
    {
        return normalize_path_at(AT_FDCWD, pathname, oflags);
    }

    std::string normalize_fd(int fd)
    {
        return normalize_path_at(fd, NULL);
    }

    bool IsFailingUnexpectedAccesses()
    {
        return CheckFailUnexpectedFileAccesses(pip_->GetFamFlags());
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
        return IsEnabled() && check.ShouldDenyAccess() && IsFailingUnexpectedAccesses();
    }

    GEN_FN_DEF(void*, dlopen, const char *filename, int flags);
    GEN_FN_DEF(int, dlclose, void *handle);

    GEN_FN_DEF(pid_t, fork, void);
    GEN_FN_DEF(int, clone, int (*fn)(void *), void *child_stack, int flags, void *arg, ... /* pid_t *ptid, void *newtls, pid_t *ctid */ );
    GEN_FN_DEF_REAL(void, _exit, int);
    GEN_FN_DEF(int, fexecve, int, char *const[], char *const[]);
    GEN_FN_DEF(int, execv, const char *, char *const[]);
    GEN_FN_DEF(int, execve, const char *, char *const[], char *const[]);
    GEN_FN_DEF(int, execvp, const char *, char *const[]);
    GEN_FN_DEF(int, execvpe, const char *, char *const[], char *const[]);
    GEN_FN_DEF(int, __lxstat, int, const char *, struct stat *);
    GEN_FN_DEF(int, __lxstat64, int, const char*, struct stat64*);
    GEN_FN_DEF(int, __xstat, int, const char *, struct stat *);
    GEN_FN_DEF(int, __xstat64, int, const char*, struct stat64*);
    GEN_FN_DEF(int, __fxstat, int, int, struct stat*);
    GEN_FN_DEF(int, __fxstatat, int, int, const char*, struct stat*, int);;
    GEN_FN_DEF(int, __fxstat64, int, int, struct stat64*);
    GEN_FN_DEF(int, __fxstatat64, int, int, const char*, struct stat64*, int);
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
    GEN_FN_DEF(int, link, const char *, const char *);
    GEN_FN_DEF(int, linkat, int, const char *, int, const char *, int);
    GEN_FN_DEF(int, unlink, const char *pathname);
    GEN_FN_DEF(int, unlinkat, int dirfd, const char *pathname, int flags);
    GEN_FN_DEF(int, symlink, const char *, const char *);
    GEN_FN_DEF(int, symlinkat, const char *, int, const char *);
    GEN_FN_DEF(ssize_t, readlink, const char *, char *, size_t);
    GEN_FN_DEF(ssize_t, readlinkat, int, const char *, char *, size_t);
    GEN_FN_DEF(char*, realpath, const char*, char*);
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

    /* ============ don't need to be interposed ======================= */
    GEN_FN_DEF(int, dup, int oldfd);
    GEN_FN_DEF(int, dup2, int oldfd, int newfd);
    GEN_FN_DEF(int, close, int fd);
    GEN_FN_DEF(int, fclose, FILE *stream);
    GEN_FN_DEF(int, statfs, const char *, struct statfs *buf);
    GEN_FN_DEF(int, statfs64, const char *, struct statfs64 *buf);
    GEN_FN_DEF(int, fstatfs, int fd, struct statfs *buf);
    GEN_FN_DEF(int, fstatfs64, int fd, struct statfs64 *buf); 
    /* =================================================================== */

    /* ============ old/obsolete/unavailable ==========================
    GEN_FN_DEF(int, execveat, int dirfd, const char *pathname, char *const argv[], char *const envp[], int flags);
    GEN_FN_DEF(int, renameat2, int olddirfd, const char *oldpath, int newdirfd, const char *newpath, unsigned int flags);
    GEN_FN_DEF(int, getdents, unsigned int fd, struct linux_dirent *dirp, unsigned int count);
    GEN_FN_DEF(int, getdents64, unsigned int fd, struct linux_dirent64 *dirp, unsigned int count);
    =================================================================== */
};
