// Copyright (c); Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <dirent.h>
#include <errno.h>
#include <fcntl.h>
#include <iostream>
#include <libgen.h>
#include <linux/limits.h>
#include <stdarg.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <sys/sendfile.h>
#include <sys/stat.h>
#include <sys/time.h>
#include <sys/types.h>
#include <sys/uio.h>
#include <sys/utsname.h>
#include <sys/wait.h>
#include <unistd.h>
#include <utime.h>

#define GET_CWD                                                                                     \
    char cwd[PATH_MAX] = { 0 };                                                                     \
    char *res = getcwd(cwd, PATH_MAX);                                                              \

#define GEN_TEST_FN(syscall); int Test##syscall()

GEN_TEST_FN(fork);
GEN_TEST_FN(vfork);
GEN_TEST_FN(clone);
GEN_TEST_FN(clone3);
GEN_TEST_FN(clone3WithProbe);
GEN_TEST_FN(clone3Nested);
GEN_TEST_FN(clone3NestedAndExec);
GEN_TEST_FN(_exit);
GEN_TEST_FN(fexecve);
GEN_TEST_FN(execv);
GEN_TEST_FN(execve);
GEN_TEST_FN(execvp);
GEN_TEST_FN(execvpe);
GEN_TEST_FN(execl);
GEN_TEST_FN(execlp);
GEN_TEST_FN(execle);
// The tests below are manually created because they are not available on all versions of glibc
int Test__lxstat();
int Test__lxstat64();
int Test__xstat();
int Test__xstat64();
int Test__fxstat();
int Test__fxstatat();
int Test__fxstat64();
int Test__fxstatat64();
int Teststat();
int Teststat64();
int Testlstat();
int Testlstat64();
int Testfstat();
int Testfstat64();
GEN_TEST_FN(fdopen);
GEN_TEST_FN(fopen);
GEN_TEST_FN(fopen64);
GEN_TEST_FN(freopen);
GEN_TEST_FN(freopen64);
GEN_TEST_FN(fread);
GEN_TEST_FN(fwrite);
GEN_TEST_FN(fputc);
GEN_TEST_FN(fputs);
GEN_TEST_FN(putc);
GEN_TEST_FN(putchar);
GEN_TEST_FN(puts);
GEN_TEST_FN(access);
GEN_TEST_FN(faccessat);
GEN_TEST_FN(creat);
GEN_TEST_FN(open64);
GEN_TEST_FN(open);
GEN_TEST_FN(openat);
GEN_TEST_FN(write);
GEN_TEST_FN(writev);
GEN_TEST_FN(pwritev);
GEN_TEST_FN(pwritev2);
GEN_TEST_FN(pwrite);
GEN_TEST_FN(pwrite64);
GEN_TEST_FN(remove);
GEN_TEST_FN(truncate);
GEN_TEST_FN(ftruncate);
GEN_TEST_FN(truncate64);
GEN_TEST_FN(ftruncate64);
GEN_TEST_FN(rmdir);
GEN_TEST_FN(rename);
GEN_TEST_FN(renameat);
GEN_TEST_FN(renameat2);
GEN_TEST_FN(link);
GEN_TEST_FN(linkat);
GEN_TEST_FN(unlink);
GEN_TEST_FN(unlinkat);
GEN_TEST_FN(symlink);
GEN_TEST_FN(symlinkat);
GEN_TEST_FN(readlink);
GEN_TEST_FN(readlinkat);
GEN_TEST_FN(realpath);
GEN_TEST_FN(opendir);
GEN_TEST_FN(fdopendir);
GEN_TEST_FN(utime);
GEN_TEST_FN(utimes);
GEN_TEST_FN(utimensat);
GEN_TEST_FN(futimesat);
GEN_TEST_FN(futimens);
GEN_TEST_FN(mkdir);
GEN_TEST_FN(mkdirat);
GEN_TEST_FN(mknod);
GEN_TEST_FN(mknodat);
GEN_TEST_FN(printf);
GEN_TEST_FN(fprintf);
GEN_TEST_FN(dprintf);
GEN_TEST_FN(vprintf);
GEN_TEST_FN(vfprintf);
GEN_TEST_FN(vdprintf);
GEN_TEST_FN(chmod);
GEN_TEST_FN(fchmod);
GEN_TEST_FN(fchmodat);
GEN_TEST_FN(chown);
GEN_TEST_FN(fchown);
GEN_TEST_FN(lchown);
GEN_TEST_FN(fchownat);
GEN_TEST_FN(sendfile);
GEN_TEST_FN(sendfile64);
GEN_TEST_FN(copy_file_range);
GEN_TEST_FN(name_to_handle_at);
GEN_TEST_FN(dup);
GEN_TEST_FN(dup2);
GEN_TEST_FN(dup3);
GEN_TEST_FN(scandir);
GEN_TEST_FN(scandir64);
GEN_TEST_FN(scandirat);
GEN_TEST_FN(scandirat64);
GEN_TEST_FN(statx);
GEN_TEST_FN(closedir);
GEN_TEST_FN(readdir);
GEN_TEST_FN(readdir64);
GEN_TEST_FN(readdir_r);
GEN_TEST_FN(readdir64_r);
