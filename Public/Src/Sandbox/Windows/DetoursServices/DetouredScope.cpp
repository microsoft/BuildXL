// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "stdafx.h"

#include "DetouredScope.h"

// ----------------------------------------------------------------------------
// GLOBALS
// ----------------------------------------------------------------------------

__declspec(thread) size_t DetouredScope::gt_DetouredCount = 0;
