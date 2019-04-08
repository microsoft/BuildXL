/***** includes *****/
#include "lfds711_list_addonly_singlylinked_ordered_internal.h"





/****************************************************************************/
void lfds711_list_aso_init_valid_on_current_logical_core( struct lfds711_list_aso_state *lasos,
                                                          int (*key_compare_function)(void const *new_key, void const *existing_key),
                                                          enum lfds711_list_aso_existing_key existing_key,
                                                          void *user_state )
{
  LFDS711_PAL_ASSERT( lasos != NULL );
  LFDS711_PAL_ASSERT( (lfds711_pal_uint_t) &lasos->dummy_element % LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES == 0 );
  LFDS711_PAL_ASSERT( (lfds711_pal_uint_t) &lasos->start % LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES == 0 );
  LFDS711_PAL_ASSERT( key_compare_function != NULL );
  // TRD : existing_key can be any value in its range
  // TRD : user_state can be NULL

  // TRD : dummy start element - makes code easier when you can always use ->next
  lasos->start = &lasos->dummy_element;

  lasos->start->next = NULL;
  lasos->start->value = NULL;
  lasos->key_compare_function = key_compare_function;
  lasos->existing_key = existing_key;
  lasos->user_state = user_state;

  lfds711_misc_internal_backoff_init( &lasos->insert_backoff );

  LFDS711_MISC_BARRIER_STORE;

  lfds711_misc_force_store();

  return;
}

