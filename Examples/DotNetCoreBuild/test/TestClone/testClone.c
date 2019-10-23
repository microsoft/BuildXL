// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <stdio.h>
#include <stdlib.h>
#include <stdbool.h>
#include <sys/types.h>
#include <unistd.h>
#include <string.h>

int main(int argc, char** argv)
{
    if (argc < 3)
    {
        printf("Usage: %s <input-file-path> <output-file-path>", argv[0]);
        return 1;
    }

    const char *input = argv[1];
    const char *output = argv[2];

    return clonefile(input, output, 0);
}
