// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "Stopwatch.hpp"

void Stopwatch::reset()
{
    start_ = lastLap_ = time();
}

Timespan Stopwatch::lap()
{
    uint64_t newLap = time();
    Timespan duration = Timespan::fromNanoseconds(newLap - lastLap_);
    lastLap_ = newLap;
    return duration;
}
