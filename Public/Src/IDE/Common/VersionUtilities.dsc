// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

namespace IDE.VersionUtilities {

    /*
     * In the plugin's package.json, the version field is set to 0.0.0.
     * This function updates that to the given version string.
     * (e.g. "version": "0.0.0" --> "version": "20171101.1.2")
     * 
     * Note: The reason for it being set to 0.0.0 is to have a semver-compliant version
     * to allow debugging locally.
     */
    export function updateVersion(version: string, file: SourceFile) : DerivedFile
    {
        const brandingDirectory = Context.getNewOutputDirectory("branding");

        let content = File.readAllText(file, TextEncoding.utf8);
        
        content = content.replace("0.0.0", version);
        let newFile = Transformer.writeAllText(p`${brandingDirectory}/${file.name}`, content);

        return newFile;
    }
    
}
