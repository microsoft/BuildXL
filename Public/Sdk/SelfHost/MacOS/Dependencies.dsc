// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

// Defaults to empty, on Windows we don't add these dependencies at all
const userName = Environment.getStringValue("USER") || "";

const applications = Transformer.sealSourceDirectory(d`/Applications`, Transformer.SealSourceDirectoryOption.allDirectories);
const usrBin = Transformer.sealSourceDirectory(d`/usr/bin`, Transformer.SealSourceDirectoryOption.allDirectories);
const usrInclude = Transformer.sealSourceDirectory(d`/usr/include`, Transformer.SealSourceDirectoryOption.allDirectories);
const usrLib = Transformer.sealSourceDirectory(d`/usr/lib`, Transformer.SealSourceDirectoryOption.allDirectories);
const library = Transformer.sealSourceDirectory(d`/Library`, Transformer.SealSourceDirectoryOption.allDirectories);
const provisioning = Transformer.sealSourceDirectory(d`/Users/${userName}/Library/MobileDevice/Provisioning Profiles`, Transformer.SealSourceDirectoryOption.allDirectories);

@@public
export const systemFolderInputDeps = [
    applications,
    usrBin,
    usrInclude,
    usrLib,
    library,
    provisioning
];

const userLibPref = d`/Users/${userName}/Library/Preferences`;

@@public
export const untrackedSystemFolderDeps = [
    d`/bin`,
    d`/dev`,
    d`/private`,
    d`/sbin`,
    d`/System/Library`,
    d`/usr/libexec`,
    d`/usr/share`,
    d`/usr/standalone`,
    d`/usr/sbin`,
    d`/var`,
    userLibPref
];


// login.keychain is created by the OS the first time any process invokes an OS API that references the keychain.
// Untracking because build state will not be stored there and codesigning will fail if required certs are in the keychain
const loginKeychainFiles = [
    f`/Users/${userName}/Library/Keychains/login.keychain-db`,
    f`/Users/${userName}/Library/Keychains/login.keychain`
];

const xcodeIDEInteractivityHistory = f`/Users/${userName}/Library/Developer/Xcode/UserData/IDEEditorInteractivityHistory`;
const userTextEncodingFile = f`/Users/${userName}/.CFUserTextEncoding`;

@@public
export const filesAndSymlinkInputDeps = [
    f`/etc`, // Symlink to /private/etc
    f`/tmp`, // Symling to /private/tmp
];

@@public
export const untrackedFiles = [
    ...loginKeychainFiles,
    xcodeIDEInteractivityHistory,
    userTextEncodingFile
];