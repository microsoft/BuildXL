// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "ResourceManager.hpp"

#define super OSObject

OSDefineMetaClassAndStructors(ResourceManager, OSObject)

ResourceManager* ResourceManager::create(ResourceCounters *counters)
{
    ResourceManager *instance = new ResourceManager;
    if (instance != nullptr)
    {
        if (!instance->init(counters))
        {
            OSSafeReleaseNULL(instance);
        }
    }
    
    return instance;
}

bool ResourceManager::init(ResourceCounters *counters)
{
    if (!super::init())
    {
        return false;
    }

    thresholds_ =
    {
        .cpuUsageBlock     = {0},
        .cpuUsageWakeup    = {0},
        .minAvailableRamMB = 0,
    };

    counters_ = counters;

    procBarrier_ = IOLockAlloc();
    if (procBarrier_ == nullptr)
    {
        return false;
    }

    return true;
}

void ResourceManager::free()
{
    if (procBarrier_ != nullptr)
    {
        IOLockWakeup(procBarrier_, this, /*oneThread*/ false);
        IOLockFree(procBarrier_);
        procBarrier_ = nullptr;
    }

    super::free();
}

static inline bool isThresholdValid(percent percent)
{
    return percent.value > 0 && percent.value < 100;
}

static bool isBelowThreshold(basis_points value, percent threshold)
{
    return value.value < threshold.value * 100;
}

static bool shouldThrottle(basis_points value, percent threshold)
{
    return isThresholdValid(threshold) && !isBelowThreshold(value, threshold);
}

inline bool ResourceManager::shouldThrottleProcesses() const
{
    return
        counters_->availableRamMB < thresholds_.minAvailableRamMB ||
        shouldThrottle(counters_->cpuUsage, thresholds_.cpuUsageBlock);
}

inline bool ResourceManager::IsProcessThrottlingEnabled() const
{
    return
        thresholds_.minAvailableRamMB > 0 ||
        isThresholdValid(thresholds_.cpuUsageBlock);
}

void ResourceManager::UpdateNumTrackedProcesses(uint newCount)
{
    uint oldCount = counters_->numTrackedProcesses;
    OSCompareAndSwap(oldCount, newCount, &counters_->numTrackedProcesses);
    if (newCount < oldCount)
    {
        wakeupBlockedProcesses(/* justOne */ newCount == oldCount - 1);
    }
}

void ResourceManager::UpdateCpuUsage(basis_points cpuUsage)
{
    basis_points oldCpuUsage = counters_->cpuUsage;
    OSCompareAndSwap(oldCpuUsage.value, cpuUsage.value, &counters_->cpuUsage.value);
    if (isBelowThreshold(cpuUsage, thresholds_.GetCpuUsageForWakeup()))
    {
        wakeupBlockedProcesses(/* justOne */ true);
    }
}

void ResourceManager::UpdateAvailableRam(uint availableRamMB)
{
    uint oldRam = counters_->availableRamMB;
    OSCompareAndSwap(oldRam, availableRamMB, &counters_->availableRamMB);
    if (availableRamMB > oldRam)
    {
        wakeupBlockedProcesses(/* justOne */ true);
    }
}

void ResourceManager::WaitForCpu()
{
    if (!IsProcessThrottlingEnabled())
    {
        return;
    }
    
    if (shouldThrottleProcesses())
    {
        IOLockLock(procBarrier_);
        while (shouldThrottleProcesses())
        {
            OSIncrementAtomic(&counters_->numBlockedProcesses);
            IOLockSleep(procBarrier_, this, THREAD_INTERRUPTIBLE);
            OSDecrementAtomic(&counters_->numBlockedProcesses);
        }
        IOLockUnlock(procBarrier_);
    }
}

void ResourceManager::wakeupBlockedProcesses(bool justOne)
{
    if (procBarrier_ != nullptr && counters_->numBlockedProcesses > 0 && !shouldThrottleProcesses())
    {
        IOLockWakeup(procBarrier_, this, justOne);
    }
}
