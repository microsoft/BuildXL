/***** includes *****/
#include "lfds711_prng_internal.h"





/****************************************************************************/
void lfds711_prng_init_valid_on_current_logical_core( struct lfds711_prng_state *ps, lfds711_pal_uint_t seed )
{
  LFDS711_PAL_ASSERT( ps != NULL );
  LFDS711_PAL_ASSERT( (lfds711_pal_uint_t) &ps->entropy % LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES == 0 );
  // TRD : seed can be any value in its range (unlike for the mixing function)

  LFDS711_PRNG_ST_MIXING_FUNCTION( seed );

  ps->entropy = seed;

  LFDS711_MISC_BARRIER_STORE;

  lfds711_misc_force_store();

  return;
}





/****************************************************************************/
void lfds711_prng_st_init( struct lfds711_prng_st_state *psts, lfds711_pal_uint_t seed )
{
  LFDS711_PAL_ASSERT( psts != NULL );
  LFDS711_PAL_ASSERT( seed != 0 );

  LFDS711_PRNG_ST_MIXING_FUNCTION( seed );

  psts->entropy = seed;

  return;
}

