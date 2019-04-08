// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// stdafx-win.h : windows-specific stdafx

#pragma once

// Disable warnings about unreferenced inline functions. We'd do this below but this disable has to be in effect
// at optimization time.
#pragma warning( disable : 4514 4710 )

// We don't care about the addition of needed struct padding.
#pragma warning( disable : 4820 )

// Peculiar warning about binding an rvalue to a non-const reference. But this appears to be in std::map? Instantiations seem fine.
// Allegedly triggered by HandleOverlay.cpp, but suppressing there doesn't work for some reason.
#pragma warning (disable : 4350)

// In order to compile with /Wall (mega pedantic warnings), we need to turn off a few that the Windows SDK violates.
// We could do this in stdafx.cpp so long as a precompiled header is being generated, since the compiler state from
// that file (including warning state!) would be dumped to the .pch - instead, we stick to the sane compilation
// model and twiddle warning state at include time. This means that disabling .pch generation doesn't result in weird warnings.
#pragma warning( disable : 4711) // ... selected for inline expansion
#pragma warning( push )
#pragma warning( disable : 4350 4668 )

#include "targetver.h"
#include <sdkddkver.h>
#include <windows.h>
#include <stdarg.h>
#include <stdio.h>
#include <detours.h>
#include <string>
#include <vector>
#include <memory>
#pragma warning( pop )

#include "Assertions.h"
