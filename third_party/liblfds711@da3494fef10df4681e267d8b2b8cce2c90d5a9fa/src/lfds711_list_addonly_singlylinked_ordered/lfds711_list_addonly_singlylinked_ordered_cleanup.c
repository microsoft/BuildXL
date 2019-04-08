/***** includes *****/
#include "lfds711_list_addonly_singlylinked_ordered_internal.h"





/****************************************************************************/
void lfds711_list_aso_cleanup( struct lfds711_list_aso_state *lasos,
                               void (*element_cleanup_callback)(struct lfds711_list_aso_state *lasos, struct lfds711_list_aso_element *lasoe) )
{
  struct lfds711_list_aso_element
    *lasoe,
    *temp;

  LFDS711_PAL_ASSERT( lasos != NULL );
  // TRD : element_cleanup_callback can be NULL

  LFDS711_MISC_BARRIER_LOAD;

  if( element_cleanup_callback == NULL )
    return;

  lasoe = LFDS711_LIST_ASO_GET_START( *lasos );

  while( lasoe != NULL )
  {
    temp = lasoe;

    lasoe = LFDS711_LIST_ASO_GET_NEXT( *lasoe );

    element_cleanup_callback( lasos, temp );
  }

  return;
}

