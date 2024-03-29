// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// stdafx.h : include file for standard system include files,
// or project specific include files that are used frequently, but
// are changed infrequently
//

#pragma once

// Disable warnings about unreferenced inline functions. We'd do this below but this disable has to be in effect
// at optimization time.
#pragma warning( disable : 4514 4710 )

// We don't care about the addition of needed struct padding.
#pragma warning( disable : 4820 )

// warning C5045: Compiler will insert Spectre mitigation for memory load if /Qspectre switch specified
// This spectre mitigation has been applied to detours, however this warning will continue to show up with /Wall enabled
#pragma warning( disable : 5045)

// BuildXL should run on Win10+.
#include <WinSDKVer.h>
#define _WIN32_WINNT _WIN32_WINNT_WIN10
#include <SDKDDKVer.h>

// In order to compile with /Wall (mega pedantic warnings), we need to turn off a few that the Windows SDK violates.
// We could do this in stdafx.cpp so long as a precompiled header is being generated, since the compiler state from
// that file (including warning state!) would be dumped to the .pch - instead, we stick to the sane compilation
// model and twiddle warning state at include time. This means that disabling .pch generation doesn't result in weird warnings.
#pragma warning( push )
#pragma warning( disable : 4350 4668 )
#include <windows.h>
#include <winternl.h>
#include <stdarg.h>
#include <stdio.h>
#include <assert.h>
#include <string>
#include <vector>
#include <memory>
#pragma warning( pop )