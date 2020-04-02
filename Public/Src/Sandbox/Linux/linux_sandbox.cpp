// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <dlfcn.h>
#include <unistd.h>
#include <sys/types.h>

#include "linux_sandbox.h"

typedef FILE* (fopen_fn)(const char *, const char *);

static bxl_state* init_from_pip(SandboxedPip *pip)
{
    bxl_state *bxl = (bxl_state*)malloc(sizeof(bxl_state));
    bxl->pip = pip;
    return bxl;
}

static bxl_state* init_from_fam(const char *famPath)
{
    static fopen_fn *real_fopen = NULL;
    if (!real_fopen) real_fopen = (fopen_fn*)dlsym(RTLD_NEXT, "fopen");

    FILE *famFile = real_fopen(famPath, "rb");
    if (!famFile)
    {
        _fatal("Could not open file '%s'; errno: %d", famPath, errno);
    }

    fseek(famFile, 0, SEEK_END);
    long famLength = ftell(famFile);
    rewind(famFile);

    char *famPayload = (char *)malloc(famLength);
    fread(famPayload, famLength, 1, famFile);
    fclose(famFile);

    return init_from_pip(new SandboxedPip(getpid(), famPayload, famLength));
}

bxl_state* bxl_linux_sandbox_init()
{
    const char *famPath = getenv(BxlEnvFamPath);
    if (famPath && *famPath)
    {
        return init_from_fam(famPath);
    }

    _fatal("Env var '%s' not set", BxlEnvFamPath);
    return NULL;
}
