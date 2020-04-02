// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#define _GNU_SOURCE

#include <dirent.h>
#include <errno.h>
#include <limits.h>
#include <stdarg.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <dlfcn.h>
#include <unistd.h>
#include <sys/vfs.h>
#include <sys/types.h>
#include <sys/param.h>
#include <sys/mount.h>
#include <sys/stat.h>
#include <sys/fcntl.h>
#include <sys/xattr.h>

// #include "../MacOs/Sandbox/Src/BuildXLSandboxShared.hpp"

extern char *__progname;

static const char *EnvLogPath = "__BUILDXL_DetoursLogPath";

enum RequestedAccess {
    kNone = 0,
    kRead = 1,
    kWrite = 1 << 1,
    kProbe = 1 << 2,
    kEnumerate = 1 << 3,
    kEnumerationProbe = 1 << 4,
    kLookup = 1 << 5
};

enum FileAccessStatus {
    kAllowed = 1,
    kDenied = 2,
    kCannotDeterminePolicy = 3
};

enum Operation {
    kOpProcess = 0,
    kOpProcessExit,
    kOpProcessTreeCompletedAck,
    kOpWrite = 24,
    kOpRead = 25,
    kOpProbe = 26
};

static bool fatal(const char *fmt, ...)
{
    va_list args;
    va_start(args, fmt);
    vfprintf(stderr, fmt, args);
    va_end(args);
    exit(-1);
    return false;
}

static bool send(const char *buf, size_t bufsiz)
{
    static int (*real_open)(const char *, int) = NULL;
    if (!real_open) real_open = dlsym(RTLD_NEXT, "open");
    if (!real_open)
    {
        return fatal("syscall 'open' not found; errno: %d", errno);
    }

    static const char *logPath = NULL;
    if (!logPath)
    {
        logPath = getenv(EnvLogPath);
    }

    if (!logPath || *logPath == '\0')
    {
        return fatal("Env var '%s' not set.", EnvLogPath);
    }

    if (bufsiz > PIPE_BUF)
    {
        return fatal("Cannot atomically send a buffer whose size (%d) is greater than PIPE_BUF (%d)", bufsiz, PIPE_BUF);
    }

    int logFd = real_open(logPath, O_WRONLY | O_APPEND);
    if (logFd == -1)
    {
        return fatal("Could not open file '%s'; errno: %d", logPath, errno);
    }

    ssize_t numWritten = write(logFd, buf, bufsiz);
    if (numWritten < bufsiz)
    {
        return fatal("Wrote only %d bytes out of %d", numWritten, bufsiz);
    }

    close(logFd);
    return true;
}

static ssize_t fd_to_path(int fd, char *buf, size_t bufsiz)
{
    static ssize_t (*real_readlink)(const char *restrict, char *restrict, size_t) = NULL;
    if (!real_readlink) real_readlink = dlsym(RTLD_NEXT, "readlink");

    char procPath[100] = {0};
    sprintf(procPath, "/proc/self/fd/%d", fd);
    return real_readlink(procPath, buf, bufsiz);
}

static bool report_access_at(const char *fname, int dirfd, const char *pathname, enum RequestedAccess access, enum Operation opcode);

static bool report_access(const char *fname, const char *pathname, enum RequestedAccess access, enum Operation opcode)
{
    char realpathBuf[PATH_MAX];
    char *realpathPtr = realpath(pathname, realpathBuf);

    const int explicitLogging       = 1;
    int err                         = realpathPtr == NULL ? 2 : 0;
    const char *reportPath          = realpathPtr == NULL ? pathname : realpathPtr;
    enum RequestedAccess realAccess = realpathPtr == NULL ? kProbe : access;

    const int PrefixLength = sizeof(uint);
    char buffer[PIPE_BUF] = {0};
    int maxMessageLength = PIPE_BUF - PrefixLength;
    int numWritten = snprintf(
        &buffer[PrefixLength], maxMessageLength, "%s|%d|%d|%d|%d|%d|%s|%d|%s\n", 
        __progname, getpid(), access, kAllowed, explicitLogging, err, fname, opcode, reportPath);
    if (numWritten == maxMessageLength)
    {
        return fatal("Message truncated to fit PIPE_BUF (%d): %s", PIPE_BUF, buffer);
    }

    *(uint*)(buffer) = numWritten;
    return send(buffer, numWritten + PrefixLength);
}

static bool report_access_fd(const char *fname, int fd, enum RequestedAccess access, enum Operation opcode)
{
    char fullpath[PATH_MAX] = {0};
    fd_to_path(fd, fullpath, PATH_MAX);

    return report_access(fname, fullpath, access, opcode);
}

static bool report_access_at(const char *fname, int dirfd, const char *pathname, enum RequestedAccess access, enum Operation opcode)
{
    char fullpath[PATH_MAX] = {0};
    ssize_t len = 0;

    if (dirfd == AT_FDCWD)
    {
        getcwd(fullpath, PATH_MAX);
        len = strlen(fullpath);
    }
    else
    {
        len = fd_to_path(dirfd, fullpath, PATH_MAX);
    }

    if (len <= 0)
    {
        return fatal("Could not get path for fd %d; errno: %d", dirfd, errno);
    }

    snprintf(&fullpath[len], PATH_MAX - len, "/%s", pathname);
    return report_access(fname, fullpath, access, opcode);
}

inline static bool report_read_fd(const char *fname, int fd)                          { return report_access_fd(fname, fd, kRead, kOpRead); }
inline static bool report_read_at(const char *fname, int dirfd, const char *pathname) { return report_access_at(fname, dirfd, pathname, kRead, kOpRead); }
inline static bool report_read(const char *fname, const char *pathname)               { return *pathname != '/' ? report_access_at(fname, AT_FDCWD, pathname, kRead, kOpRead) : report_access(fname, pathname, kRead, kOpRead); }

inline static bool report_probe_fd(const char *fname, int fd)                          { return report_access_fd(fname, fd, kProbe, kOpProbe); }
inline static bool report_probe_at(const char *fname, int dirfd, const char *pathname) { return report_access_at(fname, dirfd, pathname, kProbe, kOpProbe); }
inline static bool report_probe(const char *fname, const char *pathname)               { return *pathname != '/' ? report_access_at(fname, AT_FDCWD, pathname, kProbe, kOpProbe) : report_access(fname, pathname, kProbe, kOpProbe); }

inline static bool report_write_fd(const char *fname, int fd)                          { return report_access_fd(fname, fd, kWrite, kOpWrite); }
inline static bool report_write_at(const char *fname, int dirfd, const char *pathname) { return report_access_at(fname, dirfd, pathname, kWrite, kOpWrite); }
inline static bool report_write(const char *fname, const char *pathname)               { return *pathname != '/' ? report_access_at(fname, AT_FDCWD, pathname, kWrite, kOpWrite) : report_access(fname, pathname, kWrite, kOpWrite); }

static enum RequestedAccess oflag_to_access(int oflag)
{
    return oflag & (O_WRONLY | O_RDWR) ? kWrite : kRead;
}

int fexecve(int fd, char *const argv[], char *const envp[]) {
    static int (*real_fexecve)(int fd, char *const argv[], char *const envp[]) = NULL;
    if (!real_fexecve) real_fexecve = dlsym(RTLD_NEXT, __func__);

    report_read_fd(__func__, fd);
    return real_fexecve(fd, argv, envp);
}

int execveat(int dirfd, const char *pathname, char *const argv[], char *const envp[], int flags) {
    static int (*real_execveat)(int, const char *, char *const[], char *const[], int) = NULL;
    if (!real_execveat) real_execveat = dlsym(RTLD_NEXT, __func__);

    report_read_at(__func__, dirfd, pathname);
    return real_execveat(dirfd, pathname, argv, envp, flags);
}

int execv(const char *file, char *const argv[]) {
    static int (*real_execv)(const char *, char *const[]) = NULL;
    if (!real_execv) real_execv = dlsym(RTLD_NEXT, __func__);

    report_read(__func__, file);
    return real_execv(file, argv);
}

int execve(const char *file, char *const argv[], char *const envp[]) {
    static int (*real_execve)(const char *, char *const[], char *const[]) = NULL;
    if (!real_execve) real_execve = dlsym(RTLD_NEXT, __func__);

    report_read(__func__, file);
    return real_execve(file, argv, envp);
}

int execvp(const char *file, char *const argv[]) {
    static int (*real_execvp)(const char *, char *const[]) = NULL;
    if (!real_execvp) real_execvp = dlsym(RTLD_NEXT, __func__);

    report_read(__func__, file);
    return real_execvp(file, argv);
}

int execvpe(const char *file, char *const argv[], char *const envp[]) {
    static int (*real_execvpe)(const char *, char *const[], char *const[]) = NULL;
    if (!real_execvpe) real_execvpe = dlsym(RTLD_NEXT, __func__);

    report_read(__func__, file);
    return real_execvpe(file, argv, envp);
}

int fstat(int fd, struct stat *statbuf) {
    static int (*real_fstat)(int, struct stat *) = NULL;
    if (!real_fstat) real_fstat = dlsym(RTLD_NEXT, "fstat");

    report_probe_fd(__func__, fd);
    return real_fstat(fd, statbuf);
}

int __fxstat(int __ver, int fd, struct stat *__stat_buf) {
    static int (*real___fxstat)(int, int, struct stat *) = NULL;
    if (!real___fxstat) real___fxstat = dlsym(RTLD_NEXT, "__fxstat");

    report_probe_fd(__func__, fd);
    return real___fxstat(__ver, fd, __stat_buf);
}

int statfs(const char *pathname, struct statfs *buf) {
    static int (*real_statfs)(const char *, struct statfs *) = NULL;
    if (!real_statfs) real_statfs = dlsym(RTLD_NEXT, "statfs");

    report_probe(__func__, pathname);
    return real_statfs(pathname, buf);
}

int stat(const char *pathname, struct stat *buf) {
    static int (*real_stat)(const char *, struct stat *) = NULL;
    if (!real_stat) real_stat = dlsym(RTLD_NEXT, "stat");

    report_probe(__func__, pathname);
    return real_stat(pathname, buf);
}

int lstat(const char *pathname, struct stat *buf) {
    static int (*real_lstat)(const char *, struct stat *) = NULL;
    if (!real_lstat) real_lstat = dlsym(RTLD_NEXT, "lstat");

    report_probe(__func__, pathname);
    return real_lstat(pathname, buf);
}

int __xstat(int __ver, const char *pathname, struct stat *buf) {
    static int (*real___xstat)(int, const char *, struct stat *) = NULL;
    if (!real___xstat) real___xstat = dlsym(RTLD_NEXT, "__xstat");

    report_probe(__func__, pathname);
    return real___xstat(__ver, pathname, buf);
}

int __lxstat(int __ver, const char *pathname, struct stat *buf) {
    static int (*real___lxstat)(int, const char *, struct stat *) = NULL;
    if (!real___lxstat) real___lxstat = dlsym(RTLD_NEXT, "__lxstat");
    
    report_probe(__func__, pathname);
    return real___lxstat(__ver, pathname, buf);
}

FILE* fopen(const char *pathname, const char *mode) {
    static FILE* (*real_fopen)(const char *, const char *) = NULL;
    if (!real_fopen) real_fopen = dlsym(RTLD_NEXT, "fopen");
    
    report_access(__func__, pathname, *mode == 'r' ? kRead : kWrite, kOpRead);
    return real_fopen(pathname, mode);
}

int access(const char *pathname, int mode) {
    static int (*real_access)(const char *, int) = NULL;
    if (!real_access) real_access = dlsym(RTLD_NEXT, "access");

    report_probe(__func__, pathname);
    return real_access(pathname, mode);
}

int faccessat(int dirfd, const char *pathname, int mode, int flags) {
    static int (*real_faccessat)(int, const char *, int, int) = NULL;
    if (!real_faccessat) real_faccessat = dlsym(RTLD_NEXT, "faccessat");

    report_probe_at(__func__, dirfd, pathname);
    return real_faccessat(dirfd, pathname, mode, flags);
}

int open(const char *path, int oflag, ...) {
    static int (*real_open)(const char *, int, mode_t) = NULL;
    if (!real_open) real_open = dlsym(RTLD_NEXT, "open");

    va_list args;
    va_start(args, oflag);
    mode_t mode = va_arg(args, mode_t);
    va_end(args);

    report_access(__func__, path, oflag_to_access(oflag), kOpRead);
    return real_open(path, oflag, mode);
}

int creat(const char *pathname, mode_t mode) {
    static int (*real_creat)(const char *, mode_t) = NULL;
    if (!real_creat) real_creat = dlsym(RTLD_NEXT, "creat");

    report_write(__func__, pathname);
    return real_creat(pathname, mode);
}

int openat(int dirfd, const char *pathname, int flags, ...) {
    static int (*real_openat)(int, const char *, int, mode_t) = NULL;
    if (!real_openat) real_openat = dlsym(RTLD_NEXT, "openat");

    va_list args;
    va_start(args, flags);
    mode_t mode = va_arg(args, mode_t);
    va_end(args);

    report_access_at(__func__, dirfd, pathname, oflag_to_access(flags), kOpRead);
    return real_openat(dirfd, pathname, flags, mode);
}

int remove(const char *pathname) {
    static int (*real_remove)(const char *) = NULL;
    if (!real_remove) real_remove = dlsym(RTLD_NEXT, "remove");

    report_write(__func__, pathname);
    return real_remove(pathname);
}

int rename(const char *old, const char *new) {
    static int (*real_rename)(const char *, const char *) = NULL;
    if (!real_rename) real_rename = dlsym(RTLD_NEXT, "rename");

    report_read(__func__, old);
    report_write(__func__, new);
    return real_rename(old, new);
}

int link(const char *path1, const char *path2) {
    static int (*real_link)(const char *, const char *) = NULL;
    if (!real_link) real_link = dlsym(RTLD_NEXT, "link");

    report_read(__func__, path1);
    report_write(__func__, path2);
    return real_link(path1, path2);
}

int linkat(int fd1, const char *name1, int fd2, const char *name2, int flag) {
    static int (*real_linkat)(int, const char *, int, const char *, int) = NULL;
    if (!real_linkat) real_linkat = dlsym(RTLD_NEXT, "linkat");

    report_read_at(__func__, fd1, name1);
    report_write_at(__func__, fd2, name2);
    return real_linkat(fd1, name1, fd2, name2, flag);
}

int unlink(const char *path) {
    static int (*real_unlink)(const char *) = NULL;
    if (!real_unlink) real_unlink = dlsym(RTLD_NEXT, "unlink");

    report_write(__func__, path);
    return real_unlink(path);
}

int symlink(const char *path1, const char *path2) {
    static int (*real_symlink)(const char *, const char *) = NULL;
    if (!real_symlink) real_symlink = dlsym(RTLD_NEXT, "symlink");

    report_write(__func__, path2);
    return real_symlink(path1, path2);
}

int symlinkat(const char *name1, int fd, const char *name2) {
    static int (*real_symlinkat)(const char *, int, const char *) = NULL;
    if (!real_symlinkat) real_symlinkat = dlsym(RTLD_NEXT, "symlinkat");

    report_write_at(__func__, fd, name2);
    return real_symlinkat(name1, fd, name2);
}

ssize_t readlink(const char *restrict path, char *restrict buf, size_t bufsize) {
    static ssize_t (*real_readlink)(const char *restrict, char *restrict, size_t) = NULL;
    if (!real_readlink) real_readlink = dlsym(RTLD_NEXT, "readlink");

    report_read(__func__, path);
    return real_readlink(path, buf, bufsize);
}

ssize_t readlinkat(int fd, const char *restrict path, char *restrict buf, size_t bufsize) {
    static ssize_t (*real_readlinkat)(int, const char *restrict, char *restrict, size_t) = NULL;
    if (!real_readlinkat) real_readlinkat = dlsym(RTLD_NEXT, __func__);
    
    report_read_at(__func__, fd, path);
    return real_readlinkat(fd, path, buf, bufsize);
}

DIR* opendir(const char *name) {
    static DIR* (*real_opendir)(const char*) = NULL;
    if (!real_opendir) real_opendir = dlsym(RTLD_NEXT, __func__);

    report_read(__func__, name);
    return real_opendir(name);
}

static inline void report_exit()
{
    report_access("atexit", "", 0, kOpProcessExit);
}

void __attribute__ ((constructor)) my_library_init(void)
{
    atexit(report_exit);
}
