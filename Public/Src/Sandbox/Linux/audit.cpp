// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include <link.h>
#include "bxl_observer.hpp"

// https://man7.org/linux/man-pages/man7/rtld-audit.7.html

/**
 * When invoking this function, the dynamic linker passes, in version, the highest version
 * of the auditing interface that the linker supports.  If necessary, the auditing library
 * can check that this version is sufficient for its requirements.  In our case, we just
 * return the same version.
 * 
 * @param version The highest version of the auditing interface that the linker supports.
 * @return The version of the auditing interface that this auditing library expects to use.
 */
unsigned int la_version(unsigned int version)
{
    return version;
}

/**
 * The dynamic linker calls this function when a new shared object is loaded.
 * The map argument is a pointer to a link-map structure that describes the object.
 * 
 * Our implementation of this callback just forwards this call to the BxlObserver singleton instance.
 * 
 * @return A bit mask specifying whether symbol bindings for this object should be audited.
 */
unsigned int la_objopen(struct link_map *map, Lmid_t lmid, uintptr_t *cookie)
{
    // report if path is set
    if (map->l_name && *map->l_name == '/')
    {
        BxlObserver::GetInstance()->report_audit_objopen(map->l_name);
    }

    return 0; // disable symbol auditing; to enable, return LA_FLG_BINDTO | LA_FLG_BINDFROM;
}