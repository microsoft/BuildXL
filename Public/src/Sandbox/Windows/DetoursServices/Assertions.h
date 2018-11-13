// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

#pragma once

#ifdef NDEBUG
#define assert(e) ((void)0)
#else
#define assert(e) do { if (!(e)) { _fail_assert(); } } while(false)
void _fail_assert();
#endif