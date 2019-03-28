/***** includes *****/
#include "lfds711_freelist_internal.h"





/****************************************************************************/
void lfds711_freelist_cleanup( struct lfds711_freelist_state *fs,
                               void (*element_cleanup_callback)(struct lfds711_freelist_state *fs, struct lfds711_freelist_element *fe) )
{
  struct lfds711_freelist_element
    *fe,
    *fe_temp;

  LFDS711_PAL_ASSERT( fs != NULL );
  // TRD : element_cleanup_callback can be NULL

  LFDS711_MISC_BARRIER_LOAD;

  if( element_cleanup_callback != NULL )
  {
    fe = fs->top[POINTER];

    while( fe != NULL )
    {
      fe_temp = fe;
      fe = fe->next;

      element_cleanup_callback( fs, fe_temp );
    }
  }

  return;
}

