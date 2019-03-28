This solution is intended as a demonstration test-bed for streaming
files over grpc.

The projects are:
 * ProtoGen defines the client/server contract. The NuGet packages
   it depends on cause shared types to be auto-generated
   based on the .proto file on compilation.
 * CopyServer defines a service implementation that
   streams the bytes of any file it is asked for.
 * CopyClient sends requests to CopyServer and puts
   the bytes it receives into its own working directory.

The basic debug scenario is to start CopyServer, then start
CopyClient and give it paths to files you want copied. The
server reports requests to it and the client reports
telemetry for each copy.

One aspect of this system which required significant work
was support for gzip compressed transmission. What made
this particularly hard was that GZipStream expects to write/read
to/from streams on compression/decompression, but grpc expects
implementors to write/read chunks on server/client side. This
required the introdution of BufferedWriteStream and
BufferedReadStream to convert between streams and chunks.
Compression reduces transmission times x3-x10 for large files.