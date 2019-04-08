# Ctrl-C Handling in BuildXL

BuildXL captures Ctrl-C to cancel a running build. There are two behaviors depending on how many times it is pressed.

## Ctrl-C 
A single or multiple keystroke combination will cause BuildXL to attempt to cleanly shut down. Specifically, this means:
* The cache will be shut down gracefully to avoid a costlier startup on the next build
* (MS internal) Telemetry is flushed
* Log files are flushed

All running child processes will be immediately shut down via BuildXL stopping their owning Job Object. The build will **NOT** wait for those processes to exit on their own.

## Ctrl-Break 

The Ctrl-Break keystroke will cause a more immediate process shutdown. BuildXL will attempt to exit as quickly as possible without regard for cleanly shutting down the items above. This may incur a greater startup cost on the next build because the cache may need to reconstruct its metadata by scanning the disk for files.
