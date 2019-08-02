// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

#if !defined(MAC_OS_SANDBOX) && !defined(MAC_OS_LIBRARY)
// BuildXL should run on Win7+.
#include <WinSDKVer.h>
#define _WIN32_WINNT _WIN32_WINNT_WIN7
#include <SDKDDKVer.h>
#endif // !defined(MAC_OS)

