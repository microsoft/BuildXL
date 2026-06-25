// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BlobDaemon from "Sdk.BlobDaemon";
import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

// Example: upload a build output to Azure Blob Storage with BlobDaemon.
//
// BlobDaemon is a BuildXL service pip that uploads files/directories produced by a build to Azure Blob Storage.
// This example shows the end-to-end shape:
//   1. A process pip produces a file (the "build output").
//   2. BlobDaemon is started as a service.
//   3. The produced file is handed to BlobDaemon, which uploads it to a container in a storage account.
//
// Authentication: BlobDaemon authenticates to Azure Storage with a Microsoft Entra (Azure AD) bearer token.
// The token is read at runtime from an environment variable (see 'authEnvVar' below); the calling pipeline mints
// it (e.g. via the EntraAuthenticate task) and forwards it to the daemon. The identity behind the token must hold
// the 'Storage Blob Data Contributor' role on the target storage account.

const isWindows = Context.isWindowsOS();

// Name of the environment variable that holds the Microsoft Entra bearer token used to authenticate against
// Azure Storage. The pipeline mints this token (EntraAuthenticate) and forwards it to the daemon.
// NOTE: This must match the env var name set in the pipeline (blobdaemon.yml).
const authEnvVar = "BLOB_AUTH_TOKEN";

// Target storage account URL (e.g. https://<account>.blob.core.windows.net) and container.
// These are provided by the pipeline via environment variables. When running locally for graph construction
// they are not set, so we fall back to placeholders (the upload pip won't be executed in that case).
const accountUrl = Environment.hasVariable("BLOB_ACCOUNT_URL")
    ? Environment.getStringValue("BLOB_ACCOUNT_URL")
    : "https://placeholder.blob.core.windows.net";

const containerName = Environment.hasVariable("BLOB_CONTAINER")
    ? Environment.getStringValue("BLOB_CONTAINER")
    : "blobdaemon-validation";

// Shell tool used to run the producer script. Cross-platform so the build can run on either OS.
const shellTool: Transformer.ToolDefinition = {
    exe: isWindows ? f`${Environment.getPathValue("COMSPEC")}` : f`/bin/bash`,
    dependsOnCurrentHostOSDirectories: true,
    prepareTempDirectory: true,
};

// A single process pip that produces one file. The producer is a tiny checked-in script that writes to the path
// passed as its first argument. The output path is provided via Artifact.output, which renders the real path and
// declares the output (we never interpolate paths into command strings).
function produceFile() : File {
    const outDir = Context.getNewOutputDirectory("blobtest");
    const outFile = p`${outDir}/hello.txt`;

    const result = Transformer.execute({
        tool: shellTool,
        workingDirectory: outDir,
        arguments: isWindows
            ? [
                Cmd.argument("/d"),
                Cmd.argument("/c"),
                Cmd.argument(Artifact.input(f`produce.cmd`)),
                Cmd.argument(Artifact.output(outFile)),
              ]
            : [
                Cmd.argument(Artifact.input(f`produce.sh`)),
                Cmd.argument(Artifact.output(outFile)),
              ],
    });

    return result.getOutputFile(outFile);
}

const fileToUpload = produceFile();

// Start the BlobDaemon service. The auth-token env var is forwarded so the daemon can read it at runtime.
const service = BlobDaemon.runner.startDaemon({
    forwardEnvironmentVars: [ authEnvVar ],
});

// Upload the produced file to '<container>/blobdaemon-validation/hello.txt'. uploadArtifacts schedules an IPC pip
// that hands the file to the running daemon; the daemon authenticates with the forwarded token and uploads it.
@@public
export const upload = BlobDaemon.runner.uploadArtifacts(
    service,
    {},
    [
        {
            kind: "file",
            file: fileToUpload,
            uploadLocation: {
                kind: "container",
                accountName: accountUrl,
                containerName: containerName,
                uploadPath: r`blobdaemon-validation/hello.txt`,
            },
            authEnvironmentVariable: authEnvVar,
        }
    ]
);
