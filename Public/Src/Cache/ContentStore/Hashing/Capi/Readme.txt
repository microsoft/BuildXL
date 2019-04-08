This files were taken from .net source code and slimmed to include only what
is needed for computing the SHA 256 hash in CloudStore.  .net allocated a new
byte[] every time HashCore is called, which is intensive on the GC.  Our
version gets rid of that allocation.
