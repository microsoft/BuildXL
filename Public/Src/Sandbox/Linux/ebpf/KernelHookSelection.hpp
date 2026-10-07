// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#pragma once

namespace buildxl::linux::ebpf {

enum class SymlinkHook {
    kPickLink,
    kFollowLink,
    kUnsupported,
};

constexpr SymlinkHook SelectSymlinkHook(bool has_pick_link, bool has_step_into, bool has_follow_link)
{
    if (has_pick_link) {
        return SymlinkHook::kPickLink;
    }

    if (has_step_into && has_follow_link) {
        return SymlinkHook::kFollowLink;
    }

    return SymlinkHook::kUnsupported;
}

} // namespace buildxl::linux::ebpf
