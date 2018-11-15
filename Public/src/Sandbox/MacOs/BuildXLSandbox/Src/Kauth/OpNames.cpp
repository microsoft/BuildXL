//
//  OpNames.cpp
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include "OpNames.hpp"

#define GET_OP_VALUE(name, value) value,
const char *OpNames[kOpMax] =
{
    FOR_ALL_OPERATIONS(GET_OP_VALUE)
};
