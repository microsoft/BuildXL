// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

int CallDetouredFileCreateWithSymlink();
int CallDetouredFileCreateWithNoSymlink();
int CallCreateSymLinkOnFiles();
int CallCreateSymLinkOnDirectories();
int CallAccessSymLinkOnFiles();
int CallCreateAndDeleteSymLinkOnFiles();
int CallMoveSymLinkOnFilesNotEnforceChainSymLinkAccesses();
int CallAccessSymLinkOnDirectories();
int CallDetouredFileCreateThatAccessesChainOfSymlinks();
int CallDetouredCopyFileFollowingChainOfSymlinks();
int CallDetouredCopyFileNotFollowingChainOfSymlinks();
int CallDetouredNtCreateFileThatAccessesChainOfSymlinks();
int CallAccessNestedSiblingSymLinkOnFiles();
int CallAccessJunctionSymlink_Real();
int CallAccessJunctionSymlink_Junction();
int CallAccessOnChainOfJunctions();
int CallDetouredAccessesCreateSymlinkForQBuild();
int CallDetouredCreateFileWForSymlinkProbeOnlyWithReparsePointFlag();
int CallDetouredCreateFileWForSymlinkProbeOnlyWithoutReparsePointFlag();
