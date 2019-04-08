// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <stdio.h>
#include <stdlib.h>
#include <stdbool.h>
#include <sys/types.h>
#include <unistd.h>
#include <string.h>

void doRead(const char *path, const char *probePath, int sleepUs)
{
    FILE *readFile = fopen(path, "rw");
    if (!readFile)
    {
        printf("Cannot open file to read: '%s'.\n", path);
        exit(1);
    }

    int numCharsRead = 0;
    char ch;
    while ((ch = fgetc(readFile)) != EOF)
    {
        numCharsRead++;
        int err = ferror(readFile);
        if (err != 0)
        {
            printf("Error while reading file '%s': %d\n", path, err);
            exit(2);
        }

        err = fflush(readFile);
        if (err != 0)
        {
            printf("Flush failed on file '%s': %d\n", path, err);
            exit(3);
        }

        FILE *probeFile = fopen(probePath, "r");
        if (!probeFile)
        {
            printf("Cannot open file to probe: '%s'.\n", probeFile);
            exit(4);
        }
        else
        {
            fclose(probeFile);
        }

        usleep(sleepUs);
    }

    printf("Read %d character from '%s'", numCharsRead, path);
    fclose(readFile);
}

int main(int argc, char** argv)
{
    if (argc < 3)
    {
        printf("Usage: %s <read-file-path> <probe-file-path> [--usleep <int>]", argv[0]);
        return 1;
    }

    const char *path = argv[1];
    const char *probePath = argv[2];

    int sleep = 1;
    int i = 1;
    while (++i < argc)
    {
        if (strcmp(argv[i], "--usleep") == 0)
        {
            if (++i < argc)
            {
                sleep = strtol(argv[i], NULL, 10);
            }
        }
    }

    doRead(path, probePath, sleep);
    return 0; 
}
