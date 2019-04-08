/***** includes *****/
#include "lfds711_hash_addonly_internal.h"





/****************************************************************************/
void lfds711_hash_a_init_valid_on_current_logical_core( struct lfds711_hash_a_state *has,
                                                        struct lfds711_btree_au_state *baus_array,
                                                        lfds711_pal_uint_t array_size,
                                                        int (*key_compare_function)(void const *new_key, void const *existing_key),
                                                        void (*key_hash_function)(void const *key, lfds711_pal_uint_t *hash),
                                                        enum lfds711_hash_a_existing_key existing_key,
                                                        void *user_state )
{
  enum lfds711_btree_au_existing_key
    btree_au_existing_key = LFDS711_BTREE_AU_EXISTING_KEY_OVERWRITE; // TRD : for compiler warning

  lfds711_pal_uint_t
    loop;

  LFDS711_PAL_ASSERT( has != NULL );
  LFDS711_PAL_ASSERT( baus_array != NULL );
  LFDS711_PAL_ASSERT( array_size > 0 );
  LFDS711_PAL_ASSERT( key_compare_function != NULL );
  LFDS711_PAL_ASSERT( key_hash_function != NULL );
  // TRD : existing_key can be any value in its range
  // TRD : user_state can be NULL

  has->array_size = array_size;
  has->key_compare_function = key_compare_function;
  has->key_hash_function = key_hash_function;
  has->existing_key = existing_key;
  has->baus_array = baus_array;
  has->user_state = user_state;

  if( has->existing_key == LFDS711_HASH_A_EXISTING_KEY_OVERWRITE )
    btree_au_existing_key = LFDS711_BTREE_AU_EXISTING_KEY_OVERWRITE;

  if( has->existing_key == LFDS711_HASH_A_EXISTING_KEY_FAIL )
    btree_au_existing_key = LFDS711_BTREE_AU_EXISTING_KEY_FAIL;

  // TRD : since the addonly_hash atomic counts, if that flag is set, the btree_addonly_unbalanceds don't have to
  for( loop = 0 ; loop < array_size ; loop++ )
    lfds711_btree_au_init_valid_on_current_logical_core( has->baus_array+loop, key_compare_function, btree_au_existing_key, user_state );

  LFDS711_MISC_BARRIER_STORE;

  lfds711_misc_force_store();

  return;
}

