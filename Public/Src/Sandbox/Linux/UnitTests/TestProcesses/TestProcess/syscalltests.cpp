// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "syscalltests.hpp"

#define GET_CWD                                                                                     \
    char cwd[PATH_MAX] = { 0 };                                                                     \
    char *res = getcwd(cwd, PATH_MAX);                                                              \

#define TEMPORARY_FILE                                                                              \
    GET_CWD                                                                                         \
    char *fileName = "testfile";                                                                    \
    std::string testFile("");                                                                       \
    testFile = cwd;                                                                                 \
    testFile.append("/");                                                                           \
    testFile.append(fileName);                                                                      \

#define WITH_TEMPORARY_FILE(innercode) {                                                            \
    TEMPORARY_FILE                                                                                  \
    int fd = open(testFile.c_str(), /* flags */ O_RDWR | O_CREAT, /* mode */ 0777);                 \
    innercode                                                                                       \
    remove(testFile.c_str());                                                                       \
}

#define CHECK_RESULT(res, sys) if (res < 0) { perror(#sys); return EXIT_FAILURE; }
#define CHECK_RESULT_NULL(res, sys) if (res == nullptr) { perror(#sys); return EXIT_FAILURE; }

int HandleChild(pid_t pid)
{
    if (pid == 0)
    {
        exit(EXIT_SUCCESS);
    }
    else if (pid == -1)
    {
        return EXIT_FAILURE;
    }

    int status;
    waitpid(pid, &status, 0);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(fork)
{
    return HandleChild(fork());
}

GEN_TEST_FN(vfork)
{
    return HandleChild(vfork());
}

static int CloneChild(void *arg)
{
    exit(EXIT_SUCCESS);
}

GEN_TEST_FN(clone)
{
    const int STACK_SIZE = 65536;
    char* stack = (char *)malloc(STACK_SIZE);
    char* stackTop = stack + STACK_SIZE;  /* Assume stack grows downward */
    return HandleChild(clone(CloneChild, stackTop, SIGCHLD, nullptr));
}

// GEN_TEST_FN(_exit)
// {

// }

int GetCurrentExe(char *buf, int bufsize)
{
    auto read = readlink("/proc/self/exe", buf, bufsize);
    if (read == -1)
    {
        fprintf(stderr, "Unable to find path to current exe");
        exit(EXIT_FAILURE);
    }
    return EXIT_SUCCESS;
}

/**
 * Open a file with all permissions
*/
int open(const char *path)
{
    return open(path, /* flags */ O_RDWR | O_CREAT, /* mode */ 0777);
}

/**
 * Open a directory with all permissions
*/
int opend(const char *path)
{
    return open(path, /* flags */ O_RDONLY, /* mode */ 0644);
}

GEN_TEST_FN(fexecve)
{
    // Executing the current exe without any args will cause it to fail and exit early which is good enough for this test
    char buf[PATH_MAX] = { 0 };
    GetCurrentExe(buf, PATH_MAX);

    auto pid = fork();
    if (pid == 0)
    {
        int fd = open(buf, O_RDONLY, 0644);

        static char *argv[] = { NULL };
        static char *envp[] = { NULL };

        fexecve(fd, argv, envp);

        // shouldn't happen, but in case it does lets just exit with success (not a failure because we were still able to interpose exec here)
        exit(EXIT_SUCCESS);
    }

    int status;
    waitpid(pid, &status, 0);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(execv)
{
    char buf[PATH_MAX] = { 0 };
    GetCurrentExe(buf, PATH_MAX);

    auto pid = fork();

    if (pid == 0)
    {
        static char *argv[] = { NULL };
        execv(buf, argv);

        exit(EXIT_SUCCESS);
    }

    int status;
    waitpid(pid, &status, 0);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(execve)
{
    char buf[PATH_MAX] = { 0 };
    GetCurrentExe(buf, PATH_MAX);

    auto pid = fork();

    if (pid == 0)
    {
        static char *argv[] = { NULL };
        static char *envp[] = { NULL };
        execve(buf, argv, envp);

        exit(EXIT_SUCCESS);
    }

    int status;
    waitpid(pid, &status, 0);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(execvp)
{
    char buf[PATH_MAX] = { 0 };
    GetCurrentExe(buf, PATH_MAX);

    auto pid = fork();

    if (pid == 0)
    {
        static char *argv[] = {  NULL };
        static char *envp[] = { NULL };
        execvp(buf, argv);

        exit(EXIT_SUCCESS);
    }

    int status;
    waitpid(pid, &status, 0);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(execvpe)
{
    char buf[PATH_MAX] = { 0 };
    GetCurrentExe(buf, PATH_MAX);

    auto pid = fork();

    if (pid == 0)
    {
        static char *argv[] = {  NULL };
        static char *envp[] = { NULL };
        execvpe(buf, argv, envp);

        exit(EXIT_SUCCESS);
    }

    int status;
    waitpid(pid, &status, 0);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(execl)
{
    char buf[PATH_MAX] = { 0 };
    GetCurrentExe(buf, PATH_MAX);

    auto pid = fork();

    if (pid == 0)
    {
        execl(buf, (char *) NULL);
        exit(EXIT_SUCCESS);
    }

    int status;
    waitpid(pid, &status, 0);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(execlp)
{
    char buf[PATH_MAX] = { 0 };
    GetCurrentExe(buf, PATH_MAX);

    auto pid = fork();

    if (pid == 0)
    {
        execlp(buf, (char *) NULL);
        exit(EXIT_SUCCESS);
    }

    int status;
    waitpid(pid, &status, 0);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(execle)
{
    char buf[PATH_MAX] = { 0 };
    GetCurrentExe(buf, PATH_MAX);

    auto pid = fork();

    if (pid == 0)
    {
        static char *envp[] = { NULL };
        execle(buf, (char *) NULL, envp);
        exit(EXIT_SUCCESS);
    }

    int status;
    waitpid(pid, &status, 0);

    return EXIT_SUCCESS;
}

int Test__lxstat()
{
#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
    WITH_TEMPORARY_FILE
    ({
        struct stat sb;
        int result = __lxstat(1, testFile.c_str(), &sb);
        CHECK_RESULT(result, __lxstat);
    })
#endif
    return EXIT_SUCCESS;
}

int Test__lxstat64()
{
#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
    WITH_TEMPORARY_FILE
    ({
        struct stat64 sb;
        int result = __lxstat64(1, testFile.c_str(), &sb);
        CHECK_RESULT(result, __lxstat64);
    })
#endif
    return EXIT_SUCCESS;
}

int Test__xstat()
{
#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
    WITH_TEMPORARY_FILE
    ({
        struct stat sb;
        int result = __xstat(1, testFile.c_str(), &sb);
        CHECK_RESULT(result, __xstat);
    })
#endif
    return EXIT_SUCCESS;

}

int Test__xstat64()
{
#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
    WITH_TEMPORARY_FILE
    ({
        struct stat64 sb;
        int result = __xstat64(1, testFile.c_str(), &sb);
        CHECK_RESULT(result, __xstat64);
    })
#endif
    return EXIT_SUCCESS;

}

int Test__fxstat()
{
#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
    WITH_TEMPORARY_FILE
    ({
        struct stat sb;
        int result = __fxstat(1, fd, &sb);
        CHECK_RESULT(result, __fxstat);
    })
#endif
    return EXIT_SUCCESS;
}

int Test__fxstatat()
{
#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
    WITH_TEMPORARY_FILE
    ({
        char buf[PATH_MAX] = { 0 };
        char *res = getcwd(buf, PATH_MAX);
        int dirfd = open(buf, O_RDONLY | O_DIRECTORY);
        struct stat sb;
        int result = __fxstatat(1, dirfd, "testfile", &sb, 0);
        CHECK_RESULT(result, __fxstatat);
    })
#endif
    return EXIT_SUCCESS;
}

int Test__fxstat64()
{
#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
    WITH_TEMPORARY_FILE
    ({
        struct stat64 sb;
        int result = __fxstat64(1, fd, &sb);
        CHECK_RESULT(result, __fxstat64);
    })
#endif
    return EXIT_SUCCESS;
}

int Test__fxstatat64()
{
#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
    WITH_TEMPORARY_FILE
    ({
        char buf[PATH_MAX] = { 0 };
        char *res = getcwd(buf, PATH_MAX);
        int dirfd = open(buf, O_RDONLY | O_DIRECTORY);
        struct stat64 sb;
        int result = __fxstatat64(1, dirfd, "testfile", &sb, 0);
        CHECK_RESULT(result, __fxstatat64);
    })
#endif
    return EXIT_SUCCESS;
}

int Teststat()
{
#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
    return EXIT_SUCCESS;
#else
    WITH_TEMPORARY_FILE
    ({
        struct stat sb;
        int result = stat(testFile.c_str(), &sb);
        CHECK_RESULT(result, stat);
    })
#endif
    return EXIT_SUCCESS;
}

int Teststat64()
{
#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
    return EXIT_SUCCESS;
#else
    WITH_TEMPORARY_FILE
    ({
        struct stat64 sb;
        int result = stat64(testFile.c_str(), &sb);
        CHECK_RESULT(result, stat64);
    })
#endif
    return EXIT_SUCCESS;
}

int Testlstat()
{
#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
    return EXIT_SUCCESS;
#else
    WITH_TEMPORARY_FILE
    ({
        struct stat sb;
        int result = lstat(testFile.c_str(), &sb);
        CHECK_RESULT(result, lstat);
    })
#endif
    return EXIT_SUCCESS;
}

int Testlstat64()
{
#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
    return EXIT_SUCCESS;
#else
    WITH_TEMPORARY_FILE
    ({
        struct stat64 sb;
        int result = lstat64(testFile.c_str(), &sb);
        CHECK_RESULT(result, lstat64);
    })
#endif
    return EXIT_SUCCESS;
}

int Testfstat()
{
#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
    return EXIT_SUCCESS;
#else
    WITH_TEMPORARY_FILE
    ({
        struct stat sb;
        int result = fstat(fd, &sb);
        CHECK_RESULT(result, fstat);
    })
#endif
    return EXIT_SUCCESS;
}

int Testfstat64()
{
#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
    return EXIT_SUCCESS;
#else
    WITH_TEMPORARY_FILE
    ({
        struct stat64 sb;
        int result = fstat64(fd, &sb);
        CHECK_RESULT(result, fstat64);
    })
#endif
    return EXIT_SUCCESS;
}

GEN_TEST_FN(fdopen)
{
    WITH_TEMPORARY_FILE
    ({
        FILE *fp = fdopen(fd, "rw");
        fclose(fp);
    })
    return EXIT_SUCCESS;
}

GEN_TEST_FN(fopen)
{
    WITH_TEMPORARY_FILE
    ({
        FILE *fp = fopen(testFile.c_str(), "rw");
        fclose(fp);
    })
    return EXIT_SUCCESS;
}

GEN_TEST_FN(fopen64)
{
    WITH_TEMPORARY_FILE
    ({
        FILE *fp = fopen64(testFile.c_str(), "rw");
        fclose(fp);
    })
    return EXIT_SUCCESS;
}

GEN_TEST_FN(freopen)
{
    WITH_TEMPORARY_FILE
    ({
        close(fd);
        FILE *fp = freopen(testFile.c_str(), "w+", stdout);
        CHECK_RESULT_NULL(fp, freopen);
        fclose(fp);
    })
    return EXIT_SUCCESS;
}

GEN_TEST_FN(freopen64)
{
    WITH_TEMPORARY_FILE
    ({
        close(fd);
        FILE *fp = freopen64(testFile.c_str(), "w+", stdout);
        CHECK_RESULT_NULL(fp, freopen64);
        fclose(fp);
    })
    return EXIT_SUCCESS;
}

GEN_TEST_FN(fread)
{
    WITH_TEMPORARY_FILE
    ({
        write(fd, "test", 4);

        unsigned char  buffer[4];
        FILE *fp = fopen(testFile.c_str(), "rb");
        if (!fp)
        {
            perror("fopen");
            return EXIT_FAILURE;
        }

        size_t ret = fread(buffer, sizeof(*buffer), 4, fp);
        if (ret != 4)
        {
            fprintf(stderr, "fread() failed: %zu\n", ret);
            return EXIT_FAILURE;
        }

        fclose(fp);
    });

    return EXIT_SUCCESS;
}

GEN_TEST_FN(fwrite)
{
    WITH_TEMPORARY_FILE
    ({
        char *str = "test string";
        FILE *fp = fopen(testFile.c_str(), "rw");
        if (!fp)
        {
            perror("fopen");
            return EXIT_FAILURE;
        }

        fwrite(str, 1, sizeof(str), fp);
        fclose(fp);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(fputc)
{
    WITH_TEMPORARY_FILE
    ({
        FILE *fp = fopen(testFile.c_str(), "rw");
        if (!fp)
        {
            perror("fopen");
            return EXIT_FAILURE;
        }

        fputc('a', fp);
        fclose(fp);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(fputs)
{
    WITH_TEMPORARY_FILE
    ({
        FILE *fp = fopen(testFile.c_str(), "rw+");
        if (!fp)
        {
            perror("fopen");
            return EXIT_FAILURE;
        }

        int result = fputs("test string", fp);
        CHECK_RESULT(result, fputs);
        fclose(fp);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(putc)
{
    WITH_TEMPORARY_FILE
    ({
        FILE *fp = fopen(testFile.c_str(), "rw");
        if (!fp)
        {
            perror("fopen");
            return EXIT_FAILURE;
        }

        putc('a', fp);
        fclose(fp);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(putchar)
{
    putchar('a');
    return EXIT_SUCCESS;
}

GEN_TEST_FN(puts)
{
    puts("test string");
    return EXIT_SUCCESS;
}

GEN_TEST_FN(access)
{
    WITH_TEMPORARY_FILE
    ({
        access(testFile.c_str(), F_OK);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(faccessat)
{
    WITH_TEMPORARY_FILE
    ({
        int dirfd = opend(cwd);

        faccessat(dirfd, fileName, F_OK, /* flags */ 0);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(creat)
{
    TEMPORARY_FILE

    int fd = creat(testFile.c_str(), O_CREAT | O_RDWR);
    if (fd == -1)
    {
        return EXIT_FAILURE;
    }

    close(fd);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(open64)
{
    TEMPORARY_FILE

    int fd = open64(testFile.c_str(), O_CREAT | O_RDWR, 0777);
    CHECK_RESULT(fd, open64);

    close(fd);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(open)
{
    // With temporary file calls open, we just need to validate that the fd is valid
    WITH_TEMPORARY_FILE
    ({
        CHECK_RESULT(fd, open);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(openat)
{
    TEMPORARY_FILE
    int dirfd = opend(cwd);

    int fd = openat(dirfd, fileName, O_CREAT | O_RDWR);
    CHECK_RESULT(fd, openat);

    close(fd);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(write)
{
    WITH_TEMPORARY_FILE
    ({
        ssize_t written = write(fd, "test string", 11);
        if (written == -1)
        {
            return EXIT_FAILURE;
        }
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(writev)
{
    // Source for example: https://man7.org/linux/man-pages/man3/writev.3p.html
    WITH_TEMPORARY_FILE
    ({
        ssize_t bytes_written;
        char *buf0 = "short string\n";
        char *buf1 = "This is a longer string\n";
        char *buf2 = "This is the longest string in this example\n";
        int iovcnt;
        struct iovec iov[3];

        iov[0].iov_base = buf0;
        iov[0].iov_len = strlen(buf0);
        iov[1].iov_base = buf1;
        iov[1].iov_len = strlen(buf1);
        iov[2].iov_base = buf2;
        iov[2].iov_len = strlen(buf2);
        iovcnt = sizeof(iov) / sizeof(struct iovec);

        bytes_written = writev(fd, iov, iovcnt);

        CHECK_RESULT(bytes_written, writev)
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(pwritev)
{
    WITH_TEMPORARY_FILE
    ({
        ssize_t bytes_written;
        char *buf0 = "short string\n";
        char *buf1 = "This is a longer string\n";
        char *buf2 = "This is the longest string in this example\n";
        int iovcnt;
        struct iovec iov[3];

        iov[0].iov_base = buf0;
        iov[0].iov_len = strlen(buf0);
        iov[1].iov_base = buf1;
        iov[1].iov_len = strlen(buf1);
        iov[2].iov_base = buf2;
        iov[2].iov_len = strlen(buf2);
        iovcnt = sizeof(iov) / sizeof(struct iovec);

        bytes_written = pwritev(fd, iov, iovcnt, /* offset */ 0);

        CHECK_RESULT(bytes_written, pwritev)
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(pwritev2)
{
    WITH_TEMPORARY_FILE
    ({
        ssize_t bytes_written;
        char *buf0 = "short string\n";
        char *buf1 = "This is a longer string\n";
        char *buf2 = "This is the longest string in this example\n";
        int iovcnt;
        struct iovec iov[3];

        iov[0].iov_base = buf0;
        iov[0].iov_len = strlen(buf0);
        iov[1].iov_base = buf1;
        iov[1].iov_len = strlen(buf1);
        iov[2].iov_base = buf2;
        iov[2].iov_len = strlen(buf2);
        iovcnt = sizeof(iov) / sizeof(struct iovec);

        bytes_written = pwritev2(fd, iov, iovcnt, /* offset */ -1, /* flags */ 0);

        CHECK_RESULT(bytes_written, pwritev2)
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(pwrite)
{
    WITH_TEMPORARY_FILE
    ({
        std::string buf("short string");
        ssize_t bytes_written = pwrite(fd, buf.c_str(), buf.length()-1, /* offset */ 0);

        CHECK_RESULT(bytes_written, pwrite)
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(pwrite64)
{
    WITH_TEMPORARY_FILE
    ({
        std::string buf("short string");
        ssize_t bytes_written = pwrite64(fd, buf.c_str(), buf.length()-1, /* offset */ 0);

        CHECK_RESULT(bytes_written, pwrite64)
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(remove)
{
    TEMPORARY_FILE
    int fd = open(testFile.c_str());
    auto result = remove(testFile.c_str());

    CHECK_RESULT(result, remove)

    return EXIT_SUCCESS;
}

GEN_TEST_FN(truncate)
{
    WITH_TEMPORARY_FILE
    ({
        std::string buf("short string");
        ssize_t bytes_written = write(fd, buf.c_str(), buf.length());

        int result = truncate(testFile.c_str(), /* length */ 1);

        CHECK_RESULT(result, truncate)
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(ftruncate)
{
    WITH_TEMPORARY_FILE
    ({
        std::string buf("short string");
        ssize_t bytes_written = write(fd, buf.c_str(), buf.length());

        int result = ftruncate(fd, /* length */ 1);

        CHECK_RESULT(result, ftruncate)
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(truncate64)
{
    WITH_TEMPORARY_FILE
    ({
        std::string buf("short string");
        ssize_t bytes_written = write(fd, buf.c_str(), buf.length());

        int result = truncate64(testFile.c_str(), /* length */ 1);

        CHECK_RESULT(result, truncate64)
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(ftruncate64)
{
    WITH_TEMPORARY_FILE
    ({
        std::string buf("short string");
        ssize_t bytes_written = write(fd, buf.c_str(), buf.length());

        int result = ftruncate64(fd, /* length */ 1);

        CHECK_RESULT(result, ftruncate64)
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(rmdir)
{
    GET_CWD
    std::string dirpath(cwd);
    dirpath.append("/testdirectory");

    int result = mkdir(dirpath.c_str(), 0700);
    CHECK_RESULT(result, mkdir)

    result = rmdir(dirpath.c_str());
    CHECK_RESULT(result, rmdir)

    return EXIT_SUCCESS;
}

GEN_TEST_FN(rename)
{
    TEMPORARY_FILE
    int fd = open(testFile.c_str());
    close(fd);

    std::string newPath(cwd);
    newPath.append("/testfile2");

    int result = rename(testFile.c_str(), newPath.c_str());
    CHECK_RESULT(result, rename);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(renameat)
{
    TEMPORARY_FILE
    int dirfd = opend(cwd);
    int fd = open(testFile.c_str());

    int result = renameat(dirfd, fileName, dirfd, "testfile2");
    CHECK_RESULT(result, renameat);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(link)
{
    WITH_TEMPORARY_FILE
    ({
        std::string newPath(cwd);
        newPath.append("/testfile2");
        
        int result = link(testFile.c_str(), newPath.c_str());
        CHECK_RESULT(result, link);
        remove(newPath.c_str());
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(linkat)
{
    WITH_TEMPORARY_FILE
    ({
        int dirfd = opend(cwd);
        std::string newPath(cwd);
        newPath.append("/testfile2");

        int result = linkat(dirfd, fileName, dirfd, "testfile2", /* flags */ 0);
        CHECK_RESULT(result, linkat);
        remove(newPath.c_str());
        close(dirfd);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(unlink)
{
    TEMPORARY_FILE
    int fd = open(testFile.c_str());
    close(fd);

    int result = unlink(testFile.c_str());
    CHECK_RESULT(result, unlink);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(unlinkat)
{
    TEMPORARY_FILE
    int fd = open(testFile.c_str());
    close(fd);
    int dirfd = opend(cwd);
    fprintf(stderr, "unlinkat: dirfd: %d", dirfd);
    
    int result = unlinkat(dirfd, fileName, /* flags */ 0);
    CHECK_RESULT(result, unlinkat);
    close(dirfd);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(symlink)
{
    WITH_TEMPORARY_FILE
    ({
        std::string target(cwd); 
        target.append("/testfile2");

        int result = symlink(testFile.c_str(), target.c_str());
        CHECK_RESULT(result, symlink);
        remove(target.c_str());
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(symlinkat)
{
    WITH_TEMPORARY_FILE
    ({
        std::string target(cwd); 
        target.append("/testfile2");
        int dirfd = opend(cwd);

        int result = symlinkat(testFile.c_str(), dirfd, "testfile2");
        CHECK_RESULT(result, symlinkat);
        remove(target.c_str());
        close(dirfd);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(readlink)
{
    WITH_TEMPORARY_FILE
    ({
        std::string target(cwd); 
        target.append("/testfile2");

        int result = symlink(testFile.c_str(), target.c_str());
        CHECK_RESULT(result, symlink);
        
        char buf[PATH_MAX] = { 0 };
        result = readlink(target.c_str(), buf, PATH_MAX);
        CHECK_RESULT(result, readlink);
        
        remove(target.c_str());
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(readlinkat)
{
    WITH_TEMPORARY_FILE
    ({
        std::string target(cwd); 
        target.append("/testfile2");

        int result = symlink(testFile.c_str(), target.c_str());
        CHECK_RESULT(result, symlink);
        
        char buf[PATH_MAX] = { 0 };
        int dirfd = opend(cwd);
        result = readlinkat(dirfd, "testfile2", buf, PATH_MAX);
        CHECK_RESULT(result, readlink);
        
        remove(target.c_str());
        close(dirfd);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(realpath)
{
    char buf[PATH_MAX] = { 0 };
    char* result = realpath("./", buf);
    CHECK_RESULT_NULL(result, realpath);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(opendir)
{
    GET_CWD
    DIR *dir = opendir(cwd);
    CHECK_RESULT_NULL(dir, opendir);
    closedir(dir);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(fdopendir)
{
    GET_CWD
    int dirfd = opend(cwd);
    DIR *dir = fdopendir(dirfd);
    CHECK_RESULT_NULL(dir, fdopendir);
    closedir(dir);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(utime)
{
    WITH_TEMPORARY_FILE
    ({
        struct utimbuf times;
        times.modtime = 0;
        time(&times.actime);
        int result = utime(testFile.c_str(), &times);
        CHECK_RESULT(result, utime);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(utimes)
{
    WITH_TEMPORARY_FILE
    ({
        struct timeval times[2];
        struct timezone tz;

        gettimeofday(&times[0], &tz);
        times[1].tv_sec = times[0].tv_sec;
        times[1].tv_usec = times[0].tv_usec;

        int result = utimes(testFile.c_str(), times);
        CHECK_RESULT(result, utimes);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(utimensat)
{
    WITH_TEMPORARY_FILE
    ({
        close(fd);
        int dirfd = opend(cwd);
        struct timespec times[2];
        struct timeval tv;
        struct timezone tz;

        gettimeofday(&tv, &tz);
        times[0].tv_sec = tv.tv_sec;
        times[0].tv_nsec = tv.tv_usec * 1000;
        times[1].tv_sec = tv.tv_sec;
        times[1].tv_nsec = tv.tv_usec * 1000;

        int result = utimensat(dirfd, fileName, times, /* flags */ 0);
        CHECK_RESULT(result, utimensat);

        close(dirfd);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(futimesat)
{
    WITH_TEMPORARY_FILE
    ({
        int dirfd = opend(cwd);
        struct timeval times[2];
        struct timezone tz;

        gettimeofday(&times[0], &tz);
        times[1].tv_sec = times[0].tv_sec;
        times[1].tv_usec = times[0].tv_usec;

        int result = futimesat(dirfd, fileName, times);
        CHECK_RESULT(result, utimes);
        close(dirfd);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(futimens)
{
    WITH_TEMPORARY_FILE
    ({
        struct timespec times[2];
        struct timeval tv;
        struct timezone tz;

        gettimeofday(&tv, &tz);
        times[0].tv_sec = tv.tv_sec;
        times[0].tv_nsec = tv.tv_usec * 1000;
        times[1].tv_sec = tv.tv_sec;
        times[1].tv_nsec = tv.tv_usec * 1000;

        int result = futimens(fd, times);
        CHECK_RESULT(result, futimens);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(mkdir)
{
    GET_CWD
    std::string newPath(cwd);
    newPath.append("/testdirectory");

    int result = mkdir(newPath.c_str(), /* mode */ 0644);
    CHECK_RESULT(result, mkdir);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(mkdirat)
{
    GET_CWD
    int dirfd = opend(cwd);

    int result = mkdirat(dirfd, "testdirectory", /* mode */ 0644);
    CHECK_RESULT(result, mkdirat);

    close(dirfd);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(mknod)
{
    GET_CWD
    std::string testFile(cwd);
    testFile.append("/testfile");
    // make sure the test file doesn't already exist
    unlink(testFile.c_str());
    fprintf(stderr, "test file exists: %d\n", access(testFile.c_str(), F_OK) == 0);
    int result = mknod(testFile.c_str(), (mode_t) (S_IFREG | 0777), (dev_t) 0);
    fprintf(stderr, "syscall result: %d errno: %d\n", result, errno);
    CHECK_RESULT(result, mknod);
    fprintf(stderr, "test file exists exists: %d\n", access(testFile.c_str(), F_OK) == 0);

    unlink(testFile.c_str());

    return EXIT_SUCCESS;
}

GEN_TEST_FN(mknodat)
{
    GET_CWD
    int dirfd = opend(cwd);
    std::string testFile(cwd);
    testFile.append("/testfile");
    int result = mknodat(dirfd, "testfile", 0777 | S_IFREG, 0);
    CHECK_RESULT(result, mknodat);

    unlink(testFile.c_str());

    return EXIT_SUCCESS;
}

GEN_TEST_FN(printf)
{
    int result = printf("test %s", "string");
    CHECK_RESULT(result, printf);
    return EXIT_SUCCESS;
}

GEN_TEST_FN(fprintf)
{
    WITH_TEMPORARY_FILE
    ({
        close(fd);
        FILE *fp = fopen(testFile.c_str(), "w+");
        CHECK_RESULT_NULL(fp, fopen);

        int result = fprintf(fp, "test %s", "string");
        CHECK_RESULT(result, fprintf);

        fclose(fp);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(dprintf)
{
    WITH_TEMPORARY_FILE
    ({
        int result = dprintf(fd, "test %s", "string");
        CHECK_RESULT(result, dprintf);
    })
    
    return EXIT_SUCCESS;
}

int vprintfhelper(const char *format, ...)
{
    va_list args;
    va_start(args, format);
    int result = vprintf(format, args);
    va_end(args);
    return result;
}

GEN_TEST_FN(vprintf)
{
    CHECK_RESULT(vprintfhelper("test %s", "string"), vprintf);
    return EXIT_SUCCESS;
}

int vfprintfhelper(FILE *fp, const char *format, ...)
{
    va_list args;
    va_start(args, format);
    int result = vfprintf(fp, format, args);
    va_end(args);
    return result;
}

GEN_TEST_FN(vfprintf)
{
    WITH_TEMPORARY_FILE
    ({
        close(fd);
        FILE *fp = fopen(testFile.c_str(), "w+");

        CHECK_RESULT(vfprintfhelper(fp, "test %s", "string"), vfprintf);
        fclose(fp);
    })
    return EXIT_SUCCESS;
}

int vdprintfhelper(int fd, const char *format, ...)
{
    va_list args;
    va_start(args, format);
    int result = vdprintf(fd, format, args);
    va_end(args);
    return result;
}


GEN_TEST_FN(vdprintf)
{
    WITH_TEMPORARY_FILE
    ({
        CHECK_RESULT(vdprintfhelper(fd, "test %s", "string"), vdprintf);
    })
    return EXIT_SUCCESS;
}

GEN_TEST_FN(chmod)
{
    WITH_TEMPORARY_FILE
    ({
        int result = chmod(testFile.c_str(), S_IRUSR | S_IRGRP | S_IROTH);
        CHECK_RESULT(result, chmod);
    })
    return EXIT_SUCCESS;
}

GEN_TEST_FN(fchmod)
{
    WITH_TEMPORARY_FILE
    ({
        int result = fchmod(fd, S_IRUSR | S_IRGRP | S_IROTH);
        CHECK_RESULT(result, fchmod);
    })
    return EXIT_SUCCESS;
}

GEN_TEST_FN(fchmodat)
{
    WITH_TEMPORARY_FILE
    ({
        int dirfd = opend(cwd);
        int result = fchmodat(dirfd, fileName, S_IRUSR | S_IRGRP | S_IROTH, /* flags */ 0);
        CHECK_RESULT(result, fchmodat);
        close(dirfd);
    })
    return EXIT_SUCCESS;
}

GEN_TEST_FN(chown)
{
    WITH_TEMPORARY_FILE
    ({
        int result = chown(testFile.c_str(), /* owner */ -1, /* group */ -1);
        CHECK_RESULT(result, chown);
    })
    return EXIT_SUCCESS;
}

GEN_TEST_FN(fchown)
{
    WITH_TEMPORARY_FILE
    ({
        int result = fchown(fd, /* owner */ -1, /* group */ -1);
        CHECK_RESULT(result, fchown);
    })
    return EXIT_SUCCESS;
}

GEN_TEST_FN(lchown)
{
    WITH_TEMPORARY_FILE
    ({
        int result = lchown(testFile.c_str(), /* owner */ -1, /* group */ -1);
        CHECK_RESULT(result, lchown);
    })
    return EXIT_SUCCESS;
}

GEN_TEST_FN(fchownat)
{
    WITH_TEMPORARY_FILE
    ({
        int dirfd = opend(cwd);
        int result = fchownat(dirfd, fileName, /* owner */ -1, /* group */ -1, /* flags */ 0);
        CHECK_RESULT(result, fchownat);
        close(dirfd);
    })
    return EXIT_SUCCESS;
}

GEN_TEST_FN(sendfile)
{
    WITH_TEMPORARY_FILE
    ({
        std::string testFile2(cwd);
        testFile2.append("/testfile2");
        int fd2 = open(testFile2.c_str(), O_RDWR | O_CREAT, 0777);
        CHECK_RESULT(fd2, open);
        CHECK_RESULT(write(fd2, "test string", 11), write);
        
        ssize_t result = sendfile(fd, fd2, /* offset */ 0, /* count */ 11);
        CHECK_RESULT(result, sendfile);

        close(fd2);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(sendfile64)
{
    WITH_TEMPORARY_FILE
    ({
        std::string testFile2(cwd);
        testFile2.append("/testfile2");
        int fd2 = open(testFile2.c_str(), O_RDWR | O_CREAT, 0777);
        CHECK_RESULT(fd2, open);
        CHECK_RESULT(write(fd2, "test string", 11), write);
        
        ssize_t result = sendfile64(fd, fd2, /* offset */ 0, /* count */ 11);
        CHECK_RESULT(result, sendfile64);

        close(fd2);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(copy_file_range)
{
    WITH_TEMPORARY_FILE
    ({
        std::string testFile2(cwd);
        testFile2.append("/testfile2");
        int fd2 = open(testFile2.c_str(), O_RDWR | O_CREAT, 0777);
        CHECK_RESULT(fd2, open);
        CHECK_RESULT(write(fd2, "test string", 11), write);
        
        ssize_t result = copy_file_range(fd2, /* offset */ 0, fd, /* offset */ 0, /* count */ 11, /* flags */ 0);
        CHECK_RESULT(result, copy_file_range);

        close(fd2);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(name_to_handle_at)
{
    WITH_TEMPORARY_FILE
    ({
        struct file_handle *handle  = (struct file_handle *)malloc(sizeof(struct file_handle) + MAX_HANDLE_SZ);
        handle->handle_bytes = MAX_HANDLE_SZ;
        int mountid;

        int result = name_to_handle_at(AT_FDCWD, fileName, handle, /* mount_id */ &mountid, /* flags */ 0);
        free(handle);

        CHECK_RESULT(result, name_to_handle_at);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(dup)
{
    WITH_TEMPORARY_FILE
    ({
        int result = dup(fd);
        CHECK_RESULT(result, dup);
        close(result);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(dup2)
{
    WITH_TEMPORARY_FILE
    ({
        int result = dup2(fd, /* newfd */ 15);
        CHECK_RESULT(result, dup2);
        close(result);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(dup3)
{
    WITH_TEMPORARY_FILE
    ({
        int result = dup3(fd, /* newfd */ 15, /* flags */ 0);
        CHECK_RESULT(result, dup3);
        close(result);
    })

    return EXIT_SUCCESS;
}

GEN_TEST_FN(scandir)
{
    struct dirent **namelist;
    int n;

    n = scandir(".", &namelist, NULL, alphasort);
    CHECK_RESULT(n, scandir);

    while (n--)
    {
        free(namelist[n]);
    }
    free(namelist);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(scandir64)
{
    struct dirent64 **namelist;
    int n;

    n = scandir64(".", &namelist, NULL, alphasort64);
    CHECK_RESULT(n, scandir64);

    while (n--)
    {
        free(namelist[n]);
    }
    free(namelist);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(scandirat)
{
    struct dirent **namelist;
    int n;

    n = scandirat(AT_FDCWD, ".", &namelist, NULL, alphasort);
    CHECK_RESULT(n, scandirat);

    while (n--)
    {
        free(namelist[n]);
    }
    free(namelist);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(scandirat64)
{
    struct dirent64 **namelist;
    int n;

    n = scandirat64(AT_FDCWD, ".", &namelist, NULL, alphasort64);
    CHECK_RESULT(n, scandirat64);

    while (n--)
    {
        free(namelist[n]);
    }
    free(namelist);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(statx)
{
    struct statx statxbuf = { 0 };
    int result = statx(AT_FDCWD, ".", /* flags */ 0, /* mask */ STATX_ALL, &statxbuf);
    CHECK_RESULT(result, statx);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(closedir)
{
    GET_CWD
    DIR *dir = opendir(cwd);
    CHECK_RESULT_NULL(dir, opendir);
    int result = closedir(dir);
    CHECK_RESULT(result, closedir);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(readdir)
{
    GET_CWD
    DIR *dir = opendir(cwd);
    CHECK_RESULT_NULL(dir, opendir);

    struct dirent *entry = readdir(dir);

    if (entry == nullptr && errno != 0)
    {
        perror("readdir");
        closedir(dir);
        return EXIT_FAILURE;
    }

    closedir(dir);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(readdir64)
{
    GET_CWD
    DIR *dir = opendir(cwd);
    CHECK_RESULT_NULL(dir, opendir);

    struct dirent64 *entry = readdir64(dir);

    if (entry == nullptr && errno != 0)
    {
        perror("readdir64");
        closedir(dir);
        return EXIT_FAILURE;
    }

    closedir(dir);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(readdir_r)
{
    GET_CWD
    DIR *dir = opendir(cwd);
    CHECK_RESULT_NULL(dir, opendir);

    struct dirent e;
    struct dirent *p = &e;

    int result = readdir_r(dir, &e, &p);

    if (result > 0)
    {
        perror("readdir_r");
        closedir(dir);
        return EXIT_FAILURE;
    }

    closedir(dir);

    return EXIT_SUCCESS;
}

GEN_TEST_FN(readdir64_r)
{
    GET_CWD
    DIR *dir = opendir(cwd);
    CHECK_RESULT_NULL(dir, opendir);

    struct dirent64 e;
    struct dirent64 *p = &e;

    int result = readdir64_r(dir, &e, &p);

    if (result > 0)
    {
        perror("readdir64_r");
        closedir(dir);
        return EXIT_FAILURE;
    }

    closedir(dir);

    return EXIT_SUCCESS;
}