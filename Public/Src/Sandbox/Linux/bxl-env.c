// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

int main(int argc, char **argv)
{
    int forwardArgsStartIdx = 0;

    // some old versions of /usr/bin/env do not support the -C option, so handle that here before calling /usr/bin/env
    if (argc >= 3 && strcmp(argv[1], "-C") == 0) {
        forwardArgsStartIdx = 2;
        int ret = chdir(argv[2]);
        if (ret != 0) {
            fprintf(stderr, "%s: cannot change directory to '%s': %s\n", argv[0], argv[2], strerror(errno));
            return 125;
        }
    }

    char *envExe = "/usr/bin/env";
    argv[forwardArgsStartIdx] = envExe;
    return execv(envExe, &argv[forwardArgsStartIdx]);
}