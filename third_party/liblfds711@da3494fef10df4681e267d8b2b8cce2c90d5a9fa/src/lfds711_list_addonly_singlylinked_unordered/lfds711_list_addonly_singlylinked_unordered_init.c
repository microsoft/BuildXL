/***** includes *****/
#include "lfds711_list_addonly_singlylinked_unordered_internal.h"





/****************************************************************************/
void lfds711_list_asu_init_valid_on_current_logical_core( struct lfds711_list_asu_state *lasus,
                                                          void *user_state )
{
  LFDS711_PAL_ASSERT( lasus != NULL );
  LFDS711_PAL_ASSERT( (lfds711_pal_uint_t) &lasus->dummy_element % LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES == 0 );
  LFDS711_PAL_ASSERT( (lfds711_pal_uint_t) &lasus->end % LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES == 0 );
  LFDS711_PAL_ASSERT( (lfds711_pal_uint_t) &lasus->start % LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES == 0 );
  // TRD : user_state can be NULL

  // TRD : dummy start element - makes code easier when you can always use ->next
  lasus->start = lasus->end = &lasus->dummy_element;

  lasus->start->next = NULL;
  lasus->start->value = NULL;
  lasus->user_state = user_state;

  lfds711_misc_internal_backoff_init( &lasus->after_backoff );
  lfds711_misc_internal_backoff_init( &lasus->start_backoff );
  lfds711_misc_internal_backoff_init( &lasus->end_backoff );

  LFDS711_MISC_BARRIER_STORE;

  lfds711_misc_force_store();

  return;
}

