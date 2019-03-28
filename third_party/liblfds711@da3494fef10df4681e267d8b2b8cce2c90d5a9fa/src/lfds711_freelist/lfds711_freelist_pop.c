/***** includes *****/
#include "lfds711_freelist_internal.h"





/****************************************************************************/
int lfds711_freelist_pop( struct lfds711_freelist_state *fs,
                          struct lfds711_freelist_element **fe,
                          struct lfds711_prng_st_state *psts )
{
  char unsigned
    result;

  lfds711_pal_uint_t
    backoff_iteration = LFDS711_BACKOFF_INITIAL_VALUE,
    elimination_array_index,
    loop,
    random_value;

  struct lfds711_freelist_element LFDS711_PAL_ALIGN(LFDS711_PAL_ALIGN_DOUBLE_POINTER)
    *new_top[PAC_SIZE],
    *volatile original_top[PAC_SIZE];

  LFDS711_PAL_ASSERT( fs != NULL );
  LFDS711_PAL_ASSERT( fe != NULL );
  // TRD : psts can be NULL

  LFDS711_MISC_BARRIER_LOAD;

  if( fs->elimination_array_size_in_elements > 0 )
  {
    if( psts != NULL )
    {
      LFDS711_PRNG_ST_GENERATE( *psts, random_value );
      elimination_array_index = ( random_value & (fs->elimination_array_size_in_elements-1) );
    }
    else
    {
      elimination_array_index = (lfds711_pal_uint_t) fe;
      LFDS711_PRNG_ST_MIXING_FUNCTION( elimination_array_index );
      elimination_array_index = ( elimination_array_index & (fs->elimination_array_size_in_elements-1) );
    }

    // TRD : full scan of one cache line, max pointers per cache line

    *fe = NULL;

    for( loop = 0 ; loop < LFDS711_FREELIST_ELIMINATION_ARRAY_ELEMENT_SIZE_IN_FREELIST_ELEMENTS ; loop++ )
      if( fs->elimination_array[elimination_array_index][loop] != NULL )
      {
        LFDS711_PAL_ATOMIC_EXCHANGE( &fs->elimination_array[elimination_array_index][loop], *fe, struct lfds711_freelist_element * );
        if( *fe != NULL )
          return 1;
      }
  }

  original_top[COUNTER] = fs->top[COUNTER];
  original_top[POINTER] = fs->top[POINTER];

  do
  {
    if( original_top[POINTER] == NULL )
    {
      *fe = NULL;
      return 0;
    }

    new_top[COUNTER] = original_top[COUNTER] + 1;
    new_top[POINTER] = original_top[POINTER]->next;

    LFDS711_PAL_ATOMIC_DWCAS( fs->top, original_top, new_top, LFDS711_MISC_CAS_STRENGTH_WEAK, result );

    if( result == 0 )
    {
      LFDS711_BACKOFF_EXPONENTIAL_BACKOFF( fs->pop_backoff, backoff_iteration );
      LFDS711_MISC_BARRIER_LOAD;
    }
  }
  while( result == 0 );

  *fe = original_top[POINTER];

  LFDS711_BACKOFF_AUTOTUNE( fs->pop_backoff, backoff_iteration );

  return 1;
}

