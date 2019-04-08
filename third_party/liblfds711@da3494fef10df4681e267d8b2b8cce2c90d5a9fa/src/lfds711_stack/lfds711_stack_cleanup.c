/***** includes *****/
#include "lfds711_stack_internal.h"





/****************************************************************************/
void lfds711_stack_cleanup( struct lfds711_stack_state *ss,
                            void (*element_cleanup_callback)(struct lfds711_stack_state *ss, struct lfds711_stack_element *se) )
{
  struct lfds711_stack_element
    *se,
    *se_temp;

  LFDS711_PAL_ASSERT( ss != NULL );
  // TRD : element_cleanup_callback can be NULL

  LFDS711_MISC_BARRIER_LOAD;

  if( element_cleanup_callback != NULL )
  {
    se = ss->top[POINTER];

    while( se != NULL )
    {
      se_temp = se;
      se = se->next;

      element_cleanup_callback( ss, se_temp );
    }
  }

  return;
}

