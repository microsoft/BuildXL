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
    IF_COMMAND(TestAnonymousFile);
    IF_COMMAND_STR(fork);
    IF_COMMAND_STR(vfork);
    IF_COMMAND_STR(clone);
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
    IF_COMMAND_STR(link);
    IF_COMMAND_STR(linkat);
    IF_COMMAND_STR(unlink);
    IF_COMMAND_STR(unlinkat);
    IF_COMMAND_STR(symlink);
    IF_COMMAND_STR(symlinkat);
    IF_COMMAND_STR(readlink);
    IF_COMMAND_STR(readlinkat);
    IF_COMMAND_STR(realpath);
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

    // Invalid command
    exit(-1);
}