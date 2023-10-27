// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include<stdio.h>
#include<unistd.h>

// executes the given executable and arguments using vfork
int main(int argc, char *argv[]) 
{
    if (argc < 2)
    {
        printf("Not enough arguments. Syntax: vforkSpawn <executable> <args...>\n");
        return 1;
    }

    // We are going to use execv, and therefore we need to construct the arguments so the last
    // element is a null terminated string

    // First argument is always, by convention, the file being executed
    char * execvargs[argc];
    execvargs[0] = argv[1];

    // Copy the other pointers
    for (int i = 2 ; i < argc; i++)
    {
        execvargs[i - 1] = argv[i];
    }

    // And make sure the last one is null
    execvargs[argc - 1] = NULL;

    // Do vfork + exec
    pid_t pid = vfork();
    if (pid == 0)
    {
        execv(argv[1], execvargs);
        // exec failed, otherwise it won't return
        _exit(1);
    }

    return 0;
}