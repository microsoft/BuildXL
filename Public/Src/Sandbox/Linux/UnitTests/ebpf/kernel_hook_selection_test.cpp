// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include <cstdlib>
#include "KernelHookSelection.hpp"

using buildxl::linux::ebpf::SelectSymlinkHook;
using buildxl::linux::ebpf::SymlinkHook;

static_assert(SelectSymlinkHook(true, false, false) == SymlinkHook::kPickLink);
static_assert(SelectSymlinkHook(true, true, false) == SymlinkHook::kPickLink);
static_assert(SelectSymlinkHook(true, false, true) == SymlinkHook::kPickLink);
static_assert(SelectSymlinkHook(true, true, true) == SymlinkHook::kPickLink);
static_assert(SelectSymlinkHook(false, true, true) == SymlinkHook::kFollowLink);
static_assert(SelectSymlinkHook(false, false, true) == SymlinkHook::kUnsupported);
static_assert(SelectSymlinkHook(false, true, false) == SymlinkHook::kUnsupported);
static_assert(SelectSymlinkHook(false, false, false) == SymlinkHook::kUnsupported);

int main()
{
    return EXIT_SUCCESS;
}
