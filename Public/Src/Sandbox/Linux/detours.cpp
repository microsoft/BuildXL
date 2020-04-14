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

#define ERROR_RETURN_VALUE -1

static std::string sEmptyStr("");

INTERPOSE(pid_t, fork, void)({
    result_t<pid_t> childPid = bxl->fwd_fork();

    // report fork only when we are in the parent process
    if (childPid.get() > 0)
    {
        std::string exePath(bxl->GetProgramPath());
        IOEvent event(getpid(), childPid.get(), getppid(), ES_EVENT_TYPE_NOTIFY_FORK, exePath, std::string(""), exePath, 0, false);
        bxl->report_access(__func__, event);
    }

    return childPid.restore();
})

INTERPOSE(int, fexecve, int fd, char *const argv[], char *const envp[])({
    bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_EXEC, fd);
    return bxl->real_fexecve(fd, argv, envp);
})

INTERPOSE(int, execv, const char *file, char *const argv[])({
    bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_EXEC, file);
    return bxl->real_execv(file, argv);
})

INTERPOSE(int, execve, const char *file, char *const argv[], char *const envp[])({
    bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_EXEC, file);
    return bxl->real_execve(file, argv, envp);
})

INTERPOSE(int, execvp, const char *file, char *const argv[])({
    bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_EXEC, std::string(file), std::string(""));
    return bxl->real_execvp(file, argv);
})

INTERPOSE(int, execvpe, const char *file, char *const argv[], char *const envp[])({
    bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_EXEC, std::string(file), std::string(""));
    return bxl->real_execvpe(file, argv, envp);
})

INTERPOSE(int, statfs, const char *pathname, struct statfs *buf)({
    result_t<int> result = bxl->fwd_statfs(pathname, buf);
    bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_STAT, pathname);
    return result.restore();
})

INTERPOSE(int, __fxstat, int __ver, int fd, struct stat *__stat_buf)({
    result_t<int> result = bxl->fwd___fxstat(__ver, fd, __stat_buf);
    bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_STAT, fd);
    return result.restore();
})

INTERPOSE(int, __fxstat64, int __ver, int fd, struct stat64 *buf)({
    result_t<int> result(bxl->fwd___fxstat64(__ver, fd, buf));
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_STAT, fd);
    return result.restore();
})

INTERPOSE(int, __fxstatat, int __ver, int fd, const char *pathname, struct stat *__stat_buf, int flag)({
    result_t<int> result = bxl->fwd___fxstatat(__ver, fd, pathname, __stat_buf, flag);
    bxl->report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_STAT, fd, pathname);
    return result.restore();
})

INTERPOSE(int, __fxstatat64, int __ver, int fd, const char *pathname, struct stat64 *buf, int flag)({
    result_t<int> result(bxl->fwd___fxstatat64(__ver, fd, pathname, buf, flag));
    auto check = bxl->report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_STAT, fd, pathname);
    return result.restore();
})

INTERPOSE(int, __xstat, int __ver, const char *pathname, struct stat *buf)({
    result_t<int> result = bxl->fwd___xstat(__ver, pathname, buf);
    bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_STAT, pathname);
    return result.restore();
})

INTERPOSE(int, __xstat64, int __ver, const char *pathname, struct stat64 *buf)({
    result_t<int> result(bxl->fwd___xstat64(__ver, pathname, buf));
    bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_STAT, pathname);
    return result.restore();
})

INTERPOSE(int, __lxstat, int __ver, const char *pathname, struct stat *buf)({
    result_t<int> result = bxl->fwd___lxstat(__ver, pathname, buf);
    bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_STAT, pathname);
    return result.restore();
})

INTERPOSE(int, __lxstat64, int __ver, const char *pathname, struct stat64 *buf)({
    result_t<int> result(bxl->fwd___lxstat64(__ver, pathname, buf));
    bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_STAT, pathname);
    return result.restore();
})

INTERPOSE(FILE*, fopen, const char *pathname, const char *mode)({
    result_t<FILE*> result = bxl->fwd_fopen(pathname, mode);
    bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_OPEN, pathname);
    return result.restore();
})

INTERPOSE(size_t, fread, void *ptr, size_t size, size_t nmemb, FILE *stream)({
    result_t<size_t> result = bxl->fwd_fread(ptr, size, nmemb, stream);
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_OPEN, fileno(stream));
    return bxl->restore_if_allowed(result, check, (size_t)0);
})

INTERPOSE(size_t, fwrite, const void *ptr, size_t size, size_t nmemb, FILE *stream)({
    result_t<size_t> result = bxl->fwd_fwrite(ptr, size, nmemb, stream);
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, fileno(stream));
    return bxl->restore_if_allowed(result, check, (size_t)0);
})

INTERPOSE(int, fputc, int c, FILE *stream)({
    result_t<int> result = bxl->real_fputc(c, stream);
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, fileno(stream));
    return bxl->restore_if_allowed(result, check, EOF);
})

INTERPOSE(int, fputs, const char *s, FILE *stream)({
    result_t<int> result = bxl->real_fputs(s, stream);
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, fileno(stream));
    return bxl->restore_if_allowed(result, check, EOF);
})

INTERPOSE(int, putc, int c, FILE *stream)({
    result_t<int> result = bxl->real_putc(c, stream);
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, fileno(stream));
    return bxl->restore_if_allowed(result, check, EOF);
})

INTERPOSE(int, putchar, int c)({
    result_t<int> result = bxl->real_putchar(c);
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, fileno(stdout));
    return bxl->restore_if_allowed(result, check, EOF);
})

INTERPOSE(int, puts, const char *s)({
    result_t<int> result = bxl->real_puts(s);
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, fileno(stdout));
    return bxl->restore_if_allowed(result, check, EOF);
})

INTERPOSE(int, access, const char *pathname, int mode)({
    result_t<int> result = bxl->fwd_access(pathname, mode);
    bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_ACCESS, pathname);
    return result.restore();
})

INTERPOSE(int, faccessat, int dirfd, const char *pathname, int mode, int flags)({
    result_t<int> result = bxl->fwd_faccessat(dirfd, pathname, mode, flags);
    bxl->report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_ACCESS, dirfd, pathname);
    return result.restore();
})

INTERPOSE(int, open, const char *path, int oflag, ...)({
    va_list args;
    va_start(args, oflag);
    mode_t mode = va_arg(args, mode_t);
    va_end(args);

    result_t<int> result = bxl->fwd_open(path, oflag, mode);
    bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_OPEN, path);
    return result.restore();
})

INTERPOSE(int, creat, const char *pathname, mode_t mode)({
    result_t<int> result = bxl->fwd_creat(pathname, mode);
    auto check = bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_CREATE, pathname);
    return bxl->restore_if_allowed(result, check, ERROR_RETURN_VALUE);
})

INTERPOSE(int, openat, int dirfd, const char *pathname, int flags, ...)({
    va_list args;
    va_start(args, flags);
    mode_t mode = va_arg(args, mode_t);
    va_end(args);

    result_t<int> result = bxl->fwd_openat(dirfd, pathname, flags, mode);
    bxl->report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_OPEN, dirfd, pathname);
    return result.restore();
})

INTERPOSE(ssize_t, write, int fd, const void *buf, size_t bufsiz)({
    result_t<ssize_t> result = bxl->fwd_write(fd, buf, bufsiz);
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, fd);
    return bxl->restore_if_allowed(result, check, (ssize_t)ERROR_RETURN_VALUE);
})

INTERPOSE(int, remove, const char *pathname)({
    result_t<int> result = bxl->fwd_remove(pathname);
    auto check = bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_UNLINK, pathname);
    return bxl->restore_if_allowed(result, check, ERROR_RETURN_VALUE);
})

INTERPOSE(int, rename, const char *old, const char *n)({
    result_t<int> result = bxl->fwd_rename(old, n);
    auto check = bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_RENAME, old, n);
    return bxl->restore_if_allowed(result, check, ERROR_RETURN_VALUE);
})

INTERPOSE(int, link, const char *path1, const char *path2)({
    result_t<int> result = bxl->fwd_link(path1, path2);
    auto check = bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_LINK, path1, path2);
    return bxl->restore_if_allowed(result, check, ERROR_RETURN_VALUE);
})

INTERPOSE(int, linkat, int fd1, const char *name1, int fd2, const char *name2, int flag)({
    result_t<int> result = bxl->fwd_linkat(fd1, name1, fd2, name2, flag);
    auto check = bxl->report_access(
        __func__,
        ES_EVENT_TYPE_NOTIFY_LINK,
        bxl->normalize_path_at(fd1, name1),
        bxl->normalize_path_at(fd2, name2));
    return bxl->restore_if_allowed(result, check, ERROR_RETURN_VALUE);
})

INTERPOSE(int, unlink, const char *path)({
    result_t<int> result = bxl->fwd_unlink(path);
    auto check = bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_UNLINK, path);
    return bxl->restore_if_allowed(result, check, ERROR_RETURN_VALUE);
})

INTERPOSE(int, symlink, const char *target, const char *linkPath)({
    result_t<int> result = bxl->fwd_symlink(target, linkPath);
    auto check = bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_CREATE, linkPath);
    return bxl->restore_if_allowed(result, check, ERROR_RETURN_VALUE);
})

INTERPOSE(int, symlinkat, const char *target, int dirfd, const char *linkPath)({
    result_t<int> result = bxl->fwd_symlinkat(target, dirfd, linkPath);
    auto check = bxl->report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_CREATE, dirfd, linkPath);
    return bxl->restore_if_allowed(result, check, ERROR_RETURN_VALUE);
})

INTERPOSE(ssize_t, readlink, const char *path, char *buf, size_t bufsize)({
    result_t<ssize_t> result = bxl->fwd_readlink(path, buf, bufsize);
    auto check = bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_READLINK, path);
    return bxl->restore_if_allowed(result, check, (ssize_t)ERROR_RETURN_VALUE);
})

INTERPOSE(ssize_t, readlinkat, int fd, const char *path, char *buf, size_t bufsize)({
    result_t<ssize_t> result = bxl->fwd_readlinkat(fd, path, buf, bufsize);
    auto check = bxl->report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_READLINK, fd, path);
    return bxl->restore_if_allowed(result, check, (ssize_t)ERROR_RETURN_VALUE);
})

INTERPOSE(DIR*, opendir, const char *name)({
    result_t<DIR*> result = bxl->fwd_opendir(name);
    auto check = bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_READDIR, name);
    return bxl->restore_if_allowed(result, check, (DIR*)NULL);
})

INTERPOSE(DIR*, fdopendir, int fd)({
    result_t<DIR*> result = bxl->fwd_fdopendir(fd);
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_READDIR, fd);
    return bxl->restore_if_allowed(result, check, (DIR*)NULL);
})

INTERPOSE(int, utimensat, int dirfd, const char *pathname, const struct timespec times[2], int flags)({
    result_t<int> result = bxl->fwd_utimensat(dirfd, pathname, times, flags);
    auto check = bxl->report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_SETTIME, dirfd, pathname);
    return bxl->restore_if_allowed(result, check, ERROR_RETURN_VALUE);
})

INTERPOSE(int, futimens, int fd, const struct timespec times[2])({
    result_t<int> result = bxl->fwd_futimens(fd, times);
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_SETTIME, fd);
    return bxl->restore_if_allowed(result, check, ERROR_RETURN_VALUE);
})

INTERPOSE(int, mkdir, const char *pathname, mode_t mode)({
    result_t<int> result = bxl->fwd_mkdir(pathname, mode);
    auto check = bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_CREATE, pathname);
    return bxl->restore_if_allowed(result, check, ERROR_RETURN_VALUE);
})

INTERPOSE(int, mkdirat, int dirfd, const char *pathname, mode_t mode)({
    result_t<int> result = bxl->fwd_mkdirat(dirfd, pathname, mode);
    auto check = bxl->report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_CREATE, dirfd, pathname);
    return bxl->restore_if_allowed(result, check, ERROR_RETURN_VALUE);
})

INTERPOSE(int, vprintf, const char *fmt, va_list args)({
    result_t<int> result = bxl->fwd_vprintf(fmt, args);
    bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, 1);
    return result.restore();
})

INTERPOSE(int, vfprintf, FILE *f, const char *fmt, va_list args)({
    result_t<int> result = bxl->fwd_vfprintf(f, fmt, args);
    bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, fileno(f));
    return result.restore();
})

INTERPOSE(int, vdprintf, int fd, const char *fmt, va_list args)({
    result_t<int> result = bxl->fwd_vdprintf(fd, fmt, args);
    bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, fd);
    return result.restore();
})

INTERPOSE(int, printf, const char *fmt, ...)({
    va_list args;
    va_start(args, fmt);
    result_t<int> result = vprintf(fmt, args);
    va_end(args);
    return result.restore();
})

INTERPOSE(int, fprintf, FILE *f, const char *fmt, ...)({
    va_list args;
    va_start(args, fmt);
    result_t<int> result = vfprintf(f, fmt, args);
    va_end(args);
    return result.restore();
})

INTERPOSE(int, dprintf, int fd, const char *fmt, ...)({
    va_list args;
    va_start(args, fmt);
    result_t<int> result = vdprintf(fd, fmt, args);
    va_end(args);
    return result.restore();
})

// TODO: temporarily interposing syscalls not needed for access checking but useful for tracing
INTERPOSE(int, close, int fd)             ({ return bxl->fwd_close(fd).restore(); })
INTERPOSE(int, fclose, FILE *f)           ({ return bxl->fwd_fclose(f).restore(); })
INTERPOSE(int, dup, int fd)               ({ return bxl->fwd_dup(fd).restore(); })
INTERPOSE(int, dup2, int oldfd, int newfd)({ return bxl->fwd_dup2(oldfd, newfd).restore(); })

static void report_exit()
{
    BxlObserver::GetInstance()->report_access("atexit", ES_EVENT_TYPE_NOTIFY_EXIT, std::string(""), std::string(""));
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