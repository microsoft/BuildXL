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
#include <gnu/lib-names.h>
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

INTERPOSE(void, _exit, int status)({
    bxl->report_access("_exit", ES_EVENT_TYPE_NOTIFY_EXIT, std::string(""), std::string(""));
    bxl->real__exit(status);
    _exit(status);
})

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
    return bxl->fwd_fexecve(fd, argv, envp).restore();
})

INTERPOSE(int, execv, const char *file, char *const argv[])({
    bxl->report_exec(__func__, argv[0], file);
    return bxl->fwd_execv(file, argv).restore();
})

INTERPOSE(int, execve, const char *file, char *const argv[], char *const envp[])({
    bxl->report_exec(__func__, argv[0], file);
    return bxl->fwd_execve(file, argv, envp).restore();
})

INTERPOSE(int, execvp, const char *file, char *const argv[])({
    bxl->report_exec(__func__, argv[0], file);
    return bxl->fwd_execvp(file, argv).restore();
})

INTERPOSE(int, execvpe, const char *file, char *const argv[], char *const envp[])({
    bxl->report_exec(__func__, argv[0], file);
    return bxl->fwd_execvpe(file, argv, envp).restore();
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
    bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_STAT, pathname, O_NOFOLLOW);
    return result.restore();
})

INTERPOSE(int, __lxstat64, int __ver, const char *pathname, struct stat64 *buf)({
    result_t<int> result(bxl->fwd___lxstat64(__ver, pathname, buf));
    bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_STAT, pathname, O_NOFOLLOW);
    return result.restore();
})

INTERPOSE(FILE*, fopen, const char *pathname, const char *mode)({
    auto check = bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_OPEN, pathname);
    return bxl->check_and_fwd_fopen(check, (FILE*)NULL, pathname, mode);
})

INTERPOSE(size_t, fread, void *ptr, size_t size, size_t nmemb, FILE *stream)({
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_OPEN, fileno(stream));
    return bxl->check_and_fwd_fread(check, (size_t)0, ptr, size, nmemb, stream);
})

INTERPOSE(size_t, fwrite, const void *ptr, size_t size, size_t nmemb, FILE *stream)({
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, fileno(stream));
    return bxl->check_and_fwd_fwrite(check, (size_t)0, ptr, size, nmemb, stream);
})

INTERPOSE(int, fputc, int c, FILE *stream)({
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, fileno(stream));
    return bxl->check_and_fwd_fputc(check, ERROR_RETURN_VALUE, c, stream);
})

INTERPOSE(int, fputs, const char *s, FILE *stream)({
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, fileno(stream));
    return bxl->check_and_fwd_fputs(check, ERROR_RETURN_VALUE, s, stream);
})

INTERPOSE(int, putc, int c, FILE *stream)({
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, fileno(stream));
    return bxl->check_and_fwd_putc(check, ERROR_RETURN_VALUE, c, stream);
})

INTERPOSE(int, putchar, int c)({
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, fileno(stdout));
    return bxl->check_and_fwd_putchar(check, ERROR_RETURN_VALUE, c);
})

INTERPOSE(int, puts, const char *s)({
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, fileno(stdout));
    return bxl->check_and_fwd_puts(check, ERROR_RETURN_VALUE, s);
})

INTERPOSE(int, access, const char *pathname, int mode)({
    auto check = bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_ACCESS, pathname);
    return bxl->check_and_fwd_access(check, ERROR_RETURN_VALUE, pathname, mode);
})

INTERPOSE(int, faccessat, int dirfd, const char *pathname, int mode, int flags)({
    auto check = bxl->report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_ACCESS, dirfd, pathname);
    return bxl->check_and_fwd_faccessat(check, ERROR_RETURN_VALUE, dirfd, pathname, mode, flags);
})

INTERPOSE(int, open, const char *path, int oflag, ...)({
    va_list args;
    va_start(args, oflag);
    mode_t mode = va_arg(args, mode_t);
    va_end(args);

    std::string pathStr = bxl->normalize_path(path);
    mode_t pathMode = bxl->get_mode(pathStr.c_str());
    IOEvent event(
        pathMode == 0 && (oflag & (O_CREAT|O_TRUNC)) ? ES_EVENT_TYPE_NOTIFY_CREATE : ES_EVENT_TYPE_NOTIFY_OPEN, 
        pathStr, bxl->GetProgramPath(), pathMode, false);
    auto check = bxl->report_access(__func__, event);

    return bxl->check_and_fwd_open(check, ERROR_RETURN_VALUE, path, oflag, mode);
})

INTERPOSE(int, openat, int dirfd, const char *pathname, int flags, ...)({
    va_list args;
    va_start(args, flags);
    mode_t mode = va_arg(args, mode_t);
    va_end(args);

    std::string pathStr = bxl->normalize_path_at(dirfd, pathname);
    mode_t pathMode = bxl->get_mode(pathStr.c_str());
    IOEvent event(
        pathMode == 0 && (flags & (O_CREAT|O_TRUNC)) ? ES_EVENT_TYPE_NOTIFY_CREATE : ES_EVENT_TYPE_NOTIFY_OPEN, 
        pathStr, bxl->GetProgramPath(), pathMode, false);
    auto check = bxl->report_access(__func__, event);

    return bxl->check_and_fwd_openat(check, ERROR_RETURN_VALUE, dirfd, pathname, flags, mode);
})

INTERPOSE(int, creat, const char *pathname, mode_t mode)({
    return open(pathname, O_CREAT | O_WRONLY | O_TRUNC);
})

INTERPOSE(ssize_t, write, int fd, const void *buf, size_t bufsiz)({
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, fd);
    return bxl->check_and_fwd_write(check, (ssize_t)ERROR_RETURN_VALUE, fd, buf, bufsiz);
})

INTERPOSE(int, remove, const char *pathname)({
    auto check = bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_UNLINK, pathname, O_NOFOLLOW);
    return bxl->check_and_fwd_remove(check, ERROR_RETURN_VALUE, pathname);
})

INTERPOSE(int, rename, const char *old, const char *n)({
    std::string oldStr = bxl->normalize_path(old, O_NOFOLLOW);
    std::string newStr = bxl->normalize_path(n, O_NOFOLLOW);

    mode_t mode = bxl->get_mode(oldStr.c_str());
    IOEvent event(ES_EVENT_TYPE_NOTIFY_RENAME, oldStr, bxl->GetProgramPath(), mode, false, newStr);

    // special case for 'rename' must check before forwarding the call and report after 
    // (so that bxl can properly rename all files inside the renamed directories)
    auto check = bxl->report_access(__func__, event); // TODO: this step should only check permission without reporting anything if allowed

    result_t<int> result = bxl->check_and_fwd_rename(check, ERROR_RETURN_VALUE, old, n);

    // if allowed and 'old' is a directory --> report again so that bxl can translate accesses to renamed files
    if (S_ISDIR(mode) && result.get() != -1)
    {
        bxl->report_access(__func__, event);
    }

    return result.restore();
})

INTERPOSE(int, link, const char *path1, const char *path2)({
    auto check = bxl->report_access(
        __func__,
        ES_EVENT_TYPE_NOTIFY_LINK,
        bxl->normalize_path(path1, O_NOFOLLOW),
        bxl->normalize_path(path2, O_NOFOLLOW));
    return bxl->check_and_fwd_link(check, ERROR_RETURN_VALUE, path1, path2);
})

INTERPOSE(int, linkat, int fd1, const char *name1, int fd2, const char *name2, int flag)({
    auto check = bxl->report_access(
        __func__,
        ES_EVENT_TYPE_NOTIFY_LINK,
        bxl->normalize_path_at(fd1, name1, O_NOFOLLOW),
        bxl->normalize_path_at(fd2, name2, O_NOFOLLOW));
    return bxl->check_and_fwd_linkat(check, ERROR_RETURN_VALUE, fd1, name1, fd2, name2, flag);
})

INTERPOSE(int, unlink, const char *path)({
    auto check = bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_UNLINK, path, O_NOFOLLOW);
    return bxl->check_and_fwd_unlink(check, ERROR_RETURN_VALUE, path);
})

INTERPOSE(int, symlink, const char *target, const char *linkPath)({
    IOEvent event(ES_EVENT_TYPE_NOTIFY_CREATE, bxl->normalize_path(linkPath, O_NOFOLLOW), bxl->GetProgramPath(), S_IFLNK);
    auto check = bxl->report_access(__func__, event);
    return bxl->check_and_fwd_symlink(check, ERROR_RETURN_VALUE, target, linkPath);
})

INTERPOSE(int, symlinkat, const char *target, int dirfd, const char *linkPath)({
    IOEvent event(ES_EVENT_TYPE_NOTIFY_CREATE, bxl->normalize_path_at(dirfd, linkPath, O_NOFOLLOW), bxl->GetProgramPath(), S_IFLNK);
    auto check = bxl->report_access(__func__, event);
    return bxl->check_and_fwd_symlinkat(check, ERROR_RETURN_VALUE, target, dirfd, linkPath);
})

INTERPOSE(ssize_t, readlink, const char *path, char *buf, size_t bufsize)({
    auto check = bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_READLINK, path, O_NOFOLLOW);
    return bxl->check_and_fwd_readlink(check, (ssize_t)ERROR_RETURN_VALUE, path, buf, bufsize);
})

INTERPOSE(ssize_t, readlinkat, int fd, const char *path, char *buf, size_t bufsize)({
    auto check = bxl->report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_READLINK, fd, path, O_NOFOLLOW);
    return bxl->check_and_fwd_readlinkat(check, (ssize_t)ERROR_RETURN_VALUE, fd, path, buf, bufsize);
})

INTERPOSE(DIR*, opendir, const char *name)({
    auto check = bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_READDIR, name);
    return bxl->check_and_fwd_opendir(check, (DIR*)NULL, name);
})

INTERPOSE(DIR*, fdopendir, int fd)({
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_READDIR, fd);
    return bxl->check_and_fwd_fdopendir(check, (DIR*)NULL, fd);
})

INTERPOSE(int, utimensat, int dirfd, const char *pathname, const struct timespec times[2], int flags)({
    auto check = bxl->report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_SETTIME, dirfd, pathname);
    return bxl->check_and_fwd_utimensat(check, ERROR_RETURN_VALUE, dirfd, pathname, times, flags);
})

INTERPOSE(int, futimens, int fd, const struct timespec times[2])({
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_SETTIME, fd);
    return bxl->check_and_fwd_futimens(check, ERROR_RETURN_VALUE, fd, times);
})

INTERPOSE(int, mkdir, const char *pathname, mode_t mode)({
    IOEvent event(ES_EVENT_TYPE_NOTIFY_CREATE, bxl->normalize_path(pathname), bxl->GetProgramPath(), S_IFDIR);
    auto check = bxl->report_access(__func__, event);
    return bxl->check_and_fwd_mkdir(check, ERROR_RETURN_VALUE, pathname, mode);
})

INTERPOSE(int, mkdirat, int dirfd, const char *pathname, mode_t mode)({
    IOEvent event(ES_EVENT_TYPE_NOTIFY_CREATE, bxl->normalize_path_at(dirfd, pathname), bxl->GetProgramPath(), S_IFDIR);
    auto check = bxl->report_access(__func__, event);
    return bxl->check_and_fwd_mkdirat(check, ERROR_RETURN_VALUE, dirfd, pathname, mode);
})

INTERPOSE(int, vprintf, const char *fmt, va_list args)({
    bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, 1);
    return bxl->fwd_vprintf(fmt, args).restore();
})

INTERPOSE(int, vfprintf, FILE *f, const char *fmt, va_list args)({
    bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, fileno(f));
    return bxl->fwd_vfprintf(f, fmt, args).restore();
})

INTERPOSE(int, vdprintf, int fd, const char *fmt, va_list args)({
    bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_WRITE, fd);
    return bxl->fwd_vdprintf(fd, fmt, args).restore();
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

INTERPOSE(int, chmod, const char *pathname, mode_t mode)({
    auto check = bxl->report_access(__func__, ES_EVENT_TYPE_NOTIFY_SETMODE, pathname);
    return bxl->check_and_fwd_chmod(check, ERROR_RETURN_VALUE, pathname, mode);
})

INTERPOSE(int, fchmod, int fd, mode_t mode)({
    auto check = bxl->report_access_fd(__func__, ES_EVENT_TYPE_NOTIFY_SETMODE, fd);
    return bxl->check_and_fwd_fchmod(check, ERROR_RETURN_VALUE, fd, mode);
})

INTERPOSE(int, fchmodat, int dirfd, const char *pathname, mode_t mode, int flags)({
    int oflags = (flags & AT_SYMLINK_NOFOLLOW) ? O_NOFOLLOW : 0;
    auto check = bxl->report_access_at(__func__, ES_EVENT_TYPE_NOTIFY_SETMODE, dirfd, pathname, oflags);
    return bxl->check_and_fwd_fchmodat(check, ERROR_RETURN_VALUE, dirfd, pathname, mode, flags);
})

INTERPOSE(void*, dlopen, const char *filename, int flags)({
    static int libcSoNameLength = -1;
    if (libcSoNameLength == -1) libcSoNameLength = strlen(LIBC_SO);

    if (filename && (strncmp(filename, LIBC_SO, libcSoNameLength) == 0))
    {
        BXL_LOG_DEBUG(bxl, "NOT forwarding dlopen(\"%s\", %d); returning dlopen(NULL, %d)", filename, flags, flags);
        return bxl->real_dlopen((char*)NULL, flags);
    }
    else
    {
        return bxl->fwd_dlopen(filename, flags).restore();
    }
})

static void report_exit(int exitCode, void *args)
{
    BxlObserver::GetInstance()->report_access("on_exit", ES_EVENT_TYPE_NOTIFY_EXIT, std::string(""), std::string(""));
}

void __attribute__ ((constructor)) _bxl_linux_sandbox_init(void)
{
   on_exit(report_exit, NULL);
}

// ==========================

// having a main function is useful for various local testing
int main(int argc, char **argv)
{
    BxlObserver *inst = BxlObserver::GetInstance();
    printf("Path: %s\n", inst->GetReportsPath());
}