// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

// Disable warnings about unreferenced inline functions. We'd do this below but this disable has to be in effect
// at optimization time.
#pragma warning( disable : 4514 4710 4191)
// 'function': pointer or reference to potentially throwing function passed to extern C function under -EHc. Undefined behavior may occur if this function throws an exception.
// Thrown for source files included from the Windows SDK.
#pragma warning( disable : 5039)
// warning C5045: Compiler will insert Spectre mitigation for memory load if /Qspectre switch specified
// The /Qspectre flag has been specified to mitigate this, however this warning will continue to show up with /Wall enabled
#pragma warning( disable : 5045)

#define WIN32_LEAN_AND_MEAN

#include <stdio.h>
#include <tchar.h>
#include <time.h>

#include <Windows.h>
#include <string>
#include <assert.h>
#include <memory>
#include <vector>

