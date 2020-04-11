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

static BxlObserver *g_bxl = BxlObserver::GetInstance();

INTERPOSE(pid_t, fork, void) 
{
    result_t<pid_t> childPid = g_bxl->real_fork();

    // report fork only when we are in the parent process
    if (childPid > 0)
    {
        std::string exePath(g_bxl->GetProgramPath());
        IOEvent event(getpid(), childPid, getppid(), ES_EVENT_TYPE_NOTIFY_FORK, exePath, std::string(""), exePath, 0, false);
        g_bxl->report_access(__func__, event);
    }

    return childPid;
}

INTERPOSE(int, fexecve, int fd, char *const argv[], char *const envp[])
{
    g_bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_EXEC, fd);
    return g_bxl->real_fexecve(fd, argv, envp);
}

INTERPOSE(int, execv, const char *file, char *const argv[])
{
    g_bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_EXEC, file);
    return g_bxl->real_execv(file, argv);
}

INTERPOSE(int, execve, const char *file, char *const argv[], char *const envp[])
{
    g_bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_EXEC, file);
    return g_bxl->real_execve(file, argv, envp);
}

INTERPOSE(int, execvp, const char *file, char *const argv[])
{
    g_bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_EXEC, std::string(file), std::string(""));
    return g_bxl->real_execvp(file, argv);
}

INTERPOSE(int, execvpe, const char *file, char *const argv[], char *const envp[])
{
    g_bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_EXEC, std::string(file), std::string(""));
    return g_bxl->real_execvpe(file, argv, envp);
}

INTERPOSE(int, __fxstat, int __ver, int fd, struct stat *__stat_buf)
{
    result_t<int> result = g_bxl->real___fxstat(__ver, fd, __stat_buf);
    g_bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_STAT, fd);
    return result;
}

INTERPOSE(int, statfs, const char *pathname, struct statfs *buf)
{
    result_t<int> result = g_bxl->real_statfs(pathname, buf);
    g_bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_STAT, pathname);
    return result;
}

INTERPOSE(int, __xstat, int __ver, const char *pathname, struct stat *buf)
{
    result_t<int> result = g_bxl->real___xstat(__ver, pathname, buf);
    g_bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_STAT, pathname);
    return result;
}

INTERPOSE(int, __xstat64, int __ver, const char *pathname, struct stat64 *buf)
{
    result_t<int> result(g_bxl->real___xstat64(__ver, pathname, buf));
    g_bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_STAT, pathname);
    return result;
}

INTERPOSE(int, __lxstat64, int __ver, const char *pathname, struct stat64 *buf)
{
    result_t<int> result(g_bxl->real___lxstat64(__ver, pathname, buf));
    g_bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_STAT, pathname);
    return result;
}

INTERPOSE(int, __fxstat64, int __ver, int fd, struct stat64 *buf)
{
    result_t<int> result(g_bxl->real___fxstat64(__ver, fd, buf));
    g_bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_STAT, fd);
    return result;
}

INTERPOSE(int, __lxstat, int __ver, const char *pathname, struct stat *buf)
{
    result_t<int> result = g_bxl->real___lxstat(__ver, pathname, buf);
    g_bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_STAT, pathname);
    return result; 
}

INTERPOSE(FILE*, fopen, const char *pathname, const char *mode)
{
    result_t<FILE*> result = g_bxl->real_fopen(pathname, mode);
    g_bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_OPEN, pathname);
    return result;
}

INTERPOSE(int, access, const char *pathname, int mode)
{
    result_t<int> result = g_bxl->real_access(pathname, mode);
    g_bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_ACCESS, pathname);
    return result;
}

INTERPOSE(int, faccessat, int dirfd, const char *pathname, int mode, int flags)
{
    result_t<int> result = g_bxl->real_faccessat(dirfd, pathname, mode, flags);
    g_bxl->report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_ACCESS, dirfd, pathname);
    return result;
}

INTERPOSE(int, open, const char *path, int oflag, ...)
{
    va_list args;
    va_start(args, oflag);
    mode_t mode = va_arg(args, mode_t);
    va_end(args);

    result_t<int> result = g_bxl->real_open(path, oflag, mode);
    g_bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_OPEN, path);
    return result;
}

INTERPOSE(int, creat, const char *pathname, mode_t mode)
{
    result_t<int> result = g_bxl->real_creat(pathname, mode);
    g_bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_CREATE, pathname);
    return result;
}

INTERPOSE(int, openat, int dirfd, const char *pathname, int flags, ...)
{
    va_list args;
    va_start(args, flags);
    mode_t mode = va_arg(args, mode_t);
    va_end(args);

    result_t<int> result = g_bxl->real_openat(dirfd, pathname, flags, mode);
    g_bxl->report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_OPEN, dirfd, pathname);
    return result;
}

INTERPOSE(ssize_t, write, int fd, const void *buf, size_t bufsiz)
{
    result_t<ssize_t> result = g_bxl->real_write(fd, buf, bufsiz);
    g_bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, fd);
    return result;
}

INTERPOSE(int, remove, const char *pathname)
{
    result_t<int> result = g_bxl->real_remove(pathname);
    g_bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_UNLINK, pathname);
    return result;
}

INTERPOSE(int, rename, const char *old, const char *n)
{
    result_t<int> result = g_bxl->real_rename(old, n);
    g_bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_RENAME, old, n);
    return result;
}

INTERPOSE(int, link, const char *path1, const char *path2)
{
    result_t<int> result = g_bxl->real_link(path1, path2);
    g_bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_LINK, path1, path2);
    return result;
}

INTERPOSE(int, linkat, int fd1, const char *name1, int fd2, const char *name2, int flag)
{
    result_t<int> result = g_bxl->real_linkat(fd1, name1, fd2, name2, flag);
    g_bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_LINK, g_bxl->normalize_path_at(fd1, name1), g_bxl->normalize_path_at(fd2, name2));
    return result;
}

INTERPOSE(int, unlink, const char *path)
{
    result_t<int> result = g_bxl->real_unlink(path);
    g_bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_UNLINK, path);
    return result;
}

INTERPOSE(int, symlink, const char *path1, const char *path2)
{
    result_t<int> result = g_bxl->real_symlink(path1, path2);
    g_bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_CREATE, path2);
    return result;
}

INTERPOSE(int, symlinkat, const char *name1, int fd, const char *name2)
{
    result_t<int> result = g_bxl->real_symlinkat(name1, fd, name2);
    g_bxl->report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_CREATE, fd, name2);
    return result;
}

INTERPOSE(ssize_t, readlink, const char *path, char *buf, size_t bufsize)
{
    result_t<int> result = g_bxl->real_readlink(path, buf, bufsize);
    g_bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_READLINK, path);
    return result;
}

INTERPOSE(ssize_t, readlinkat, int fd, const char *path, char *buf, size_t bufsize)
{
    result_t<ssize_t> result = g_bxl->real_readlinkat(fd, path, buf, bufsize);
    g_bxl->report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_READLINK, fd, path);
    return result;
}

INTERPOSE(DIR*, opendir, const char *name)
{
    result_t<DIR*> result = g_bxl->real_opendir(name);
    g_bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_READDIR, name);
    return result;
}

INTERPOSE(int, utimensat, int dirfd, const char *pathname, const struct timespec times[2], int flags)
{
    result_t<int> result = g_bxl->real_utimensat(dirfd, pathname, times, flags);
    g_bxl->report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_SETTIME, dirfd, pathname);
    return result;
}

INTERPOSE(int, futimens, int fd, const struct timespec times[2])
{
    result_t<int> result = g_bxl->real_futimens(fd, times);
    g_bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_SETTIME, fd);
    return result;
}

static void report_exit()
{
    g_bxl->report_access("atexit", ES_EVENT_TYPE_NOTIFY_EXIT, std::string(""), std::string(""));
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
}