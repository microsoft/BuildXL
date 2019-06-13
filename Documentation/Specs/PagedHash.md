# Paged File Hashes
In the BuildXL [cache](../../Public/src/Cache/README.md) the most commonly used file content hash function is not a strict SHA256 of the file content stream, but a paged block hash, labeled as the "VSO-Hash" in the codebase. VSO-Hash originated as part of work within the Visual Studio Online services, which was renamed first to Visual Studio Team Services, then to Azure DevOps.

## Calculating Block and Page Hashes

1. Split the blob into blocks so that each block, except the last one, is exactly 2 MB. The last block is less than or equal to 2 MB.

1. For each block,
   1. Split it into 64 KB sized pages, except for the last page in the last block, which can be less than 64 KB.

   1. Perform SHA256 (https://en.wikipedia.org/wiki/SHA-2) algorithm on each page to obtain a page hash.

   1. Concatenate page hashes into a single byte array.

   1. Perform SHA256 algorithm on the byte array from above to obtain a *block hash*.

   1. Note, if the blob is empty, we still generate a block that is calculated by steps 2 and 4 against an empty byte array.

## Calculating the Blob ID Summary Hash
Calculate the *blob id* iteratively over the block hash array:

1. Initialize a `blobId` with ASCII bytes of fixed string "VSO Content Identifier Seed" (double quotes exclusive), which are [0x56, 0x53, 0x4F, 0x20, 0x43, 0x6F, 0x6E, 0x74, 0x65, 0x6E, 0x74, 0x20, 0x49, 0x64, 0x65, 0x6E, 0x74, 0x69, 0x66, 0x69, 0x65, 0x72, 0x20, 0x53, 0x65, 0x65, 0x64].

1. Concatenate the next block hash to `blobId`. 

1. If the one added in the step above is the last block hash, concatenate a byte of 0x1 (binary 00000001) to `blobId`; if not, concatenate a byte of 0x0 (binary 00000000) to `blobId`.

1. Perform SHA256 algorithm on the `blobId`, then overwrite `blodId` with the result. If there are more block hashes, go back to step 2.

1. Concatenate `blobId` with 0x00 to get the final *blob id*.

The result is a 33-byte VSO-Hash.

## Why Do It This Way?
A good implementation, such as linked below, can hash blocks and pages in parallel, saving significant end-to-end time on large files and overlapping the overhead of SHA256.

## Related Code
[VsoHash.cs](../../Public/src/Cache/ContentStore/Hashing/VsoHash.cs) is the primary implementation. It can perform asyncrhonous, parallel block and page hashing.
