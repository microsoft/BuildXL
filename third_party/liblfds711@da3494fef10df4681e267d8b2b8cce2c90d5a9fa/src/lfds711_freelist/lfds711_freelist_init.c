/***** includes *****/
#include "lfds711_freelist_internal.h"





/****************************************************************************/
void lfds711_freelist_init_valid_on_current_logical_core( struct lfds711_freelist_state *fs,
                                                          struct lfds711_freelist_element * volatile (*elimination_array)[LFDS711_FREELIST_ELIMINATION_ARRAY_ELEMENT_SIZE_IN_FREELIST_ELEMENTS],
                                                          lfds711_pal_uint_t elimination_array_size_in_elements,
                                                          void *user_state )
{
  lfds711_pal_uint_t
    loop,
    subloop;

  LFDS711_PAL_ASSERT( fs != NULL );
  LFDS711_PAL_ASSERT( (lfds711_pal_uint_t) fs->top % LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES == 0 );
  LFDS711_PAL_ASSERT( (lfds711_pal_uint_t) &fs->elimination_array_size_in_elements % LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES == 0 );
  // TRD : elimination_array can be NULL
  LFDS711_PAL_ASSERT( (elimination_array == NULL) or 
                      ( (elimination_array != NULL) and (lfds711_pal_uint_t) elimination_array % LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES == 0 ) );
  LFDS711_PAL_ASSERT( (elimination_array == NULL and elimination_array_size_in_elements == 0) or 
                      (elimination_array != NULL and elimination_array_size_in_elements >= 2 and (elimination_array_size_in_elements & (elimination_array_size_in_elements-1)) == 0) );
  // TRD : user_state can be NULL

  fs->top[POINTER] = NULL;
  fs->top[COUNTER] = 0;

  fs->elimination_array = elimination_array;
  fs->elimination_array_size_in_elements = elimination_array_size_in_elements;
  fs->user_state = user_state;

  for( loop = 0 ; loop < elimination_array_size_in_elements ; loop++ )
    for( subloop = 0 ; subloop < LFDS711_FREELIST_ELIMINATION_ARRAY_ELEMENT_SIZE_IN_FREELIST_ELEMENTS ; subloop++ )
      fs->elimination_array[loop][subloop] = NULL;

  lfds711_misc_internal_backoff_init( &fs->pop_backoff );
  lfds711_misc_internal_backoff_init( &fs->push_backoff );

  LFDS711_MISC_BARRIER_STORE;

  lfds711_misc_force_store();

  return;
}

