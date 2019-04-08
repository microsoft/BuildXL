/****************************************************************************/
#if( defined _MSC_VER && defined _M_IX86 )

  #ifdef LFDS711_PAL_PROCESSOR
    #error More than one porting abstraction layer matches the current platform in "lfds711_porting_abstraction_layer_processor.h".
  #endif

  #define LFDS711_PAL_PROCESSOR

  typedef int long          lfds711_pal_int_t;
  typedef int long unsigned lfds711_pal_uint_t;

  #define LFDS711_PAL_PROCESSOR_STRING            "x86"

  #define LFDS711_PAL_ALIGN_SINGLE_POINTER        4
  #define LFDS711_PAL_ALIGN_DOUBLE_POINTER        8

  #define LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES   32

#endif





/****************************************************************************/
#if( defined _MSC_VER && (defined _M_X64 || defined _M_AMD64) )

  #ifdef LFDS711_PAL_PROCESSOR
    #error More than one porting abstraction layer matches the current platform in "lfds711_porting_abstraction_layer_processor.h".
  #endif

  #define LFDS711_PAL_PROCESSOR

  typedef int long long          lfds711_pal_int_t;
  typedef int long long unsigned lfds711_pal_uint_t;

  #define LFDS711_PAL_PROCESSOR_STRING            "x64"

  #define LFDS711_PAL_ALIGN_SINGLE_POINTER        8
  #define LFDS711_PAL_ALIGN_DOUBLE_POINTER        16

  // TRD : Intel bring over two cache lines at once, always, unless disabled in BIOS
  #define LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES   128

#endif





/****************************************************************************/
#if( defined _MSC_VER && defined _M_IA64 )

  #ifdef LFDS711_PAL_PROCESSOR
    #error More than one porting abstraction layer matches the current platform in "lfds711_porting_abstraction_layer_processor.h".
  #endif

  #define LFDS711_PAL_PROCESSOR

  typedef int long long          lfds711_pal_int_t;
  typedef int long long unsigned lfds711_pal_uint_t;

  #define LFDS711_PAL_PROCESSOR_STRING            "IA64"

  #define LFDS711_PAL_ALIGN_SINGLE_POINTER        8
  #define LFDS711_PAL_ALIGN_DOUBLE_POINTER        16

  #define LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES   64

#endif





/****************************************************************************/
#if( defined _MSC_VER && defined _M_ARM )

  #ifdef LFDS711_PAL_PROCESSOR
    #error More than one porting abstraction layer matches the current platform in "lfds711_porting_abstraction_layer_processor.h".
  #endif

  #define LFDS711_PAL_PROCESSOR

  typedef int long          lfds711_pal_int_t;
  typedef int long unsigned lfds711_pal_uint_t;

  #define LFDS711_PAL_PROCESSOR_STRING            "ARM (32-bit)"

  #define LFDS711_PAL_ALIGN_SINGLE_POINTER        4
  #define LFDS711_PAL_ALIGN_DOUBLE_POINTER        8

  /* TRD : ARM is LL/SC and uses a reservation granule of 8 to 2048 bytes
           so the isolation value used here is worst-case - be sure to set
           this correctly, otherwise structures are painfully large

           the test application has an argument, "-e", which attempts to
           determine the ERG length
  */

  #define LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES   2048

#endif
  
  
  
  
  
/****************************************************************************/
#if( defined __GNUC__ && defined __arm__ )

  #ifdef LFDS711_PAL_PROCESSOR
    #error More than one porting abstraction layer matches the current platform in "lfds711_porting_abstraction_layer_processor.h".
  #endif

  #define LFDS711_PAL_PROCESSOR

  typedef int long          lfds711_pal_int_t;
  typedef int long unsigned lfds711_pal_uint_t;

  #define LFDS711_PAL_PROCESSOR_STRING            "ARM (32-bit)"

  #define LFDS711_PAL_ALIGN_SINGLE_POINTER        4
  #define LFDS711_PAL_ALIGN_DOUBLE_POINTER        8

  /* TRD : ARM is LL/SC and uses a reservation granule of 8 to 2048 bytes
           so the isolation value used here is worst-case - be sure to set
           this correctly, otherwise structures are painfully large

           the test application has an argument, "-e", which attempts to
           determine the ERG length
  */

  #define LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES   2048

#endif





/****************************************************************************/
#if( defined __GNUC__ && defined __aarch64__ )

  #ifdef LFDS711_PAL_PROCESSOR
    #error More than one porting abstraction layer matches the current platform in "lfds711_porting_abstraction_layer_processor.h".
  #endif

  #define LFDS711_PAL_PROCESSOR

  typedef int long long          lfds711_pal_int_t;
  typedef int long long unsigned lfds711_pal_uint_t;

  #define LFDS711_PAL_PROCESSOR_STRING            "ARM (64-bit)"

  #define LFDS711_PAL_ALIGN_SINGLE_POINTER        8
  #define LFDS711_PAL_ALIGN_DOUBLE_POINTER        16

  /* TRD : ARM is LL/SC and uses a reservation granule of 8 to 2048 bytes
           so the isolation value used here is worst-case - be sure to set
           this correctly, otherwise structures are painfully large

           the test application has an argument, "-e", which attempts to
           determine the ERG length
  */

  #define LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES   2048

#endif





/****************************************************************************/
#if( defined __GNUC__ && (defined __i686__ || defined __i586__ || defined __i486__) )

  #ifdef LFDS711_PAL_PROCESSOR
    #error More than one porting abstraction layer matches the current platform in "lfds711_porting_abstraction_layer_processor.h".
  #endif

  #define LFDS711_PAL_PROCESSOR

  typedef int long          lfds711_pal_int_t;
  typedef int long unsigned lfds711_pal_uint_t;

  #define LFDS711_PAL_PROCESSOR_STRING            "x86"

  #define LFDS711_PAL_ALIGN_SINGLE_POINTER        4
  #define LFDS711_PAL_ALIGN_DOUBLE_POINTER        8

  #define LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES   32

#endif





/****************************************************************************/
#if( defined __GNUC__ && defined __x86_64__ )

  #ifdef LFDS711_PAL_PROCESSOR
    #error More than one porting abstraction layer matches the current platform in "lfds711_porting_abstraction_layer_processor.h".
  #endif

  #define LFDS711_PAL_PROCESSOR

  typedef int long long          lfds711_pal_int_t;
  typedef int long long unsigned lfds711_pal_uint_t;

  #define LFDS711_PAL_PROCESSOR_STRING            "x64"

  #define LFDS711_PAL_ALIGN_SINGLE_POINTER        8
  #define LFDS711_PAL_ALIGN_DOUBLE_POINTER        16

  #define LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES   128

#endif





/****************************************************************************/
#if( defined __GNUC__ && defined __alpha__ )

  #ifdef LFDS711_PAL_PROCESSOR
    #error More than one porting abstraction layer matches the current platform in "lfds711_porting_abstraction_layer_processor.h".
  #endif

  #define LFDS711_PAL_PROCESSOR

  typedef int long          lfds711_pal_int_t;
  typedef int long unsigned lfds711_pal_uint_t;

  #define LFDS711_PAL_PROCESSOR_STRING            "alpha"

  #define LFDS711_PAL_ALIGN_SINGLE_POINTER        8
  #define LFDS711_PAL_ALIGN_DOUBLE_POINTER        16

  #define LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES   64

#endif





/****************************************************************************/
#if( defined __GNUC__ && defined __ia64__ )

  #ifdef LFDS711_PAL_PROCESSOR
    #error More than one porting abstraction layer matches the current platform in "lfds711_porting_abstraction_layer_processor.h".
  #endif

  #define LFDS711_PAL_PROCESSOR

  typedef int long long          lfds711_pal_int_t;
  typedef int long long unsigned lfds711_pal_uint_t;

  #define LFDS711_PAL_PROCESSOR_STRING            "IA64"

  #define LFDS711_PAL_ALIGN_SINGLE_POINTER        8
  #define LFDS711_PAL_ALIGN_DOUBLE_POINTER        16

  #define LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES   64

#endif





/****************************************************************************/
#if( defined __GNUC__ && defined __mips__ && !defined __mips64 )

  #ifdef LFDS711_PAL_PROCESSOR
    #error More than one porting abstraction layer matches the current platform in "lfds711_porting_abstraction_layer_processor.h".
  #endif

  #define LFDS711_PAL_PROCESSOR

  typedef int long          lfds711_pal_int_t;
  typedef int long unsigned lfds711_pal_uint_t;

  #define LFDS711_PAL_PROCESSOR_STRING            "MIPS (32-bit)"

  #define LFDS711_PAL_ALIGN_SINGLE_POINTER        4
  #define LFDS711_PAL_ALIGN_DOUBLE_POINTER        8

  #define LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES   32

#endif





/****************************************************************************/
#if( defined __GNUC__ && defined __mips__ && defined __mips64 )

  #ifdef LFDS711_PAL_PROCESSOR
    #error More than one porting abstraction layer matches the current platform in "lfds711_porting_abstraction_layer_processor.h".
  #endif

  #define LFDS711_PAL_PROCESSOR

  typedef int long long          lfds711_pal_int_t;
  typedef int long long unsigned lfds711_pal_uint_t;

  #define LFDS711_PAL_PROCESSOR_STRING            "MIPS (64-bit)"

  #define LFDS711_PAL_ALIGN_SINGLE_POINTER        8
  #define LFDS711_PAL_ALIGN_DOUBLE_POINTER        16

  #define LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES   64

#endif





/****************************************************************************/
#if( defined __GNUC__ && defined __ppc__ )

  #ifdef LFDS711_PAL_PROCESSOR
    #error More than one porting abstraction layer matches the current platform in "lfds711_porting_abstraction_layer_processor.h".
  #endif

  #define LFDS711_PAL_PROCESSOR

  typedef int long          lfds711_pal_int_t;
  typedef int long unsigned lfds711_pal_uint_t;

  #define LFDS711_PAL_PROCESSOR_STRING            "POWERPC (32-bit)"

  #define LFDS711_PAL_ALIGN_SINGLE_POINTER        4
  #define LFDS711_PAL_ALIGN_DOUBLE_POINTER        8

  // TRD : this value is not very certain
  #define LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES   128

#endif





/****************************************************************************/
#if( defined __GNUC__ && defined __ppc64__ )

  #ifdef LFDS711_PAL_PROCESSOR
    #error More than one porting abstraction layer matches the current platform in "lfds711_porting_abstraction_layer_processor.h".
  #endif

  #define LFDS711_PAL_PROCESSOR

  typedef int long long          lfds711_pal_int_t;
  typedef int long long unsigned lfds711_pal_uint_t;

  #define LFDS711_PAL_PROCESSOR_STRING            "POWERPC (64-bit)"

  #define LFDS711_PAL_ALIGN_SINGLE_POINTER        8
  #define LFDS711_PAL_ALIGN_DOUBLE_POINTER        16

  // TRD : this value is not very certain
  #define LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES   128

#endif





/****************************************************************************/
#if( defined __GNUC__ && defined __sparc__ && !defined __sparc_v9__ )

  #ifdef LFDS711_PAL_PROCESSOR
    #error More than one porting abstraction layer matches the current platform in "lfds711_porting_abstraction_layer_processor.h".
  #endif

  #define LFDS711_PAL_PROCESSOR

  typedef int long          lfds711_pal_int_t;
  typedef int long unsigned lfds711_pal_uint_t;

  #define LFDS711_PAL_PROCESSOR_STRING            "SPARC (32-bit)"

  #define LFDS711_PAL_ALIGN_SINGLE_POINTER        4
  #define LFDS711_PAL_ALIGN_DOUBLE_POINTER        8

  #define LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES   32

#endif





/****************************************************************************/
#if( defined __GNUC__ && defined __sparc__ && defined __sparc_v9__ )

  #ifdef LFDS711_PAL_PROCESSOR
    #error More than one porting abstraction layer matches the current platform in "lfds711_porting_abstraction_layer_processor.h".
  #endif

  #define LFDS711_PAL_PROCESSOR

  typedef int long long          lfds711_pal_int_t;
  typedef int long long unsigned lfds711_pal_uint_t;

  #define LFDS711_PAL_PROCESSOR_STRING            "SPARC (64-bit)"

  #define LFDS711_PAL_ALIGN_SINGLE_POINTER        8
  #define LFDS711_PAL_ALIGN_DOUBLE_POINTER        16

  #define LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES   64

#endif





/****************************************************************************/
#if( defined __GNUC__ && defined __m68k__ )

  #ifdef LFDS711_PAL_PROCESSOR
    #error More than one porting abstraction layer matches the current platform in "lfds711_porting_abstraction_layer_processor.h".
  #endif

  #define LFDS711_PAL_PROCESSOR

  typedef int long          lfds711_pal_int_t;
  typedef int long unsigned lfds711_pal_uint_t;

  #define LFDS711_PAL_PROCESSOR_STRING            "680x0"

  #define LFDS711_PAL_ALIGN_SINGLE_POINTER        4
  #define LFDS711_PAL_ALIGN_DOUBLE_POINTER        8

  #define LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES   32

#endif





/****************************************************************************/
#if( !defined LFDS711_PAL_PROCESSOR )

  #error No matching porting abstraction layer in "lfds711_porting_abstraction_layer_processor.h".

#endif

