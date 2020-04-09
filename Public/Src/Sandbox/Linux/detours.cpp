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

#include "bxl_observer.hpp"
#include "IOHandler.hpp"
#include "EventProcessor.hpp"

static ssize_t fd_to_path(int fd, char *buf, size_t bufsiz)
{
    GEN_REAL(ssize_t, readlink, const char *, char *, size_t);

    char procPath[100] = {0};
    sprintf(procPath, "/proc/self/fd/%d", fd);
    return real_readlink(procPath, buf, bufsiz);
}

static std::string normalize_path_at(int dirfd, const char *pathname)
{
    // TODO: remove since it's being done in Send
    GEN_REAL(char*, realpath, const char*, char*);

    char fullpath[PATH_MAX] = {0};
    char finalPath[PATH_MAX] = {0};
    ssize_t len = 0;

    // no pathname given --> read path for dirfd
    if (pathname == NULL)
    {
        fd_to_path(dirfd, fullpath, PATH_MAX);
        return fullpath;
    }
    // if relative path --> resolve it against dirfd
    else if (*pathname != '/' && *pathname != '~')
    {
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
        char *result = real_realpath(fullpath, finalPath);
        return result != NULL ? result : fullpath;
    }
    else
    {
        char *result = real_realpath(pathname, finalPath);
        return result != NULL ? result : pathname;
    }
}

static std::string normalize_path(const char *pathname)
{
    return normalize_path_at(AT_FDCWD, pathname);
}

static std::string normalize_fd(int fd)
{
    return normalize_path_at(fd, NULL);
}

static bool report_access(const char *syscallName, IOEvent &event);

static bool report_access(const char *syscallName, es_event_type_t eventType, std::string reportPath, std::string secondPath)
{
    GEN_REAL(int, __lxstat, int, const char *, struct stat *);
    GEN_REAL(ssize_t, readlink, const char *, char *, size_t);

    // TODO: don't stat all the time
    struct stat s;
    mode_t mode = real___lxstat(1, reportPath.c_str(), &s) == 0
        ? s.st_mode
        : 0;

    std::string execPath = eventType == ES_EVENT_TYPE_NOTIFY_EXEC
        ? reportPath
        : std::string(BxlObserver::GetInstance()->GetProgramPath());

    IOEvent event(getpid(), 0, getppid(), eventType, reportPath, secondPath, execPath, mode, false);
    return report_access(syscallName, event);
}

static bool report_access(const char *syscallName, IOEvent &event)
{
    es_event_type_t eventType = event.GetEventType();

    BxlObserver *bxl_observer = BxlObserver::GetInstance();
    IOHandler handler(bxl_observer->GetSandbox());
    handler.SetProcess(bxl_observer->GetProcess());

    // TODO: this should return AccessCheckResult, which should be returned from here
    handler.HandleEvent(event);

    return true;
}

static bool report_access(const char *syscallName, es_event_type_t eventType, const char *pathname, const char *otherPath = NULL)
{
    std::string reportPath = normalize_path(pathname);
    std::string secondPath = otherPath != NULL
        ? normalize_path(otherPath)
        : "";
    return report_access(syscallName, eventType, reportPath, secondPath);
}

static bool report_access_fd(const char *syscallName, es_event_type_t eventType, int fd)
{
    char fullpath[PATH_MAX] = {0};
    fd_to_path(fd, fullpath, PATH_MAX);

    return report_access(syscallName, eventType, fullpath);
}

static bool report_access_at(const char *syscallName, es_event_type_t eventType, int dirfd, const char *pathname)
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
    return report_access(syscallName, eventType, fullpath);
}

static enum RequestedAccess oflag_to_access(int oflag)
{
    return oflag & (O_WRONLY | O_RDWR) ? RequestedAccess::Write : RequestedAccess::Read;
}

pid_t fork(void)
{
    GEN_REAL(pid_t, fork);
    GEN_REAL(ssize_t, readlink, const char *, char *, size_t);

    pid_t childPid = real_fork();

    // report fork only when we are in the parent process
    if (childPid > 0)
    {
        std::string exePath(BxlObserver::GetInstance()->GetProgramPath());
        IOEvent event(getpid(), childPid, getppid(), ES_EVENT_TYPE_NOTIFY_FORK, exePath, std::string(""), exePath, 0, false);
        report_access(__func__, event);
    }

    return childPid;
}

int fexecve(int fd, char *const argv[], char *const envp[]) {
    GEN_REAL(int, fexecve, int, char *const[], char *const[])
    report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_EXEC, fd);
    return real_fexecve(fd, argv, envp);
}

int execv(const char *file, char *const argv[]) {
    GEN_REAL(int, execv, const char *, char *const[])
    report_access(__func__, ES_EVENT_TYPE_NOTIFY_EXEC, file);
    return real_execv(file, argv);
}

int execve(const char *file, char *const argv[], char *const envp[]) {
    GEN_REAL(int, execve, const char *, char *const[], char *const[])
    report_access(__func__, ES_EVENT_TYPE_NOTIFY_EXEC, file);
    return real_execve(file, argv, envp);
}

int execvp(const char *file, char *const argv[]) {
    GEN_REAL(int, execvp, const char *, char *const[])
    report_access(__func__, ES_EVENT_TYPE_NOTIFY_EXEC, std::string(file), std::string(""));
    return real_execvp(file, argv);
}

int execvpe(const char *file, char *const argv[], char *const envp[]) {
    GEN_REAL(int, execvpe, const char *, char *const[], char *const[])
    report_access(__func__, ES_EVENT_TYPE_NOTIFY_EXEC, std::string(file), std::string(""));
    return real_execvpe(file, argv, envp);
}

int __fxstat(int __ver, int fd, struct stat *__stat_buf) {
    GEN_REAL(int, __fxstat, int, int, struct stat*);
    report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_STAT, fd);
    return real___fxstat(__ver, fd, __stat_buf);
}

int statfs(const char *pathname, struct statfs *buf) {
    GEN_REAL(int, statfs, const char *, struct statfs *);
    report_access(__func__, ES_EVENT_TYPE_NOTIFY_STAT, pathname);
    return real_statfs(pathname, buf);
}

int __xstat(int __ver, const char *pathname, struct stat *buf) {
    GEN_REAL(int, __xstat, int, const char *, struct stat *);
    report_access(__func__, ES_EVENT_TYPE_NOTIFY_STAT, pathname);
    return real___xstat(__ver, pathname, buf);
}

int __xstat64(int __ver, const char *pathname, struct stat64 *buf)
{
    GEN_REAL(int, __xstat64, int, const char*, struct stat64*);
    report_access(__func__, ES_EVENT_TYPE_NOTIFY_STAT, pathname);
    return real___xstat64(__ver, pathname, buf);
}

int __lxstat64(int __ver, const char *pathname, struct stat64 *buf)
{
    GEN_REAL(int, __lxstat64, int, const char*, struct stat64*);
    report_access(__func__, ES_EVENT_TYPE_NOTIFY_STAT, pathname);
    return real___lxstat64(__ver, pathname, buf);
}

int __fxstat64(int __ver, int fd, struct stat64 *buf)
{
    GEN_REAL(int, __fxstat64, int, int, struct stat64*);
    report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_STAT, fd);
    return real___fxstat64(__ver, fd, buf);
}

int __lxstat(int __ver, const char *pathname, struct stat *buf) {
    GEN_REAL(int, __lxstat, int, const char *, struct stat *);
    report_access(__func__, ES_EVENT_TYPE_NOTIFY_STAT, pathname);
    return real___lxstat(__ver, pathname, buf);
}

FILE* fopen(const char *pathname, const char *mode) {
    GEN_REAL(FILE*, fopen, const char *, const char *);
    report_access(__func__, ES_EVENT_TYPE_NOTIFY_OPEN, pathname);
    return real_fopen(pathname, mode);
}

int access(const char *pathname, int mode) {
    GEN_REAL(int, access, const char *, int);
    report_access(__func__, ES_EVENT_TYPE_NOTIFY_ACCESS, pathname);
    return real_access(pathname, mode);
}

int faccessat(int dirfd, const char *pathname, int mode, int flags) {
    GEN_REAL(int, faccessat, int, const char *, int, int);
    report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_ACCESS, dirfd, pathname);
    return real_faccessat(dirfd, pathname, mode, flags);
}

int open(const char *path, int oflag, ...) {
    GEN_REAL(int, open, const char *, int, mode_t);

    va_list args;
    va_start(args, oflag);
    mode_t mode = va_arg(args, mode_t);
    va_end(args);

    report_access(__func__, ES_EVENT_TYPE_NOTIFY_OPEN, path);
    return real_open(path, oflag, mode);
}

int creat(const char *pathname, mode_t mode) {
    GEN_REAL(int, creat, const char *, mode_t);
    report_access(__func__, ES_EVENT_TYPE_NOTIFY_CREATE, pathname);
    return real_creat(pathname, mode);
}

int openat(int dirfd, const char *pathname, int flags, ...) {
    GEN_REAL(int, openat, int, const char *, int, mode_t);

    va_list args;
    va_start(args, flags);
    mode_t mode = va_arg(args, mode_t);
    va_end(args);

    report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_OPEN, dirfd, pathname);
    return real_openat(dirfd, pathname, flags, mode);
}

ssize_t write(int fd, const void *buf, size_t bufsiz)
{
    GEN_REAL(ssize_t, write, int, const void*, size_t);
    report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, fd);
    return real_write(fd, buf, bufsiz);
}

int remove(const char *pathname) {
    GEN_REAL(int, remove, const char *);
    report_access(__func__, ES_EVENT_TYPE_NOTIFY_UNLINK, pathname);
    return real_remove(pathname);
}

int rename(const char *old, const char *n) {
    GEN_REAL(int, rename, const char *, const char *);
    report_access(__func__, ES_EVENT_TYPE_NOTIFY_RENAME, old, n);
    return real_rename(old, n);
}

int link(const char *path1, const char *path2) {
    GEN_REAL(int, link, const char *, const char *);
    report_access(__func__, ES_EVENT_TYPE_NOTIFY_LINK, path1, path2);
    return real_link(path1, path2);
}

int linkat(int fd1, const char *name1, int fd2, const char *name2, int flag) {
    GEN_REAL(int, linkat, int, const char *, int, const char *, int);
    report_access(__func__, ES_EVENT_TYPE_NOTIFY_LINK, normalize_path_at(fd1, name1), normalize_path_at(fd2, name2));
    return real_linkat(fd1, name1, fd2, name2, flag);
}

int unlink(const char *path) {
    GEN_REAL(int, unlink, const char *);
    report_access(__func__, ES_EVENT_TYPE_NOTIFY_UNLINK, path);
    return real_unlink(path);
}

int symlink(const char *path1, const char *path2) {
    GEN_REAL(int, symlink, const char *, const char *);
    report_access(__func__, ES_EVENT_TYPE_NOTIFY_CREATE, path2);
    return real_symlink(path1, path2);
}

int symlinkat(const char *name1, int fd, const char *name2) {
    GEN_REAL(int, symlinkat, const char *, int, const char *);
    report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_CREATE, fd, name2);
    return real_symlinkat(name1, fd, name2);
}

ssize_t readlink(const char *path, char *buf, size_t bufsize) {
    GEN_REAL(ssize_t, readlink, const char *, char *, size_t);
    report_access(__func__, ES_EVENT_TYPE_NOTIFY_READLINK, path);
    return real_readlink(path, buf, bufsize);
}

ssize_t readlinkat(int fd, const char *path, char *buf, size_t bufsize) {
    GEN_REAL(ssize_t, readlinkat, int, const char *, char *, size_t);
    report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_READLINK, fd, path);
    return real_readlinkat(fd, path, buf, bufsize);
}

DIR* opendir(const char *name) {
    GEN_REAL(DIR*, opendir, const char*);
    report_access(__func__, ES_EVENT_TYPE_NOTIFY_READDIR, name);
    return real_opendir(name);
}

int utimensat(int dirfd, const char *pathname, const struct timespec times[2], int flags)
{
    GEN_REAL(int, utimensat, int, const char*, const struct timespec[2], int);
    report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_SETTIME, dirfd, pathname);
    return real_utimensat(dirfd, pathname, times, flags);
}

int futimens(int fd, const struct timespec times[2])
{
    GEN_REAL(int, futimens, int, const struct timespec[2]);
    report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_SETTIME, fd);
    return real_futimens(fd, times);
}

static inline void report_exit()
{
    report_access("atexit", ES_EVENT_TYPE_NOTIFY_EXIT, std::string(""), std::string(""));
}

void __attribute__ ((constructor)) _bxl_linux_sandbox_init(void)
{
   atexit(report_exit);
}

// ==========================

// having a main function is useful for various local testing
int main(int argc, char **argv)
{
    BxlObserver *inst = BxlObserver::GetInstance();
    printf("Path: %s\n", inst->GetReportsPath());

    GEN_REAL(int, fexecve, int, char *const[], char *const[])
    GEN_REAL(int, execv, const char *, char *const[])
    GEN_REAL(int, execve, const char *, char *const[], char *const[])
    GEN_REAL(int, execvp, const char *, char *const[])
    GEN_REAL(int, execvpe, const char *, char *const[], char *const[])
    GEN_REAL(int, __fxstat, int, int, struct stat*);
    GEN_REAL(int, statfs, const char *, struct statfs *);
    GEN_REAL(int, __xstat, int, const char *, struct stat *);
    GEN_REAL(int, __lxstat, int, const char *, struct stat *);
    GEN_REAL(int, __xstat64, int, const char*, struct stat64*);
    GEN_REAL(int, __lxstat64, int, const char*, struct stat64*);
    GEN_REAL(int, __fxstat64, int, const char*, struct stat64*);
    GEN_REAL(FILE*, fopen, const char *, const char *);
    GEN_REAL(int, access, const char *, int);
    GEN_REAL(int, faccessat, int, const char *, int, int);
    GEN_REAL(int, open, const char *, int, mode_t);
    GEN_REAL(int, creat, const char *, mode_t);
    GEN_REAL(int, openat, int, const char *, int, mode_t);
    GEN_REAL(ssize_t, write, int, const void*, size_t);
    GEN_REAL(int, remove, const char *);
    GEN_REAL(int, rename, const char *, const char *);
    GEN_REAL(int, link, const char *, const char *);
    GEN_REAL(int, linkat, int, const char *, int, const char *, int);
    GEN_REAL(int, unlink, const char *);
    GEN_REAL(int, symlink, const char *, const char *);
    GEN_REAL(int, symlinkat, const char *, int, const char *);
    GEN_REAL(ssize_t, readlink, const char *, char *, size_t);
    GEN_REAL(ssize_t, readlinkat, int, const char *, char *, size_t);
    GEN_REAL(DIR*, opendir, const char*);
    GEN_REAL(int, utimensat, int, const char*, const struct timespec[2], int);
}