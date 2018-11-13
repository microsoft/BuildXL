// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

#pragma once

int CallDetouredFileCreateWithSymlink();
int CallDetouredFileCreateWithNoSymlink();
int CallCreateSymLinkOnFiles();
int CallCreateSymLinkOnDirectories();
int CallAccessSymLinkOnFiles();
int CallCreateAndDeleteSymLinkOnFiles();
int CallMoveSymLinkOnFilesEnforceChainSymLinkAccesses();
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
