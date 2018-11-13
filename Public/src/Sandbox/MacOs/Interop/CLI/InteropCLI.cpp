//
//  InteropCLI.cpp
//  Interop
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include "FileAccessManifestParser.hpp"
#include "PolicyResult.h"

extern "C"
{
    #include "io.h"
}

int main(int argc, const char *argv[])
{
    if (argc < 2)
    {
        printf("USAGE: %s <path-to-fam>\n", argv[0]);
        exit(1);
    }

    Timestamps stamp;
    int errorno = GetTimeStampsForFilePath(argv[1], true, &stamp);
    printf("File found, read timestamps with return code: %u\n", errorno);

    FILE *file = fopen(argv[1], "r");
    if (file == NULL)
    {
        printf("ERROR: File '%s' not found.\n", argv[1]);
        exit(2);
    }
    
    fseek(file, 0, SEEK_END);
    
    // Get the current byte offset in the file
    size_t filelen = ftell(file);
    
    // Jump back to the beginning of the file
    rewind(file);
    
    char *buffer = (char *) malloc((filelen+1) *sizeof(char));
    fread(buffer, filelen, 1, file);
    fclose(file);
    
    // Parse file access manifest
    FileAccessManifestParseResult result = FileAccessManifestParseResult();
    bool initialized = result.init(buffer, (int)filelen);
    if (!initialized)
    {
        printf("ERROR parsing FileAccessManifest: %s\n", result.Error());
        exit(3);
    }

    // Print loaded FAM
    FileAccessManifestParseResult::PrintManifestTree(result.GetManifestRootNode());
    
    // Find policy for a given path
    const char *path = "/bin/bash";
    PolicySearchCursor cursor = FindFileAccessPolicyInTreeEx(result.GetUnixRootNode(), path+1, strlen(path+1));
    
    printf("Cursor for path '%s' :: was trucated: %s, record: %s, node policy: %d, cone policy: %d\n",
           path,
           cursor.SearchWasTruncated ? "true" : "false",
           cursor.Record->GetPartialPath(),
           cursor.Record->GetNodePolicy(),
           cursor.Record->GetConePolicy());
    
    // Check some file access against the policy
    PolicyResult policyResult = PolicyResult(result.GetFamFlags(), path, cursor);
    AccessCheckResult accessCheck = policyResult.CheckWriteAccess();
    
    printf("Access check :: denied: %s, should report: %s\n",
           accessCheck.ShouldDenyAccess() ? "true" : "false",
           accessCheck.ShouldReport() ? "true" : "false");

    free(buffer);
    
    return 0;
}

