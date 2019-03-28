// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef Stopwatch_hpp
#define Stopwatch_hpp

#include <IOKit/IOLib.h>
#include "BuildXLSandboxShared.hpp"

class Stopwatch
{
private:
    bool enabled_;
    uint64_t start_;
    uint64_t lastLap_;

    uint64_t time()
    {
        return enabled_ ? mach_absolute_time() : 0;
    }

public:

    Stopwatch(bool enabled = g_bxl_enable_counters) : enabled_(enabled)
    {
        reset();
    }

    void reset();
    Timespan lap();
};

#endif /* Stopwatch_hpp */
