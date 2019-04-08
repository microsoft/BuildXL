/***** public prototypes *****/
#include "../inc/liblfds711.h"

/***** defines *****/
#define and &&
#define or  ||

#define NO_FLAGS 0x0

#define LFDS711_VERSION_STRING   "7.1.1"
#define LFDS711_VERSION_INTEGER  711

#if( defined KERNEL_MODE )
  #define MODE_TYPE_STRING "kernel-mode"
#endif

#if( !defined KERNEL_MODE )
  #define MODE_TYPE_STRING "user-mode"
#endif

#if( defined NDEBUG && !defined COVERAGE && !defined TSAN && !defined PROF )
  #define BUILD_TYPE_STRING "release"
#endif

#if( !defined NDEBUG && !defined COVERAGE && !defined TSAN && !defined PROF )
  #define BUILD_TYPE_STRING "debug"
#endif

#if( !defined NDEBUG && defined COVERAGE && !defined TSAN && !defined PROF )
  #define BUILD_TYPE_STRING "coverage"
#endif

#if( !defined NDEBUG && !defined COVERAGE && defined TSAN && !defined PROF )
  #define BUILD_TYPE_STRING "threadsanitizer"
#endif

#if( !defined NDEBUG && !defined COVERAGE && !defined TSAN && defined PROF )
  #define BUILD_TYPE_STRING "profiling"
#endif

#define LFDS711_BACKOFF_INITIAL_VALUE  0
#define LFDS711_BACKOFF_LIMIT          10

#define LFDS711_BACKOFF_EXPONENTIAL_BACKOFF( backoff_state, backoff_iteration )                \
{                                                                                              \
  lfds711_pal_uint_t volatile                                                                  \
    loop;                                                                                      \
                                                                                               \
  lfds711_pal_uint_t                                                                           \
    endloop;                                                                                   \
                                                                                               \
  if( (backoff_iteration) == LFDS711_BACKOFF_LIMIT )                                           \
    (backoff_iteration) = LFDS711_BACKOFF_INITIAL_VALUE;                                       \
  else                                                                                         \
  {                                                                                            \
    endloop = ( ((lfds711_pal_uint_t) 0x1) << (backoff_iteration) ) * (backoff_state).metric;  \
    for( loop = 0 ; loop < endloop ; loop++ );                                                 \
  }                                                                                            \
                                                                                               \
  (backoff_iteration)++;                                                                       \
}

#define LFDS711_BACKOFF_AUTOTUNE( bs, backoff_iteration )                                                                           \
{                                                                                                                                   \
  if( (backoff_iteration) < 2 )                                                                                                     \
    (bs).backoff_iteration_frequency_counters[(backoff_iteration)]++;                                                               \
                                                                                                                                    \
  if( ++(bs).total_operations >= 10000 and (bs).lock == LFDS711_MISC_FLAG_LOWERED )                                                 \
  {                                                                                                                                 \
    char unsigned                                                                                                                   \
      result;                                                                                                                       \
                                                                                                                                    \
    lfds711_pal_uint_t LFDS711_PAL_ALIGN(LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES)                                                     \
      compare = LFDS711_MISC_FLAG_LOWERED;                                                                                          \
                                                                                                                                    \
    LFDS711_PAL_ATOMIC_CAS( &(bs).lock, &compare, LFDS711_MISC_FLAG_RAISED, LFDS711_MISC_CAS_STRENGTH_WEAK, result );               \
                                                                                                                                    \
    if( result == 1 )                                                                                                               \
    {                                                                                                                               \
      /* TRD : if E[1] is less than 1/100th of E[0], decrease the metric, to increase E[1] */                                       \
      if( (bs).backoff_iteration_frequency_counters[1] < (bs).backoff_iteration_frequency_counters[0] / 100 )                       \
      {                                                                                                                             \
        if( (bs).metric >= 11 )                                                                                                     \
          (bs).metric -= 10;                                                                                                        \
      }                                                                                                                             \
      else                                                                                                                          \
        (bs).metric += 10;                                                                                                          \
                                                                                                                                    \
      (bs).backoff_iteration_frequency_counters[0] = 0;                                                                             \
      (bs).backoff_iteration_frequency_counters[1] = 0;                                                                             \
      (bs).total_operations = 0;                                                                                                    \
                                                                                                                                    \
      LFDS711_MISC_BARRIER_STORE;                                                                                                   \
                                                                                                                                    \
      LFDS711_PAL_ATOMIC_SET( &(bs).lock, LFDS711_MISC_FLAG_LOWERED );                                                              \
    }                                                                                                                               \
  }                                                                                                                                 \
}

/***** library-wide prototypes *****/
void lfds711_misc_internal_backoff_init( struct lfds711_misc_backoff_state *bs );

