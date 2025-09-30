// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <errno.h>
#include <iostream>
#include <sys/mman.h>
#include <unistd.h>
#include "syscalltests.hpp"

int TestAnonymousFile()
{
    int fd = memfd_create("testFile", MFD_ALLOW_SEALING);
    if (fd == -1)
    {
        std::cerr << "memfd_create failed with errno " << errno << std::endl;
        return 2;
    }

    // Run a few system calls to see if any accesses to the anonymous file is reported to BuildXL
    if (ftruncate(fd, /* length */ 10) == -1)
    {
        std::cerr << "ftruncate failed with errno " << errno << std::endl;
        return 3;
    }

    if (close(fd) == -1)
    {
        std::cerr << "close failed with errno " << errno << std::endl;
        return 4;
    }

    return 0;
}

// The managed side creates a (directory) symlink symlinkDir -> realDir.
int FullPathResolutionOnReports()
{
    char buf[PATH_MAX] = { 0 };
    GET_CWD;
    std::string testFile(cwd);
    testFile.append("/symlinkDir/nonExistingFile.txt");
    int result = readlink(testFile.c_str(), buf, PATH_MAX);
    return EXIT_SUCCESS;
}

// The managed side creates a file symlink realDir/symlink.txt -> realDir/real.txt.
int ReadlinkReportDoesNotResolveFinalComponent()
{
    char buf[PATH_MAX] = { 0 };
    GET_CWD;
    std::string testFile(cwd);
    testFile.append("/realDir/symlink.txt");
    int result = readlink(testFile.c_str(), buf, PATH_MAX);
    return EXIT_SUCCESS;
}

// The managed side creates:
// - a directory symlink realDir -> symlinkDir
// - a file symlink realDir/symlink.txt -> realDir/real.txt.
int FileDescriptorAccessesFullyResolvesPath()
{
    char buf[PATH_MAX] = { 0 };
    GET_CWD;
    std::string testFile(cwd);
    testFile.append("/realDir/symlink.txt");
    int fd = open("symlinkDir/symlink.txt", O_RDONLY);
    struct stat sb;

#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
    // Use __fxtat as representative for a "file descriptor event"
    __fxstat(1, fd, &sb);
#else
    fstat(fd, &sb);
#endif
    return EXIT_SUCCESS;
}

int ExecReportsCorrectExecutableAndArgumentsSuccess() {
    const char *const args[] = {"/bin/echo", "hello world", nullptr};
    execv(args[0], const_cast<char* const*>(args));

    // execv should have succeeded and we should never hit this return statement
    return 1;
}

int ExecReportsCorrectExecutableAndArgumentsFailed() {
    const char *const args[] = {"/bin/echooooo", "hello world", nullptr};
    execv(args[0], const_cast<char* const*>(args));

    // expecting execv to fail here
    return EXIT_SUCCESS;
}

int OpenAtHandlesInvalidFd()
{
    int fd = openat(-1, "", O_CREAT | O_RDWR, 0666);

    // The above call should always fail, but we're testing whether the sandbox is resilient to bad inputs, so we don't care about the return value.
    return EXIT_SUCCESS;
}

int AccessLongPath()
{
    // Generate a path longer than 4k chars
    // This looks like '/foo/foo/foo...'
    std::string path;
    for (int i = 0 ; i < 8192; i++)
    {
        path.append("/foo");
    }

    int fd = access(path.c_str(), F_OK);

    // The above call should always fail, but we're testing whether the sandbox is resilient to bad inputs, so we don't care about the return value.
    return EXIT_SUCCESS;
}

/**
 * This test is expected to fail with EINVAL because readlink does not support reading links on directories.
 */
int ReadLinkOnDirectoryIsRead()
{
    GET_CWD;
    std::string linkPath(cwd);
    linkPath.append("/readlinkDirectory");

    char buf[PATH_MAX] = { 0 };
    int result = readlink(linkPath.c_str(), buf, PATH_MAX);
    if (result == -1)
    {
        return errno;
    }

    // We should never reach this point because readlink should fail with EINVAL
    return EXIT_FAILURE;
}

int main(int argc, char **argv)
{
    int opt;
    std::string testName;

    // Parse arguments
    while((opt = getopt(argc, argv, "t")) != -1)
    {
        switch (opt)
        {
            case 't':
                // -t <name of test to run>
                testName = std::string(argv[optind]);
                break;
        }
    }

    #define STR(NAME) #NAME
    #define IF_COMMAND(NAME)   { if (testName == #NAME) { exit(NAME()); } }
    #define IF_COMMAND_STR(NAME)   { if (testName == STR(Test##NAME)) { exit(Test##NAME()); } }

    // Function Definitions
    // Basic tests
    IF_COMMAND_STR(fork);
    IF_COMMAND_STR(vfork);
    IF_COMMAND_STR(clone);
    IF_COMMAND_STR(clone3);
    IF_COMMAND_STR(clone3WithProbe);
    IF_COMMAND_STR(clone3Nested);
    IF_COMMAND_STR(clone3NestedAndExec);
    IF_COMMAND_STR(fexecve);
    IF_COMMAND_STR(execv);
    IF_COMMAND_STR(execve);
    IF_COMMAND_STR(execvp);
    IF_COMMAND_STR(execvpe);
    IF_COMMAND_STR(execl);
    IF_COMMAND_STR(execlp);
    IF_COMMAND_STR(execle);
    IF_COMMAND(Test__lxstat);
    IF_COMMAND(Test__lxstat64);
    IF_COMMAND(Test__xstat);
    IF_COMMAND(Test__xstat64);
    IF_COMMAND(Test__fxstat);
    IF_COMMAND(Test__fxstatat);
    IF_COMMAND(Test__fxstat64);
    IF_COMMAND(Test__fxstatat64);
    IF_COMMAND(Teststat);
    IF_COMMAND(Teststat64);
    IF_COMMAND(Testlstat);
    IF_COMMAND(Testlstat64);
    IF_COMMAND(Testfstat);
    IF_COMMAND(Testfstat64);
    IF_COMMAND_STR(fdopen);
    IF_COMMAND_STR(fopen);
    IF_COMMAND_STR(fopen64);
    IF_COMMAND_STR(freopen);
    IF_COMMAND_STR(freopen64);
    IF_COMMAND_STR(fread);
    IF_COMMAND_STR(fwrite);
    IF_COMMAND_STR(fputc);
    IF_COMMAND_STR(fputs);
    IF_COMMAND_STR(putc);
    IF_COMMAND_STR(putchar);
    IF_COMMAND_STR(puts);
    IF_COMMAND_STR(access);
    IF_COMMAND_STR(faccessat);
    IF_COMMAND_STR(creat);
    IF_COMMAND_STR(open64);
    IF_COMMAND_STR(open);
    IF_COMMAND_STR(openat);
    IF_COMMAND_STR(write);
    IF_COMMAND_STR(writev);
    IF_COMMAND_STR(pwritev);
    IF_COMMAND_STR(pwritev2);
    IF_COMMAND_STR(pwrite);
    IF_COMMAND_STR(pwrite64);
    IF_COMMAND_STR(remove);
    IF_COMMAND_STR(truncate);
    IF_COMMAND_STR(ftruncate);
    IF_COMMAND_STR(truncate64);
    IF_COMMAND_STR(ftruncate64);
    IF_COMMAND_STR(rmdir);
    IF_COMMAND_STR(rename);
    IF_COMMAND_STR(renameat);
    IF_COMMAND_STR(renameat2);
    IF_COMMAND_STR(link);
    IF_COMMAND_STR(linkat);
    IF_COMMAND_STR(unlink);
    IF_COMMAND_STR(unlinkat);
    IF_COMMAND_STR(symlink);
    IF_COMMAND_STR(symlinkat);
    IF_COMMAND_STR(readlink);
    IF_COMMAND_STR(readlinkat);
    IF_COMMAND_STR(realpath);
    IF_COMMAND_STR(realpathOnNonSymlink);
    IF_COMMAND_STR(opendir);
    IF_COMMAND_STR(fdopendir);
    IF_COMMAND_STR(utime);
    IF_COMMAND_STR(utimes);
    IF_COMMAND_STR(utimensat);
    IF_COMMAND_STR(futimesat);
    IF_COMMAND_STR(futimens);
    IF_COMMAND_STR(mkdir);
    IF_COMMAND_STR(mkdirat);
    IF_COMMAND_STR(mknod);
    IF_COMMAND_STR(mknodat);
    IF_COMMAND_STR(printf);
    IF_COMMAND_STR(fprintf);
    IF_COMMAND_STR(dprintf);
    IF_COMMAND_STR(vprintf);
    IF_COMMAND_STR(vfprintf);
    IF_COMMAND_STR(vdprintf);
    IF_COMMAND_STR(chmod);
    IF_COMMAND_STR(fchmod);
    IF_COMMAND_STR(fchmodat);
    IF_COMMAND_STR(chown);
    IF_COMMAND_STR(fchown);
    IF_COMMAND_STR(lchown);
    IF_COMMAND_STR(fchownat);
    IF_COMMAND_STR(sendfile);
    IF_COMMAND_STR(sendfile64);
    IF_COMMAND_STR(copy_file_range);
    IF_COMMAND_STR(name_to_handle_at);
    IF_COMMAND_STR(dup);
    IF_COMMAND_STR(dup2);
    IF_COMMAND_STR(dup3);
    IF_COMMAND_STR(scandir);
    IF_COMMAND_STR(scandir64);
    IF_COMMAND_STR(scandirat);
    IF_COMMAND_STR(scandirat64);
    IF_COMMAND_STR(statx);
    IF_COMMAND_STR(closedir);
    IF_COMMAND_STR(readdir);
    IF_COMMAND_STR(readdir64);
    IF_COMMAND_STR(readdir_r);
    IF_COMMAND_STR(readdir64_r);
    // Special tests
    IF_COMMAND(TestAnonymousFile);
    IF_COMMAND(FullPathResolutionOnReports);
    IF_COMMAND(ReadlinkReportDoesNotResolveFinalComponent);
    IF_COMMAND(FileDescriptorAccessesFullyResolvesPath);
    IF_COMMAND(ExecReportsCorrectExecutableAndArgumentsSuccess);
    IF_COMMAND(ExecReportsCorrectExecutableAndArgumentsFailed);
    IF_COMMAND(OpenAtHandlesInvalidFd);
    IF_COMMAND(AccessLongPath);
    IF_COMMAND(ReadLinkOnDirectoryIsRead);

    // Invalid command
    exit(-1);
}