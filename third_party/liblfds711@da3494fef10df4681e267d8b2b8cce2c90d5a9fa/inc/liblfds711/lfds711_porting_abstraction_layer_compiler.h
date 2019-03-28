/****************************************************************************/
#if( defined __GNUC__ )
  // TRD : makes checking GCC versions much tidier
  #define LFDS711_PAL_GCC_VERSION ( __GNUC__ * 100 + __GNUC_MINOR__ * 10 + __GNUC_PATCHLEVEL__ )
#endif





/****************************************************************************/
#if( defined _MSC_VER && _MSC_VER >= 1400 )

  #ifdef LFDS711_PAL_COMPILER
    #error More than one porting abstraction layer matches the current platform in lfds711_porting_abstraction_layer_compiler.h
  #endif

  #define LFDS711_PAL_COMPILER

  #define LFDS711_PAL_COMPILER_STRING            "MSVC"

  #define LFDS711_PAL_ALIGN(alignment)           __declspec( align(alignment) )
  #define LFDS711_PAL_INLINE                     __forceinline

  #define LFDS711_PAL_BARRIER_COMPILER_LOAD      _ReadBarrier()
  #define LFDS711_PAL_BARRIER_COMPILER_STORE     _WriteBarrier()
  #define LFDS711_PAL_BARRIER_COMPILER_FULL      _ReadWriteBarrier()

  /* TRD : there are four processors to consider;

           . ARM32    (32 bit, ADD, CAS, DWCAS, EXCHANGE, SET) (defined _M_ARM)
           . Itanium  (64 bit, ADD, CAS,        EXCHANGE, SET) (defined _M_IA64)
           . x64      (64 bit, ADD, CAS, DWCAS, EXCHANGE, SET) (defined _M_X64 || defined _M_AMD64)
           . x86      (32 bit, ADD, CAS, DWCAS, EXCHANGE, SET) (defined _M_IX86)

           can't find any indications of 64-bit ARM support yet

           ARM has better intrinsics than the others, as there are no-fence variants

           in theory we also have to deal with 32-bit Windows on a 64-bit platform,
           and I presume we'd see the compiler properly indicate this in its macros,
           but this would require that we use 32-bit atomics on the 64-bit platforms,
           while keeping 64-bit cache line lengths and so on, and this is just so
           wierd a thing to do these days that it's not supported
  */

  #if( defined _M_ARM )
    #define LFDS711_PAL_BARRIER_PROCESSOR_LOAD   __dmb( _ARM_BARRIER_ISH )
    #define LFDS711_PAL_BARRIER_PROCESSOR_STORE  __dmb( _ARM_BARRIER_ISHST )
    #define LFDS711_PAL_BARRIER_PROCESSOR_FULL   __dmb( _ARM_BARRIER_ISH )

    #define LFDS711_PAL_ATOMIC_ADD( pointer_to_target, value, result, result_type )                                  \
    {                                                                                                                \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                             \
      (result) = (result_type) _InterlockedAdd_nf( (int long volatile *) (pointer_to_target), (int long) (value) );  \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                             \
    }

    #define LFDS711_PAL_ATOMIC_CAS( pointer_to_destination, pointer_to_compare, new_destination, cas_strength, result )                                                                                          \
    {                                                                                                                                                                                                            \
      lfds711_pal_uint_t                                                                                                                                                                                         \
        original_compare;                                                                                                                                                                                        \
                                                                                                                                                                                                                 \
      original_compare = (lfds711_pal_uint_t) *(pointer_to_compare);                                                                                                                                             \
                                                                                                                                                                                                                 \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                                                                                                         \
      *(lfds711_pal_uint_t *) (pointer_to_compare) = (lfds711_pal_uint_t) _InterlockedCompareExchange_nf( (long volatile *) (pointer_to_destination), (long) (new_destination), (long) *(pointer_to_compare) );  \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                                                                                                         \
                                                                                                                                                                                                                 \
      result = (char unsigned) ( original_compare == (lfds711_pal_uint_t) *(pointer_to_compare) );                                                                                                               \
    }

    #define LFDS711_PAL_ATOMIC_DWCAS( pointer_to_destination, pointer_to_compare, pointer_to_new_destination, cas_strength, result )                                                                        \
    {                                                                                                                                                                                                       \
      __int64                                                                                                                                                                                               \
        original_compare;                                                                                                                                                                                   \
                                                                                                                                                                                                            \
      original_compare = *(__int64 *) (pointer_to_compare);                                                                                                                                                 \
                                                                                                                                                                                                            \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                                                                                                    \
      *(__int64 *) (pointer_to_compare) = _InterlockedCompareExchange64_nf( (__int64 volatile *) (pointer_to_destination), *(__int64 *) (pointer_to_new_destination), *(__int64 *) (pointer_to_compare) );  \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                                                                                                    \
                                                                                                                                                                                                            \
      (result) = (char unsigned) ( *(__int64 *) (pointer_to_compare) == original_compare );                                                                                                                 \
    }

    #define LFDS711_PAL_ATOMIC_EXCHANGE( pointer_to_destination, exchange, exchange_type )                                            \
    {                                                                                                                                 \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                              \
      (exchange) = (exchange_type) _InterlockedExchange_nf( (int long volatile *) (pointer_to_destination), (int long) (exchange) );  \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                              \
    }

    #define LFDS711_PAL_ATOMIC_SET( pointer_to_destination, new_value )                                          \
    {                                                                                                            \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                         \
      (void) _InterlockedExchange_nf( (int long volatile *) (pointer_to_destination), (int long) (new_value) );  \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                         \
    }
  #endif

  #if( defined _M_IA64 )
    #define LFDS711_PAL_BARRIER_PROCESSOR_LOAD   __mf()
    #define LFDS711_PAL_BARRIER_PROCESSOR_STORE  __mf()
    #define LFDS711_PAL_BARRIER_PROCESSOR_FULL   __mf()

    #define LFDS711_PAL_ATOMIC_ADD( pointer_to_target, value, result, result_type )                                   \
    {                                                                                                                 \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                              \
      (result) = (result_type) _InterlockedAdd64_acq( (__int64 volatile *) (pointer_to_target), (__int64) (value) );  \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                              \
    }

    #define LFDS711_PAL_ATOMIC_CAS( pointer_to_destination, pointer_to_compare, new_destination, cas_strength, result )                                                                                                      \
    {                                                                                                                                                                                                                        \
      lfds711_pal_uint_t                                                                                                                                                                                                     \
        original_compare;                                                                                                                                                                                                    \
                                                                                                                                                                                                                             \
      original_compare = (lfds711_pal_uint_t) *(pointer_to_compare);                                                                                                                                                         \
                                                                                                                                                                                                                             \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                                                                                                                     \
      *(lfds711_pal_uint_t *) (pointer_to_compare) = (lfds711_pal_uint_t) _InterlockedCompareExchange64_acq( (__int64 volatile *) (pointer_to_destination), (__int64) (new_destination), (__int64) *(pointer_to_compare) );  \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                                                                                                                     \
                                                                                                                                                                                                                             \
      result = (char unsigned) ( original_compare == (lfds711_pal_uint_t) *(pointer_to_compare) );                                                                                                                           \
    }

    #define LFDS711_PAL_ATOMIC_EXCHANGE( pointer_to_destination, exchange, exchange_type )                                             \
    {                                                                                                                                  \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                               \
      (exchange) = (exchange_type) _InterlockedExchange64_acq( (__int64 volatile *) (pointer_to_destination), (__int64) (exchange) );  \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                               \
    }

    #define LFDS711_PAL_ATOMIC_SET( pointer_to_destination, new_value )                                           \
    {                                                                                                             \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                          \
      (void) _InterlockedExchange64_acq( (__int64 volatile *) (pointer_to_destination), (__int64) (new_value) );  \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                          \
    }
  #endif

  #if( defined _M_X64 || defined _M_AMD64 )
    #define LFDS711_PAL_BARRIER_PROCESSOR_LOAD   _mm_lfence()
    #define LFDS711_PAL_BARRIER_PROCESSOR_STORE  _mm_sfence()
    #define LFDS711_PAL_BARRIER_PROCESSOR_FULL   _mm_mfence()

    // TRD : no _InterlockedAdd64 for x64 - only the badly named _InterlockedExchangeAdd64, which is the same as _InterlockedAdd64 but returns the *original* value (which we must then add to before we return)
    #define LFDS711_PAL_ATOMIC_ADD( pointer_to_target, value, result, result_type )                                       \
    {                                                                                                                     \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                  \
      (result) = (result_type) _InterlockedExchangeAdd64( (__int64 volatile *) (pointer_to_target), (__int64) (value) );  \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                  \
      result += value;                                                                                                    \
    }

    #define LFDS711_PAL_ATOMIC_CAS( pointer_to_destination, pointer_to_compare, new_destination, cas_strength, result )                                                                                                  \
    {                                                                                                                                                                                                                    \
      lfds711_pal_uint_t                                                                                                                                                                                                 \
        original_compare;                                                                                                                                                                                                \
                                                                                                                                                                                                                         \
      original_compare = (lfds711_pal_uint_t) *(pointer_to_compare);                                                                                                                                                     \
                                                                                                                                                                                                                         \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                                                                                                                 \
      *(lfds711_pal_uint_t *) (pointer_to_compare) = (lfds711_pal_uint_t) _InterlockedCompareExchange64( (__int64 volatile *) (pointer_to_destination), (__int64) (new_destination), (__int64) *(pointer_to_compare) );  \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                                                                                                                 \
                                                                                                                                                                                                                         \
      result = (char unsigned) ( original_compare == (lfds711_pal_uint_t) *(pointer_to_compare) );                                                                                                                       \
    }

    #if( _MSC_VER >= 1500 )
      #define LFDS711_PAL_ATOMIC_DWCAS( pointer_to_destination, pointer_to_compare, pointer_to_new_destination, cas_strength, result )                                                                                                       \
      {                                                                                                                                                                                                                                      \
        LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                                                                                                                                   \
        (result) = (char unsigned) _InterlockedCompareExchange128( (__int64 volatile *) (pointer_to_destination), (__int64) (pointer_to_new_destination[1]), (__int64) (pointer_to_new_destination[0]), (__int64 *) (pointer_to_compare) );  \
        LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                                                                                                                                   \
      }
    #endif

    #define LFDS711_PAL_ATOMIC_EXCHANGE( pointer_to_destination, exchange, exchange_type )                                         \
    {                                                                                                                              \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                           \
      (exchange) = (exchange_type) _InterlockedExchange64( (__int64 volatile *) (pointer_to_destination), (__int64) (exchange) );  \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                           \
    }

    #define LFDS711_PAL_ATOMIC_SET( pointer_to_destination, new_value )                                       \
    {                                                                                                         \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                      \
      (void) _InterlockedExchange64( (__int64 volatile *) (pointer_to_destination), (__int64) (new_value) );  \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                      \
    }
  #endif

  #if( defined _M_IX86 )
    #define LFDS711_PAL_BARRIER_PROCESSOR_LOAD   lfds711_misc_force_store()
    #define LFDS711_PAL_BARRIER_PROCESSOR_STORE  lfds711_misc_force_store()
    #define LFDS711_PAL_BARRIER_PROCESSOR_FULL   lfds711_misc_force_store()

    // TRD : no _InterlockedAdd for x86 - only the badly named _InterlockedExchangeAdd, which is the same as _InterlockedAdd but returns the *original* value (which we must then add to before we return)
    #define LFDS711_PAL_ATOMIC_ADD( pointer_to_target, value, result, result_type )                                     \
    {                                                                                                                   \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                \
      (result) = (result_type) _InterlockedExchangeAdd( (__int64 volatile *) (pointer_to_target), (__int64) (value) );  \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                \
      result += value;                                                                                                  \
    }

    #define LFDS711_PAL_ATOMIC_CAS( pointer_to_destination, pointer_to_compare, new_destination, cas_strength, result )                                                                                       \
    {                                                                                                                                                                                                         \
      lfds711_pal_uint_t                                                                                                                                                                                      \
        original_compare;                                                                                                                                                                                     \
                                                                                                                                                                                                              \
      /* LFDS711_PAL_ASSERT( (pointer_to_destination) != NULL ); */                                                                                                                                           \
      /* LFDS711_PAL_ASSERT( (pointer_to_compare) != NULL ); */                                                                                                                                               \
      /* TRD : new_destination can be any value in its range */                                                                                                                                               \
      /* TRD : cas_strength can be any value in its range */                                                                                                                                                  \
      /* TRD : result can be any value in its range */                                                                                                                                                        \
                                                                                                                                                                                                              \
      original_compare = (lfds711_pal_uint_t) *(pointer_to_compare);                                                                                                                                          \
                                                                                                                                                                                                              \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                                                                                                      \
      *(lfds711_pal_uint_t *) (pointer_to_compare) = (lfds711_pal_uint_t) _InterlockedCompareExchange( (long volatile *) (pointer_to_destination), (long) (new_destination), (long) *(pointer_to_compare) );  \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                                                                                                      \
                                                                                                                                                                                                              \
      result = (char unsigned) ( original_compare == (lfds711_pal_uint_t) *(pointer_to_compare) );                                                                                                            \
    }

    #define LFDS711_PAL_ATOMIC_DWCAS( pointer_to_destination, pointer_to_compare, pointer_to_new_destination, cas_strength, result )                                                                     \
    {                                                                                                                                                                                                    \
      __int64                                                                                                                                                                                            \
        original_compare;                                                                                                                                                                                \
                                                                                                                                                                                                         \
      original_compare = *(__int64 *) (pointer_to_compare);                                                                                                                                              \
                                                                                                                                                                                                         \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                                                                                                 \
      *(__int64 *) (pointer_to_compare) = _InterlockedCompareExchange64( (__int64 volatile *) (pointer_to_destination), *(__int64 *) (pointer_to_new_destination), *(__int64 *) (pointer_to_compare) );  \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                                                                                                 \
                                                                                                                                                                                                         \
      (result) = (char unsigned) ( *(__int64 *) (pointer_to_compare) == original_compare );                                                                                                              \
    }

    #define LFDS711_PAL_ATOMIC_EXCHANGE( pointer_to_destination, exchange, exchange_type )                                         \
    {                                                                                                                              \
      /* LFDS711_PAL_ASSERT( (pointer_to_destination) != NULL ); */                                                                \
      /* LFDS711_PAL_ASSERT( (pointer_to_exchange) != NULL ); */                                                                   \
                                                                                                                                   \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                           \
      (exchange) = (exchange_type) _InterlockedExchange( (int long volatile *) (pointer_to_destination), (int long) (exchange) );  \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                           \
    }

    #define LFDS711_PAL_ATOMIC_SET( pointer_to_destination, new_value )                                       \
    {                                                                                                         \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                      \
      (void) _InterlockedExchange( (int long volatile *) (pointer_to_destination), (int long) (new_value) );  \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                      \
    }
  #endif

#endif





/****************************************************************************/
#if( defined __GNUC__ && LFDS711_PAL_GCC_VERSION >= 412 && LFDS711_PAL_GCC_VERSION < 473 )

  #ifdef LFDS711_PAL_COMPILER
    #error More than one porting abstraction layer matches the current platform in lfds711_porting_abstraction_layer_compiler.h
  #endif

  #define LFDS711_PAL_COMPILER

  #define LFDS711_PAL_COMPILER_STRING          "GCC < 4.7.3"

  #define LFDS711_PAL_ALIGN(alignment)         __attribute__( (aligned(alignment)) )
  #define LFDS711_PAL_INLINE                   inline

  static LFDS711_PAL_INLINE void lfds711_pal_barrier_compiler( void )
  {
    __asm__ __volatile__ ( "" : : : "memory" );
  }

  #define LFDS711_PAL_BARRIER_COMPILER_LOAD    lfds711_pal_barrier_compiler()
  #define LFDS711_PAL_BARRIER_COMPILER_STORE   lfds711_pal_barrier_compiler()
  #define LFDS711_PAL_BARRIER_COMPILER_FULL    lfds711_pal_barrier_compiler()

  #define LFDS711_PAL_BARRIER_PROCESSOR_LOAD   __sync_synchronize()
  #define LFDS711_PAL_BARRIER_PROCESSOR_STORE  __sync_synchronize()
  #define LFDS711_PAL_BARRIER_PROCESSOR_FULL   __sync_synchronize()

  #define LFDS711_PAL_ATOMIC_ADD( pointer_to_target, value, result, result_type )                                               \
  {                                                                                                                             \
    LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                          \
    (result) = (result_type) __sync_add_and_fetch( (lfds711_pal_uint_t *) (pointer_to_target), (lfds711_pal_uint_t) (value) );  \
    LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                          \
  }

  #define LFDS711_PAL_ATOMIC_CAS( pointer_to_destination, pointer_to_compare, new_destination, cas_strength, result )       \
  {                                                                                                                         \
    lfds711_pal_uint_t                                                                                                      \
      original_compare;                                                                                                     \
                                                                                                                            \
    original_compare = (lfds711_pal_uint_t) *(pointer_to_compare);                                                          \
                                                                                                                            \
    LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                      \
    *(pointer_to_compare) = __sync_val_compare_and_swap( pointer_to_destination, *(pointer_to_compare), new_destination );  \
    LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                      \
                                                                                                                            \
    result = (unsigned char) ( original_compare == (lfds711_pal_uint_t) *(pointer_to_compare) );                            \
  }

  #if( defined __x86_64__ )
    /* TRD : On 64 bit platforms, unsigned long long int is 64 bit, so we must manually use cmpxchg16b, 
             as the atomic intrinsics will only emit cmpxchg8b
    */

    // TRD : lfds711_pal_uint_t volatile (*destination)[2], lfds711_pal_uint_t (*compare)[2], lfds711_pal_uint_t (*new_destination)[2], enum lfds711_misc_cas_strength cas_strength, char unsigned result

    #define LFDS711_PAL_ATOMIC_DWCAS( pointer_to_destination, pointer_to_compare, pointer_to_new_destination, cas_strength, result )                             \
    {                                                                                                                                                            \
      (result) = 0;                                                                                                                                              \
                                                                                                                                                                 \
      __asm__ __volatile__                                                                                                                                       \
      (                                                                                                                                                          \
        "lock;"           /* make cmpxchg16b atomic        */                                                                                                    \
        "cmpxchg16b %0;"  /* cmpxchg16b sets ZF on success */                                                                                                    \
        "setz       %4;"  /* if ZF set, set result to 1    */                                                                                                    \
                                                                                                                                                                 \
        /* output */                                                                                                                                             \
        : "+m" ((pointer_to_destination)[0]), "+m" ((pointer_to_destination)[1]), "+a" ((pointer_to_compare)[0]), "+d" ((pointer_to_compare)[1]), "=q" (result)  \
                                                                                                                                                                 \
        /* input */                                                                                                                                              \
        : "b" ((pointer_to_new_destination)[0]), "c" ((pointer_to_new_destination)[1])                                                                           \
                                                                                                                                                                 \
        /* clobbered */                                                                                                                                          \
        :                                                                                                                                                        \
      );                                                                                                                                                         \
    }
  #endif

  // TRD : ARM and x86 have DWCAS which we can get via GCC intrinsics
  #if( defined __arm__ || defined __i686__ || defined __i586__ || defined __i486__ )
    #define LFDS711_PAL_ATOMIC_DWCAS( pointer_to_destination, pointer_to_compare, pointer_to_new_destination, cas_strength, result )                                                                                                   \
    {                                                                                                                                                                                                                                  \
      int long long unsigned                                                                                                                                                                                                           \
        original_destination;                                                                                                                                                                                                          \
                                                                                                                                                                                                                                       \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                                                                                                                               \
      original_destination = __sync_val_compare_and_swap( (int long long unsigned volatile *) (pointer_to_destination), *(int long long unsigned *) (pointer_to_compare), *(int long long unsigned *) (pointer_to_new_destination) );  \
      LFDS711_PAL_BARRIER_COMPILER_FULL;                                                                                                                                                                                               \
                                                                                                                                                                                                                                       \
      (result) = (char unsigned) ( original_destination == *(int long long unsigned *) (pointer_to_compare) );                                                                                                                         \
                                                                                                                                                                                                                                       \
      *(int long long unsigned *) (pointer_to_compare) = original_destination;                                                                                                                                                         \
    }
  #endif

  #define LFDS711_PAL_ATOMIC_EXCHANGE( pointer_to_destination, exchange, exchange_type )          \
  {                                                                                               \
    /* LFDS711_PAL_ASSERT( (pointer_to_destination) != NULL ); */                                 \
    /* TRD : exchange can be any value in its range */                                            \
    /* TRD : exchange_type can be any value in its range */                                       \
                                                                                                  \
    LFDS711_PAL_BARRIER_COMPILER_FULL;                                                            \
    (exchange) = (exchange_type) __sync_lock_test_and_set( pointer_to_destination, (exchange) );  \
    LFDS711_PAL_BARRIER_COMPILER_FULL;                                                            \
  }

  #define LFDS711_PAL_ATOMIC_SET( pointer_to_destination, new_value )        \
  {                                                                          \
    LFDS711_PAL_BARRIER_COMPILER_FULL;                                       \
    (void) __sync_lock_test_and_set( pointer_to_destination, (new_value) );  \
    LFDS711_PAL_BARRIER_COMPILER_FULL;                                       \
  }

#endif





/****************************************************************************/
#if( defined __GNUC__ && LFDS711_PAL_GCC_VERSION >= 473 )

  #ifdef LFDS711_PAL_COMPILER
    #error More than one porting abstraction layer matches the current platform in lfds711_porting_abstraction_layer_compiler.h
  #endif

  #define LFDS711_PAL_COMPILER

  #define LFDS711_PAL_COMPILER_STRING          "GCC >= 4.7.3"

  #define LFDS711_PAL_ALIGN(alignment)         __attribute__( (aligned(alignment)) )
  #define LFDS711_PAL_INLINE                   inline

  // TRD : GCC >= 4.7.3 compiler barriers are built into the intrinsics
  #define LFDS711_PAL_COMPILER_BARRIERS_MISSING_PRESUMED_HAVING_A_GOOD_TIME

  #define LFDS711_PAL_BARRIER_PROCESSOR_LOAD   __atomic_thread_fence( __ATOMIC_ACQUIRE )
  #define LFDS711_PAL_BARRIER_PROCESSOR_STORE  __atomic_thread_fence( __ATOMIC_RELEASE )
  #define LFDS711_PAL_BARRIER_PROCESSOR_FULL   __atomic_thread_fence( __ATOMIC_ACQ_REL )

  #define LFDS711_PAL_ATOMIC_ADD( pointer_to_target, value, result, result_type )                   \
  {                                                                                                 \
    (result) = (result_type) __atomic_add_fetch( (pointer_to_target), (value), __ATOMIC_RELAXED );  \
  }

  #define LFDS711_PAL_ATOMIC_CAS( pointer_to_destination, pointer_to_compare, new_destination, cas_strength, result )                                                       \
  {                                                                                                                                                                         \
    result = (char unsigned) __atomic_compare_exchange_n( pointer_to_destination, pointer_to_compare, new_destination, cas_strength, __ATOMIC_RELAXED, __ATOMIC_RELAXED );  \
  }

  // TRD : ARM and x86 have DWCAS which we can get via GCC intrinsics
  #if( defined __arm__ || defined __i686__ || defined __i586__ || defined __i486__ )
    #define LFDS711_PAL_ATOMIC_DWCAS( pointer_to_destination, pointer_to_compare, pointer_to_new_destination, cas_strength, result )                                                                                                                                                          \
    {                                                                                                                                                                                                                                                                                         \
      (result) = (char unsigned) __atomic_compare_exchange_n( (int long long unsigned volatile *) (pointer_to_destination), (int long long unsigned *) (pointer_to_compare), *(int long long unsigned *) (pointer_to_new_destination), (cas_strength), __ATOMIC_RELAXED, __ATOMIC_RELAXED );  \
    }
  #endif

  #if( defined __x86_64__ )
    /* TRD : On 64 bit platforms, unsigned long long int is 64 bit, so we must manually use cmpxchg16b, 
             as __sync_val_compare_and_swap() will only emit cmpxchg8b
    */

    // TRD : lfds711_pal_uint_t volatile (*destination)[2], lfds711_pal_uint_t (*compare)[2], lfds711_pal_uint_t (*new_destination)[2], enum lfds711_misc_cas_strength cas_strength, char unsigned result

    #define LFDS711_PAL_ATOMIC_DWCAS( pointer_to_destination, pointer_to_compare, pointer_to_new_destination, cas_strength, result )                             \
    {                                                                                                                                                            \
      (result) = 0;                                                                                                                                              \
                                                                                                                                                                 \
      __asm__ __volatile__                                                                                                                                       \
      (                                                                                                                                                          \
        "lock;"           /* make cmpxchg16b atomic        */                                                                                                    \
        "cmpxchg16b %0;"  /* cmpxchg16b sets ZF on success */                                                                                                    \
        "setz       %4;"  /* if ZF set, set result to 1    */                                                                                                    \
                                                                                                                                                                 \
        /* output */                                                                                                                                             \
        : "+m" ((pointer_to_destination)[0]), "+m" ((pointer_to_destination)[1]), "+a" ((pointer_to_compare)[0]), "+d" ((pointer_to_compare)[1]), "=q" (result)  \
                                                                                                                                                                 \
        /* input */                                                                                                                                              \
        : "b" ((pointer_to_new_destination)[0]), "c" ((pointer_to_new_destination)[1])                                                                           \
                                                                                                                                                                 \
        /* clobbered */                                                                                                                                          \
        :                                                                                                                                                        \
      );                                                                                                                                                         \
    }
  #endif

  #define LFDS711_PAL_ATOMIC_EXCHANGE( pointer_to_destination, exchange, exchange_type )                         \
  {                                                                                                              \
    (exchange) = (exchange_type) __atomic_exchange_n( (pointer_to_destination), (exchange), __ATOMIC_RELAXED );  \
  }

  #define LFDS711_PAL_ATOMIC_SET( pointer_to_destination, new_value )                       \
  {                                                                                         \
    (void) __atomic_exchange_n( (pointer_to_destination), (new_value), __ATOMIC_RELAXED );  \
  }

#endif





/****************************************************************************/
#if( !defined LFDS711_PAL_COMPILER )

  #error No matching porting abstraction layer in lfds711_porting_abstraction_layer_compiler.h

#endif

