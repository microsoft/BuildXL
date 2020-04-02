// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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

#include "linux_sandbox.h"

extern char *__progname;

#define GEN_REAL(ret, name, ...) \
    typedef ret (*fn_real_##name)(__VA_ARGS__); \
    static fn_real_##name real_##name = (fn_real_##name)dlsym(RTLD_NEXT, #name);


static bool send(const char *buf, size_t bufsiz)
{
    GEN_REAL(int, open, const char *, int);

    if (!real_open)
    {
        _fatal("syscall 'open' not found; errno: %d", errno);
    }

    if (bufsiz > PIPE_BUF)
    {
        _fatal("Cannot atomically send a buffer whose size (%ld) is greater than PIPE_BUF (%d)", bufsiz, PIPE_BUF);
    }

    static bxl_state *bxl_state = NULL; 
    if (bxl_state == NULL)
    {
        bxl_state = bxl_linux_sandbox_init();
    }

    if (bxl_state == NULL)
    {
        fatal("Not initialized!");
    }

    int len;
    int logFd = real_open(bxl_state->pip->GetReportsPath(&len), O_WRONLY | O_APPEND);
    if (logFd == -1)
    {
        _fatal("Could not open file '%s'; errno: %d", bxl_state->pip->GetReportsPath(&len), errno);
    }

    ssize_t numWritten = write(logFd, buf, bufsiz);
    if (numWritten < bufsiz)
    {
        _fatal("Wrote only %ld bytes out of %ld", numWritten, bufsiz);
    }

    close(logFd);
    return true;
}

static ssize_t fd_to_path(int fd, char *buf, size_t bufsiz)
{
    GEN_REAL(ssize_t, readlink, const char *, char *, size_t);

    char procPath[100] = {0};
    sprintf(procPath, "/proc/self/fd/%d", fd);
    return real_readlink(procPath, buf, bufsiz);
}

static bool report_access_at(const char *opname, int dirfd, const char *pathname, RequestedAccess access, FileOperation opcode);

static bool report_access(const char *opname, const char *pathname, RequestedAccess access, FileOperation opcode)
{
    int oldErrno = errno;

    char realpathBuf[PATH_MAX];
    char *realpathPtr = realpath(pathname, realpathBuf);

    const int explicitLogging  = 1;
    int err                    = realpathPtr == NULL ? 2 : 0;
    const char *reportPath     = realpathPtr == NULL ? pathname : realpathPtr;
    RequestedAccess realAccess = realpathPtr == NULL ? RequestedAccess::Probe : access;

    const int PrefixLength = sizeof(uint);
    char buffer[PIPE_BUF] = {0};
    int maxMessageLength = PIPE_BUF - PrefixLength;
    int numWritten = snprintf(
        &buffer[PrefixLength], maxMessageLength, "%s|%d|%d|%d|%d|%d|%s|%d|%s\n", 
        __progname, getpid(), (int)access, FileAccessStatus::FileAccessStatus_Allowed, explicitLogging, err, opname, opcode, reportPath);
    if (numWritten == maxMessageLength)
    {
        _fatal("Message truncated to fit PIPE_BUF (%d): %s", PIPE_BUF, buffer);
    }

    *(uint*)(buffer) = numWritten;
    bool ok = send(buffer, numWritten + PrefixLength);
    errno = oldErrno;
    return ok;
}

static bool report_access_fd(const char *opname, int fd, RequestedAccess access, FileOperation opcode)
{
    char fullpath[PATH_MAX] = {0};
    fd_to_path(fd, fullpath, PATH_MAX);

    return report_access(opname, fullpath, access, opcode);
}

static bool report_access_at(const char *opname, int dirfd, const char *pathname, RequestedAccess access, FileOperation opcode)
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
        _fatal("Could not get path for fd %d; errno: %d", dirfd, errno);
    }

    snprintf(&fullpath[len], PATH_MAX - len, "/%s", pathname);
    return report_access(opname, fullpath, access, opcode);
}

inline static bool report_read_fd(const char *opname, int fd)                          { return report_access_fd(opname, fd, RequestedAccess::Read, FileOperation::kOpKAuthReadFile); }
inline static bool report_read_at(const char *opname, int dirfd, const char *pathname) { return report_access_at(opname, dirfd, pathname, RequestedAccess::Read, FileOperation::kOpKAuthReadFile); }
inline static bool report_read(const char *opname, const char *pathname)               { return *pathname != '/' ? report_access_at(opname, AT_FDCWD, pathname, RequestedAccess::Read, FileOperation::kOpKAuthReadFile) : report_access(opname, pathname, RequestedAccess::Read, FileOperation::kOpKAuthReadFile); }

inline static bool report_probe_fd(const char *opname, int fd)                          { return report_access_fd(opname, fd, RequestedAccess::Probe, FileOperation::kOpKAuthVNodeProbe); }
inline static bool report_probe_at(const char *opname, int dirfd, const char *pathname) { return report_access_at(opname, dirfd, pathname, RequestedAccess::Probe, FileOperation::kOpKAuthVNodeProbe); }
inline static bool report_probe(const char *opname, const char *pathname)               { return *pathname != '/' ? report_access_at(opname, AT_FDCWD, pathname, RequestedAccess::Probe, FileOperation::kOpKAuthVNodeProbe) : report_access(opname, pathname, RequestedAccess::Probe, FileOperation::kOpKAuthVNodeProbe); }

inline static bool report_write_fd(const char *opname, int fd)                          { return report_access_fd(opname, fd, RequestedAccess::Write, FileOperation::kOpKAuthWriteFile); }
inline static bool report_write_at(const char *opname, int dirfd, const char *pathname) { return report_access_at(opname, dirfd, pathname, RequestedAccess::Write, FileOperation::kOpKAuthWriteFile); }
inline static bool report_write(const char *opname, const char *pathname)               { return *pathname != '/' ? report_access_at(opname, AT_FDCWD, pathname, RequestedAccess::Write, FileOperation::kOpKAuthWriteFile) : report_access(opname, pathname, RequestedAccess::Write, FileOperation::kOpKAuthWriteFile); }

static enum RequestedAccess oflag_to_access(int oflag)
{
    return oflag & (O_WRONLY | O_RDWR) ? RequestedAccess::Write : RequestedAccess::Read;
}

int fexecve(int fd, char *const argv[], char *const envp[]) {
    GEN_REAL(int, fexecve, int, char *const[], char *const[])
    report_read_fd(__func__, fd);
    return real_fexecve(fd, argv, envp);
}

int execveat(int dirfd, const char *pathname, char *const argv[], char *const envp[], int flags) {
    GEN_REAL(int, execveat, int, const char *, char *const[], char *const[], int)
    report_read_at(__func__, dirfd, pathname);
    return real_execveat(dirfd, pathname, argv, envp, flags);
}

int execv(const char *file, char *const argv[]) {
    GEN_REAL(int, execv, const char *, char *const[])
    report_read(__func__, file);
    return real_execv(file, argv);
}

int execve(const char *file, char *const argv[], char *const envp[]) {
    GEN_REAL(int, execve, const char *, char *const[], char *const[])
    report_read(__func__, file);
    return real_execve(file, argv, envp);
}

int execvp(const char *file, char *const argv[]) {
    GEN_REAL(int, execvp, const char *, char *const[])
    report_read(__func__, file);
    return real_execvp(file, argv);
}

int execvpe(const char *file, char *const argv[], char *const envp[]) {
    GEN_REAL(int, execvpe, const char *, char *const[], char *const[])
    report_read(__func__, file);
    return real_execvpe(file, argv, envp);
}

int fstat(int fd, struct stat *statbuf) {
    GEN_REAL(int, fstat, int, struct stat *)
    report_probe_fd(__func__, fd);
    return real_fstat(fd, statbuf);
}

int __fxstat(int __ver, int fd, struct stat *__stat_buf) {
    GEN_REAL(int, __fxstat, int, int, struct stat*);

    report_probe_fd(__func__, fd);
    return real___fxstat(__ver, fd, __stat_buf);
}

int statfs(const char *pathname, struct statfs *buf) {
    GEN_REAL(int, statfs, const char *, struct statfs *);
    report_probe(__func__, pathname);
    return real_statfs(pathname, buf);
}

int stat(const char *pathname, struct stat *buf) {
    GEN_REAL(int, stat, const char *, struct stat *);
    report_probe(__func__, pathname);
    return real_stat(pathname, buf);
}

int lstat(const char *pathname, struct stat *buf) {
    GEN_REAL(int, lstat, const char *, struct stat *);
    report_probe(__func__, pathname);
    return real_lstat(pathname, buf);
}

int __xstat(int __ver, const char *pathname, struct stat *buf) {
    GEN_REAL(int, __xstat, int, const char *, struct stat *);
    report_probe(__func__, pathname);
    return real___xstat(__ver, pathname, buf);
}

int __lxstat(int __ver, const char *pathname, struct stat *buf) {
    GEN_REAL(int, __lxstat, int, const char *, struct stat *);
    report_probe(__func__, pathname);
    return real___lxstat(__ver, pathname, buf);
}

FILE* fopen(const char *pathname, const char *mode) {
    GEN_REAL(FILE*, fopen, const char *, const char *);
    report_access(
        __func__,
        pathname,
        *mode == 'r' ? RequestedAccess::Read : RequestedAccess::Write, 
        FileOperation::kOpKAuthReadFile);
    return real_fopen(pathname, mode);
}

int access(const char *pathname, int mode) {
    GEN_REAL(int, access, const char *, int);
    report_probe(__func__, pathname);
    return real_access(pathname, mode);
}

int faccessat(int dirfd, const char *pathname, int mode, int flags) {
    GEN_REAL(int, faccessat, int, const char *, int, int);
    report_probe_at(__func__, dirfd, pathname);
    return real_faccessat(dirfd, pathname, mode, flags);
}

int open(const char *path, int oflag, ...) {
    GEN_REAL(int, open, const char *, int, mode_t);

    va_list args;
    va_start(args, oflag);
    mode_t mode = va_arg(args, mode_t);
    va_end(args);

    report_access(__func__, path, oflag_to_access(oflag), FileOperation::kOpKAuthReadFile);
    return real_open(path, oflag, mode);
}

int creat(const char *pathname, mode_t mode) {
    GEN_REAL(int, creat, const char *, mode_t);
    report_write(__func__, pathname);
    return real_creat(pathname, mode);
}

int openat(int dirfd, const char *pathname, int flags, ...) {
    GEN_REAL(int, openat, int, const char *, int, mode_t);

    va_list args;
    va_start(args, flags);
    mode_t mode = va_arg(args, mode_t);
    va_end(args);

    report_access_at(__func__, dirfd, pathname, oflag_to_access(flags), FileOperation::kOpKAuthReadFile);
    return real_openat(dirfd, pathname, flags, mode);
}

int remove(const char *pathname) {
    GEN_REAL(int, remove, const char *);
    report_write(__func__, pathname);
    return real_remove(pathname);
}

int rename(const char *old, const char *n) {
    GEN_REAL(int, rename, const char *, const char *);
    report_read(__func__, old);
    report_write(__func__, n);
    return real_rename(old, n);
}

int link(const char *path1, const char *path2) {
    GEN_REAL(int, link, const char *, const char *);
    report_read(__func__, path1);
    report_write(__func__, path2);
    return real_link(path1, path2);
}

int linkat(int fd1, const char *name1, int fd2, const char *name2, int flag) {
    GEN_REAL(int, linkat, int, const char *, int, const char *, int);
    report_read_at(__func__, fd1, name1);
    report_write_at(__func__, fd2, name2);
    return real_linkat(fd1, name1, fd2, name2, flag);
}

int unlink(const char *path) {
    GEN_REAL(int, unlink, const char *);
    report_write(__func__, path);
    return real_unlink(path);
}

int symlink(const char *path1, const char *path2) {
    GEN_REAL(int, symlink, const char *, const char *);
    report_write(__func__, path2);
    return real_symlink(path1, path2);
}

int symlinkat(const char *name1, int fd, const char *name2) {
    GEN_REAL(int, symlinkat, const char *, int, const char *);
    report_write_at(__func__, fd, name2);
    return real_symlinkat(name1, fd, name2);
}

ssize_t readlink(const char *path, char *buf, size_t bufsize) {
    GEN_REAL(ssize_t, readlink, const char *, char *, size_t);
    report_read(__func__, path);
    return real_readlink(path, buf, bufsize);
}

ssize_t readlinkat(int fd, const char *path, char *buf, size_t bufsize) {
    GEN_REAL(ssize_t, readlinkat, int, const char *, char *, size_t);
    report_read_at(__func__, fd, path);
    return real_readlinkat(fd, path, buf, bufsize);
}

DIR* opendir(const char *name) {
    GEN_REAL(DIR*, opendir, const char*);
    report_read(__func__, name);
    return real_opendir(name);
}

static inline void report_exit()
{
    report_access("atexit", "", RequestedAccess::None, kOpProcessExit);
}

void __attribute__ ((constructor)) _bxl_linux_sandbox_init(void)
{
   atexit(report_exit);
}
