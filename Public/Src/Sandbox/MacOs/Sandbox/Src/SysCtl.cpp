// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "SysCtl.hpp"

#if DEBUG
int g_bxl_verbose_logging = 1;
#else
int g_bxl_verbose_logging = 0;
#endif

int g_bxl_enable_cache = 1;
int g_bxl_enable_counters = 1;	
int g_bxl_enable_light_trie = 1;

// for caching to be disabled for a pip, it must have at least 20000 entries and no more than 20% cache hit rate
int g_bxl_disable_cache_min_entries = 20000;
int g_bxl_disable_cache_max_hit_pct = 20;

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
           g_bxl_verbose_logging,
           "Enable/Disable verbose logging");

SYSCTL_INT(_kern,
           OID_AUTO,
           bxl_enable_cache,
           CTLFLAG_RW,
           &g_bxl_enable_cache,
           g_bxl_enable_cache,
           "Enable/Disable access report caching");

SYSCTL_INT(_kern,
           OID_AUTO,
           bxl_enable_light_trie,
           CTLFLAG_RW,
           &g_bxl_enable_light_trie,
           g_bxl_enable_light_trie,
           "Enable/Disable light trie implementation (slighly slower, but uses way less memory)");

SYSCTL_INT(_kern,
           OID_AUTO,
           bxl_disable_cache_min_entries,
           CTLFLAG_RW,
           &g_bxl_disable_cache_min_entries,
           g_bxl_disable_cache_min_entries,
           "For pip caching to be disabled, the cache must have at least this many entries");

SYSCTL_INT(_kern,
           OID_AUTO,
           bxl_disable_cache_max_hit_pct,
           CTLFLAG_RW,
           &g_bxl_disable_cache_max_hit_pct,
           g_bxl_disable_cache_max_hit_pct,
           "For pip caching to be disabled, its cache hit rate must be less than this percent");

void bxl_sysctl_register()
{
    sysctl_register_oid(&sysctl__kern_bxl_enable_counters);
    sysctl_register_oid(&sysctl__kern_bxl_verbose_logging);
    sysctl_register_oid(&sysctl__kern_bxl_enable_cache);
    sysctl_register_oid(&sysctl__kern_bxl_enable_light_trie);
    sysctl_register_oid(&sysctl__kern_bxl_disable_cache_min_entries);
    sysctl_register_oid(&sysctl__kern_bxl_disable_cache_max_hit_pct);
}

void bxl_sysctl_unregister()
{
    sysctl_unregister_oid(&sysctl__kern_bxl_enable_counters);
    sysctl_unregister_oid(&sysctl__kern_bxl_verbose_logging);
    sysctl_unregister_oid(&sysctl__kern_bxl_enable_cache);
    sysctl_unregister_oid(&sysctl__kern_bxl_enable_light_trie);
    sysctl_unregister_oid(&sysctl__kern_bxl_disable_cache_min_entries);
    sysctl_unregister_oid(&sysctl__kern_bxl_disable_cache_max_hit_pct);
}
