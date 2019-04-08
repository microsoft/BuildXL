// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <stdio.h>
#include <stdlib.h>
#include <stdbool.h>
#include <sys/types.h>
#include <unistd.h>
#include <string.h>

void writeFile(const char *path)
{
    // print to file from a forked child process
    FILE *file = fopen(path, "w");
    fprintf(file, "Hello from child! PID = %d, PPID = %d\n", getpid(), getppid());
    fclose(file);
    printf("Child process PID(%d) PPID(%d) wrote to file: %s\n", getpid(), getppid(), path);
}

void doFork(const char *path, bool waitForChild, int sleepSeconds, int depth)
{
    int childPid = fork();
    if (childPid == 0)
    {
        if (depth <= 0)
        {
            // sleep first to give the parent process some time to exit (unless it's explicitly waiting)
            sleep(sleepSeconds);
            writeFile(path);
        }
        else
        {
            doFork(path, waitForChild, sleepSeconds, depth-1);
        }
    }
    else
    {
        if (waitForChild)
        {
            printf("Parent process PID(%d) PPID(%d); waiting for child PID(%d) to exit...\n", 
                getpid(), getppid(), childPid);
            int status;
            waitpid(childPid, &status, 0);
        }
        printf("Parent process PID(%d) PPID(%d) done.\n", getpid(), getppid());
    }
}

int main(int argc, char** argv)
{
    if (argc < 2)
    {
        printf("Usage: %s <output-file-path> [--wait-for-child][--depth <int>][--sleep <int>]", argv[0]);
        return 1;
    }

    const char *path = argv[1];
    bool waitForChild = false;
    int depth = 2;
    int sleep = 1;
    int i = 1;
    while (++i < argc) {
        if (strcmp(argv[i], "--wait-for-child") == 0)
        {
            waitForChild = true;
        }
         else if (strcmp(argv[i], "--depth") == 0)
         {
            if (++i < argc)
            {
                depth = strtol(argv[i], NULL, 10);
            }
        }
        else if (strcmp(argv[i], "--sleep") == 0)
        {
            if (++i < argc)
            {
                sleep = strtol(argv[i], NULL, 10);
            }
        }
    }

    doFork(path, waitForChild, sleep, depth);
    return 0; 
}
