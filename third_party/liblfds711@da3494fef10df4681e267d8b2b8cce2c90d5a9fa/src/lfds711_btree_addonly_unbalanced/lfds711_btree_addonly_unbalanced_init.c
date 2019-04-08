/***** includes *****/
#include "lfds711_btree_addonly_unbalanced_internal.h"





/****************************************************************************/
void lfds711_btree_au_init_valid_on_current_logical_core( struct lfds711_btree_au_state *baus,
                                                          int (*key_compare_function)(void const *new_key, void const *existing_key),
                                                          enum lfds711_btree_au_existing_key existing_key,
                                                          void *user_state )
{
  LFDS711_PAL_ASSERT( baus != NULL );
  LFDS711_PAL_ASSERT( (lfds711_pal_uint_t) &baus->root % LFDS711_PAL_ALIGN_SINGLE_POINTER == 0 );
  LFDS711_PAL_ASSERT( key_compare_function != NULL );
  // TRD : existing_key can be any value in its range
  // TRD : user_state can be NULL

  baus->root = NULL;
  baus->key_compare_function = key_compare_function;
  baus->existing_key = existing_key;
  baus->user_state = user_state;

  lfds711_misc_internal_backoff_init( &baus->insert_backoff );

  LFDS711_MISC_BARRIER_STORE;

  lfds711_misc_force_store();

  return;
}

