// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

#ifdef NDEBUG
#define assert(e) ((void)0)
#else
#define assert(e) do { if (!(e)) { _fail_assert(); } } while(false)
void _fail_assert();
#endif
