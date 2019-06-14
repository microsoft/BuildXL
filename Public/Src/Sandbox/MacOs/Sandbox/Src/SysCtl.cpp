// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "SysCtl.hpp"

#if DEBUG
int g_bxl_enable_counters = 1;
int g_bxl_verbose_logging = 1;
#else
int g_bxl_enable_counters = 0;
int g_bxl_verbose_logging = 0;
#endif

SYSCTL_INT(_kern,                               // parent
           OID_AUTO,                            // oid
           bxl_enable_counters,                 // name
           CTLFLAG_RW,                          // flags
           &g_bxl_enable_counters,              // pointer to variable
           g_bxl_enable_counters,               // default value
           "Enable/Disable various counters");  // description

SYSCTL_INT(_kern,
           OID_AUTO,
           bxl_verbose_logging,
           CTLFLAG_RW,
           &g_bxl_verbose_logging,
           0,
           "Enable/Disable verbose logging");

void bxl_sysctl_register()
{
    sysctl_register_oid(&sysctl__kern_bxl_enable_counters);
    sysctl_register_oid(&sysctl__kern_bxl_verbose_logging);
}

void bxl_sysctl_unregister()
{
    sysctl_unregister_oid(&sysctl__kern_bxl_enable_counters);
    sysctl_unregister_oid(&sysctl__kern_bxl_verbose_logging);
}
