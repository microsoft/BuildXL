/***** includes *****/
#include "lfds711_list_addonly_singlylinked_unordered_internal.h"





/****************************************************************************/
int lfds711_list_asu_get_by_key( struct lfds711_list_asu_state *lasus,
                                 int (*key_compare_function)(void const *new_key, void const *existing_key),
                                 void *key, 
                                 struct lfds711_list_asu_element **lasue )
{
  int
    cr = !0,
    rv = 1;

  LFDS711_PAL_ASSERT( lasus != NULL );
  LFDS711_PAL_ASSERT( key_compare_function != NULL );
  // TRD : key can be NULL
  LFDS711_PAL_ASSERT( lasue != NULL );

  *lasue = NULL;

  while( cr != 0 and LFDS711_LIST_ASU_GET_START_AND_THEN_NEXT(*lasus, *lasue) )
    cr = key_compare_function( key, (*lasue)->key );

  if( *lasue == NULL )
    rv = 0;

  return rv;
}

