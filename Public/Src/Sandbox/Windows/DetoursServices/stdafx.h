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

#if !(MAC_OS_LIBRARY) && !(MAC_OS_SANDBOX)

#include "stdafx-win.h"

#else // !(MAC_OS_LIBRARY) && !(MAC_OS_SANDBOX)

#if !(MAC_OS_SANDBOX)
  #include "stdafx-mac-interop.h"
#else // !(MAC_OS_SANDBOX)
  #include "stdafx-mac-kext.h"
#endif // !(MAC_OS_SANDBOX)

#include "stdafx-mac-common.h"

#endif // MAC_OS_LIBRARY
