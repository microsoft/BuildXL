// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#pragma once

#include <limits.h>
#include <stddef.h>

#include "Sandbox.hpp"
#include "SandboxedPip.hpp"

#define BxlEnvFamPath "__BUILDXL_FAM_PATH"
#define BxlEnvLogPath "__BUILDXL_LOG_PATH"

#define ARRAYSIZE(arr) (sizeof(arr)/sizeof(arr[0]))

#define GEN_FN_DEF(ret, name, ...) \
    typedef ret (*fn_real_##name)(__VA_ARGS__); \
    const fn_real_##name real_##name = (fn_real_##name)dlsym(RTLD_NEXT, #name);

#define INTERPOSE(ret, name, ...) \
    ret name(__VA_ARGS__)

#define _fatal(fmt, ...) do { fprintf(stderr, "(%s) " fmt "\n", __func__, __VA_ARGS__); _exit(1); } while (0)
#define fatal(msg) _fatal("%s", msg)

/**
 * Wraps the result of a syscall together with the current 'errno'.
 * 
 * When the destructor is called, 'errno' is reset back to the value
 * that was captured in the constructor.
 */
template <typename T>
class result_t final
{
private:
    int my_errno_;
    T result_;

public:
    result_t(T result) : result_(result), my_errno_(errno) {}
    ~result_t() { errno = my_errno_; }
    operator T() { return result_; }
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
    ~BxlObserver() {}
    BxlObserver(const BxlObserver&) = delete;
    BxlObserver& operator = (const BxlObserver&) = delete;

    char progFullPath_[PATH_MAX];
    char logFile_[PATH_MAX];
    std::shared_ptr<SandboxedPip> pip_;
    std::shared_ptr<SandboxedProcess> process_;
    Sandbox *sandbox_;

    void InitFam();
    void InitLogFile();
    bool Send(const char *buf, size_t bufsiz);

    bool IsValid() { return sandbox_ != NULL; }

    static BxlObserver *sInstance;

    #define LogDebug(fmt, ...) if (logFile_ && *logFile_) do { \
        FILE* _lf = real_fopen(logFile_, "a"); \
        if (_lf) fprintf(_lf, "[%s:%d] " fmt "\n", __progname, getpid(), __VA_ARGS__); \
        if (_lf) real_fclose(_lf); \
    } while(0);

public:
    static BxlObserver* GetInstance(); 

    bool SendReport(AccessReport &report);

    const char* GetProgramPath() { return progFullPath_; }
    const char* GetReportsPath() { int len; return IsValid() ? pip_->GetReportsPath(&len) : NULL; }

    bool report_access(const char *syscallName, IOEvent &event);
    bool report_access(const char *syscallName, es_event_type_t eventType, const char *pathname);
    bool report_access(const char *syscallName, es_event_type_t eventType, std::string reportPath, std::string secondPath);

    bool report_access_fd(const char *syscallName, es_event_type_t eventType, int fd);
    bool report_access_at(const char *syscallName, es_event_type_t eventType, int dirfd, const char *pathname);

    ssize_t fd_to_path(int fd, char *buf, size_t bufsiz);
    std::string normalize_path_at(int dirfd, const char *pathname);

    std::string normalize_path(const char *pathname)
    {
        return normalize_path_at(AT_FDCWD, pathname);
    }

    std::string normalize_fd(int fd)
    {
        return normalize_path_at(fd, NULL);
    }

    GEN_FN_DEF(pid_t, fork, void)
    GEN_FN_DEF(int, fexecve, int, char *const[], char *const[])
    GEN_FN_DEF(int, execv, const char *, char *const[])
    GEN_FN_DEF(int, execve, const char *, char *const[], char *const[])
    GEN_FN_DEF(int, execvp, const char *, char *const[])
    GEN_FN_DEF(int, execvpe, const char *, char *const[], char *const[])
    GEN_FN_DEF(int, __fxstat, int, int, struct stat*);
    GEN_FN_DEF(int, statfs, const char *, struct statfs *)
    GEN_FN_DEF(int, __xstat, int, const char *, struct stat *)
    GEN_FN_DEF(int, __lxstat, int, const char *, struct stat *)
    GEN_FN_DEF(int, __xstat64, int, const char*, struct stat64*)
    GEN_FN_DEF(int, __lxstat64, int, const char*, struct stat64*)
    GEN_FN_DEF(int, __fxstat64, int, int, struct stat64*)
    GEN_FN_DEF(FILE*, fopen, const char *, const char *)
    GEN_FN_DEF(size_t, fread, void*, size_t, size_t, FILE*)
    GEN_FN_DEF(int, fclose, FILE*)
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
    GEN_FN_DEF(int, utimensat, int, const char*, const struct timespec[2], int)
    GEN_FN_DEF(int, futimens, int, const struct timespec[2])
};
