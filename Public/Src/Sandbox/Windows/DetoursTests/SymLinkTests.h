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
int CallDetouredFileCreateThatDoesNotAccessChainOfSymlinks();
int CallDetouredCopyFileFollowingChainOfSymlinks();
int CallDetouredCopyFileNotFollowingChainOfSymlinks();
int CallDetouredNtCreateFileThatAccessesChainOfSymlinks();
int CallDetouredNtCreateFileThatDoesNotAccessChainOfSymlinks();
int CallAccessNestedSiblingSymLinkOnFiles();
int CallAccessJunctionSymlink_Real();
int CallAccessJunctionSymlink_Junction();
int CallAccessOnChainOfJunctions();
int CallDetouredAccessesCreateSymlinkForQBuild();
int CallDetouredCreateFileWForSymlinkProbeOnlyWithReparsePointFlag();
int CallDetouredCreateFileWForSymlinkProbeOnlyWithoutReparsePointFlag();
int CallDetouredCopyFileToExistingSymlinkFollowChainOfSymlinks();
int CallDetouredCopyFileToExistingSymlinkNotFollowChainOfSymlinks();
int CallProbeDirectorySymlink();
int CallProbeDirectorySymlinkTargetWithReparsePointFlag();
int CallProbeDirectorySymlinkTargetWithoutReparsePointFlag();
int CallValidateFileSymlinkAccesses();
int CallOpenFileThroughMultipleDirectorySymlinks();
int CallModifyDirectorySymlinkThroughDifferentPathIgnoreFullyResolve();
