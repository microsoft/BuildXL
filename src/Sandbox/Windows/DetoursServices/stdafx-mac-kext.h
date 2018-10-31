// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

// stdafx-mac-kext.h : mac-specific stdafx only to be used for the kernel extension (which runs in kernel space)

#include <kern/assert.h>
#include <libkern/libkern.h>
#define wprintf(format, ...)
extern size_t wcslen(const wchar_t* str);
