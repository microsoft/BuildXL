// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#pragma once

#ifdef __cplusplus
#define EXTERNC extern "C"
#else
#define EXTERNC
#endif

#include <limits.h>
#include <stddef.h>

#include "SandboxedPip.hpp"

static const char *BxlEnvFamPath = "__BUILDXL_FAM_PATH";
static const char *BxlEnvLogPath = "__BUILDXL_LOG_PATH";

#define ARRAYSIZE(arr) (sizeof(arr)/sizeof(arr[0]))

typedef struct
{
    SandboxedPip *pip;
} bxl_state;

#define _fatal(fmt, ...) do { fprintf(stderr, "(%s) " fmt "\n", __func__, __VA_ARGS__); exit(1); } while (0)
#define fatal(msg) _fatal("%s", msg)

EXTERNC bxl_state* bxl_linux_sandbox_init();
