// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#pragma once

#include <limits.h>
#include <stddef.h>

#include <ostream>
#include <sstream>
#include <chrono>
#include <mutex>
#include <unordered_set>
#include <unordered_map>

#include "Sandbox.hpp"
#include "SandboxedPip.hpp"

using namespace std;

extern const char *__progname;

#define BxlEnvFamPath "__BUILDXL_FAM_PATH"
#define BxlEnvLogPath "__BUILDXL_LOG_PATH"
#define BxlEnvRootPid "__BUILDXL_ROOT_PID"

#define ARRAYSIZE(arr) (sizeof(arr)/sizeof(arr[0]))

#define GEN_FN_DEF_REAL(ret, name, ...)                                         \
    typedef ret (*fn_real_##name)(__VA_ARGS__);                                 \
    const fn_real_##name real_##name = (fn_real_##name)dlsym(RTLD_NEXT, #name);

#define GEN_FN_DEF(ret, name, ...) \
    GEN_FN_DEF_REAL(ret, name, __VA_ARGS__) \
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

#define MAKE_BODY(B) \
    B \
}

#define INTERPOSE(ret, name, ...) \
ret name(__VA_ARGS__) { \
    BxlObserver *bxl = BxlObserver::GetInstance(); \
    BXL_LOG_DEBUG(bxl, "Intercepted %s", #name); \
    MAKE_BODY

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

    std::timed_mutex cacheMtx_;
    std::unordered_map<es_event_type_t, std::unordered_set<std::string>> cache_;

    std::shared_ptr<SandboxedPip> pip_;
    std::shared_ptr<SandboxedProcess> process_;
    Sandbox *sandbox_;

    void InitFam();
    void InitLogFile();
    bool Send(const char *buf, size_t bufsiz);
    bool IsCacheHit(es_event_type_t event, const string &path, const string &secondPath);

    inline bool IsValid()   { return sandbox_ != NULL; }
    inline bool IsEnabled()
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

    const char* GetProgramPath() { return progFullPath_; }
    const char* GetReportsPath() { int len; return IsValid() ? pip_->GetReportsPath(&len) : NULL; }

    void report_exec(const char *syscallName, const char *procName, const char *file);

    AccessCheckResult report_access(const char *syscallName, IOEvent &event, bool checkCache = true);
    AccessCheckResult report_access(const char *syscallName, es_event_type_t eventType, const char *pathname, int oflags = 0);
    AccessCheckResult report_access(const char *syscallName, es_event_type_t eventType, std::string reportPath, std::string secondPath);

    AccessCheckResult report_access_fd(const char *syscallName, es_event_type_t eventType, int fd);
    AccessCheckResult report_access_at(const char *syscallName, es_event_type_t eventType, int dirfd, const char *pathname, int oflags = 0);

    ssize_t fd_to_path(int fd, char *buf, size_t bufsiz);
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
        struct stat buf;
        return real___lxstat(1, path, &buf) == 0
            ? buf.st_mode
            : 0;
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

    GEN_FN_DEF(pid_t, fork, void)
    GEN_FN_DEF_REAL(void, _exit, int)
    GEN_FN_DEF(int, fexecve, int, char *const[], char *const[])
    GEN_FN_DEF(int, execv, const char *, char *const[])
    GEN_FN_DEF(int, execve, const char *, char *const[], char *const[])
    GEN_FN_DEF(int, execvp, const char *, char *const[])
    GEN_FN_DEF(int, execvpe, const char *, char *const[], char *const[])
    GEN_FN_DEF(int, statfs, const char *, struct statfs *)
    GEN_FN_DEF(int, __lxstat, int, const char *, struct stat *)
    GEN_FN_DEF(int, __lxstat64, int, const char*, struct stat64*)
    GEN_FN_DEF(int, __xstat, int, const char *, struct stat *)
    GEN_FN_DEF(int, __xstat64, int, const char*, struct stat64*)
    GEN_FN_DEF(int, __fxstat, int, int, struct stat*);
    GEN_FN_DEF(int, __fxstatat, int, int, const char*, struct stat*, int);
    GEN_FN_DEF(int, __fxstat64, int, int, struct stat64*)
    GEN_FN_DEF(int, __fxstatat64, int, int, const char*, struct stat64*, int)
    GEN_FN_DEF(FILE*, fopen, const char *, const char *)
    GEN_FN_DEF(size_t, fread, void*, size_t, size_t, FILE*)
    GEN_FN_DEF(size_t, fwrite, const void*, size_t, size_t, FILE*)
    GEN_FN_DEF(int, fclose, FILE*)
    GEN_FN_DEF(int, fputc, int c, FILE *stream)
    GEN_FN_DEF(int, fputs, const char *s, FILE *stream)
    GEN_FN_DEF(int, putc, int c, FILE *stream)
    GEN_FN_DEF(int, putchar, int c)
    GEN_FN_DEF(int, puts, const char *s)
    GEN_FN_DEF(int, access, const char *, int)
    GEN_FN_DEF(int, faccessat, int, const char *, int, int)
    GEN_FN_DEF(int, creat, const char *, mode_t)
    GEN_FN_DEF(int, open, const char *, int, mode_t)
    GEN_FN_DEF(int, openat, int, const char *, int, mode_t)
    GEN_FN_DEF(int, close, int)
    GEN_FN_DEF(ssize_t, write, int, const void*, size_t)
    GEN_FN_DEF(int, remove, const char *)
    GEN_FN_DEF(int, rename, const char *, const char *)
    GEN_FN_DEF(int, link, const char *, const char *)
    GEN_FN_DEF(int, linkat, int, const char *, int, const char *, int)
    GEN_FN_DEF(int, unlink, const char *)
    GEN_FN_DEF(int, symlink, const char *, const char *)
    GEN_FN_DEF(int, symlinkat, const char *, int, const char *)
    GEN_FN_DEF(ssize_t, readlink, const char *, char *, size_t)
    GEN_FN_DEF(ssize_t, readlinkat, int, const char *, char *, size_t)
    GEN_FN_DEF(char*, realpath, const char*, char*)
    GEN_FN_DEF(DIR*, opendir, const char*)
    GEN_FN_DEF(DIR*, fdopendir, int)
    GEN_FN_DEF(int, utimensat, int, const char*, const struct timespec[2], int)
    GEN_FN_DEF(int, futimens, int, const struct timespec[2])
    GEN_FN_DEF(int, mkdir, const char*, mode_t)
    GEN_FN_DEF(int, mkdirat, int, const char*, mode_t)
    GEN_FN_DEF(int, dup, int)
    GEN_FN_DEF(int, dup2, int, int)
    GEN_FN_DEF(int, printf, const char*, ...);
    GEN_FN_DEF(int, fprintf, FILE*, const char*, ...);
    GEN_FN_DEF(int, dprintf, int, const char*, ...);
    GEN_FN_DEF(int, vprintf, const char*, va_list);
    GEN_FN_DEF(int, vfprintf, FILE*, const char*, va_list);
    GEN_FN_DEF(int, vdprintf, int, const char*, va_list);
    GEN_FN_DEF(int, chmod, const char *pathname, mode_t mode);
    GEN_FN_DEF(int, fchmod, int fd, mode_t mode);
    GEN_FN_DEF(int, fchmodat, int dirfd, const char *pathname, mode_t mode, int flags);
};
