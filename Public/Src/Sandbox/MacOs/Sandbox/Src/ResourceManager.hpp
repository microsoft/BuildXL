// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef ResourceManager_hpp
#define ResourceManager_hpp

#include <IOKit/IOService.h>
#include <IOKit/IOLib.h>
#include "BuildXLSandboxShared.hpp"

#define ResourceManager BXL_CLASS(ResourceManager)

/*!
 * This class is where resource usage information is collected and where all the decisions are made
 * regarding any throttling due to insufficient available resources.
 *
 * This class relies on being externally notified whenever
 *   - number of tracked processes changed (see 'UpdateNumTrackedProcesses')
 *   - CPU/RAM usage changed (see 'Update{Cpu|Ram}Usage')
 */
class ResourceManager : public OSObject
{
private:

    OSDeclareDefaultStructors(ResourceManager)

    IOLock *procBarrier_;
    ResourceThresholds thresholds_;

    /*!
     * Shared counters (with all other clients) for counting the number of active/pending/blocked processes.
     *
     * NOTE: must use atomic increments to update these counters.
     */
    ResourceCounters *counters_;

    /*!
     * Wakes up one or more blocked processes if the throttling condition
     * (see 'shouldThrottleProcesses') is not met any longer.
     *
     * @param justOne Whether to wake up one or all blocked processes.
     */
    void wakeupBlockedProcesses(bool justOne);

    /*!
     * Returns whether the condition for throttling processes is met, which is:
     *   - current CPU usage is greater or equal than the cpu usage threshold, AND
     *   - current number of tracked processes is greater or equal than the tracked processes threshold.
     */
    bool shouldThrottleProcesses() const;

protected:

    bool init(ResourceCounters *counters);
    void free() override;

public:

    inline ResourceThresholds GetThresholds() const { return thresholds_; }

    /*!
     * Returns whether process throttling is enabled; process throttling is enabled when:
     *   - CPU usage threshold is set to a value from [1..99], AND
     *   - tracked processes threshold is set to a value greater than 0.
     */
    bool IsProcessThrottlingEnabled() const;

    /*!
     * Should be called once upon creation to set the thresholds.
     * If not called at all, the default threasholds amount to no throttling.
     */
    void SetThresholds(ResourceThresholds thresholds) { thresholds_ = thresholds; }

    /*!
     * Should be called whenever the number of tracked processes changed.
     */
    void UpdateNumTrackedProcesses(uint count);

    /*!
     * Should be called at steady intervals to continuously update the current CPU usage (in basis points).
     */
    void UpdateCpuUsage(basis_points cpuUsage);

    /*!
     * Should be called at steady intervals to continuously update the current RAM usage (in basis points).
     */
    void UpdateAvailableRam(uint availableRamMB);

    /*!
     * Blocks the current thread if 'IsProcessThrottlingEnabled()' and 'ShouldThrottleProcesses()' are both true.
     *
     * The blocked thread will be awakened whenever that condition changes.
     *
     * NOTE: should not be called from an interrupt routine, or everything will grind to a halt.
     */
    void WaitForCpu();

    /*!
     * Factory method.
     * @return New instance or NULL.
     */
    static ResourceManager* create(ResourceCounters *counters);
};

#endif /* ResourceManager_hpp */
