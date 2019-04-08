// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "OpNames.hpp"

#define GET_OP_VALUE(name, value) value,
const char *OpNames[kOpMax] =
{
    FOR_ALL_OPERATIONS(GET_OP_VALUE)
};
