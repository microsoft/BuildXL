// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <curses.h>
#include <stdio.h>
#include <stdlib.h>
#include <sys/param.h>
#include <unistd.h>

#include "process.h"

int main(int argc, const char * argv[])
{
    if (argc < 2)
    {
        // Dump path must be given as parameter
        return -1;
    }

    char *buffer = calloc(MAXPATHLEN, sizeof(char));
    SetupProcessDumps(argv[1], buffer, MAXPATHLEN);

    while(true)
    {
        fprintf(stdout, "kern.corefile=%s\n", buffer);
        fflush(stdout);
        sleep(1);
    }

    return 0;
}
