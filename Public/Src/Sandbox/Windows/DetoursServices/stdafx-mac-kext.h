// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// stdafx-mac-kext.h : mac-specific stdafx only to be used for the kernel extension (which runs in kernel space)

#include <kern/assert.h>
#include <libkern/libkern.h>
#define wprintf(format, ...)
extern size_t wcslen(const wchar_t* str);
