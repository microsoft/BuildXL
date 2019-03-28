// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef SysCtl_hpp
#define SysCtl_hpp

#include <mach/mach_types.h>
#include <sys/systm.h>
#include <sys/types.h>
#include <sys/sysctl.h>

extern int g_bxl_enable_counters;
extern int g_bxl_verbose_logging;

void bxl_sysctl_register();
void bxl_sysctl_unregister();

#endif /* SysCtl_hpp */
