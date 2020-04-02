// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// stdafx.h : include file for standard system include files,
// or project specific include files that are used frequently, but
// are changed infrequently
//

#pragma once

#if !defined(MAC_OS_LIBRARY)
#define MAC_OS_LIBRARY 0
#endif // !defined(MAC_OS_LIBRARY)

#if !defined(MAC_OS_SANDBOX)
#define MAC_OS_SANDBOX 0
#endif // !defined(MAC_OS_SANDBOX)

#if _WIN32
#define __linux__ 0
#define __APPLE__ 0
#endif

#if __linux__

// Linux stuff
#include "stdafx-linux.h" // must include linux before unix-common
#include "stdafx-unix-common.h"

#elif __APPLE__

// OSX stuff
#if MAC_OS_SANDBOX
#include "stdafx-mac-kext.h"
#else
#include "stdafx-mac-interop.h"
#endif
#include "stdafx-unix-common.h"

#else

// Windows stuff
#include "stdafx-win.h"

#endif