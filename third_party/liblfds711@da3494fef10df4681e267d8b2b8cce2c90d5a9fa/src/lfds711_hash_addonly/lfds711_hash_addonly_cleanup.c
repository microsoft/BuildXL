/***** includes *****/
#include "lfds711_hash_addonly_internal.h"

/***** private prototypes*****/
static void btree_au_element_cleanup_function( struct lfds711_btree_au_state *baus,
                                               struct lfds711_btree_au_element *baue );





/****************************************************************************/
void lfds711_hash_a_cleanup( struct lfds711_hash_a_state *has,
                             void (*element_cleanup_callback)(struct lfds711_hash_a_state *has, struct lfds711_hash_a_element *hae) )
{
  lfds711_pal_uint_t
    loop;

  LFDS711_PAL_ASSERT( has != NULL );
  // TRD : element_cleanup_callback can be NULL

  if( element_cleanup_callback == NULL )
    return;

  LFDS711_MISC_BARRIER_LOAD;

  has->element_cleanup_callback = element_cleanup_callback;

  for( loop = 0 ; loop < has->array_size ; loop++ )
    lfds711_btree_au_cleanup( has->baus_array+loop, btree_au_element_cleanup_function );

  return;
}





/****************************************************************************/
#pragma warning( disable : 4100 )

static void btree_au_element_cleanup_function( struct lfds711_btree_au_state *baus,
                                               struct lfds711_btree_au_element *baue )
{
  struct lfds711_hash_a_state
    *has;

  struct lfds711_hash_a_element
    *hae;

  LFDS711_PAL_ASSERT( baus != NULL );
  LFDS711_PAL_ASSERT( baue != NULL );

  hae = (struct lfds711_hash_a_element *) LFDS711_BTREE_AU_GET_VALUE_FROM_ELEMENT( *baue );
  has = (struct lfds711_hash_a_state *) LFDS711_BTREE_AU_GET_USER_STATE_FROM_STATE( *baus );

  has->element_cleanup_callback( has, hae );

  return;
}

#pragma warning( default : 4100 )

