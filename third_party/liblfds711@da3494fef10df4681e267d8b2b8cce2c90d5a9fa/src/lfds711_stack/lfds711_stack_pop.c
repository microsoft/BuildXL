/***** includes *****/
#include "lfds711_stack_internal.h"





/****************************************************************************/
int lfds711_stack_pop( struct lfds711_stack_state *ss,
                       struct lfds711_stack_element **se )
{
  char unsigned
    result;

  lfds711_pal_uint_t
    backoff_iteration = LFDS711_BACKOFF_INITIAL_VALUE;

  struct lfds711_stack_element LFDS711_PAL_ALIGN(LFDS711_PAL_ALIGN_DOUBLE_POINTER)
    *new_top[PAC_SIZE],
    *volatile original_top[PAC_SIZE];

  LFDS711_PAL_ASSERT( ss != NULL );
  LFDS711_PAL_ASSERT( se != NULL );

  LFDS711_MISC_BARRIER_LOAD;

  original_top[COUNTER] = ss->top[COUNTER];
  original_top[POINTER] = ss->top[POINTER];

  do
  {
    if( original_top[POINTER] == NULL )
    {
      *se = NULL;
      return 0;
    }

    new_top[COUNTER] = original_top[COUNTER] + 1;
    new_top[POINTER] = original_top[POINTER]->next;

    LFDS711_PAL_ATOMIC_DWCAS( ss->top, original_top, new_top, LFDS711_MISC_CAS_STRENGTH_WEAK, result );

    if( result == 0 )
    {
      LFDS711_BACKOFF_EXPONENTIAL_BACKOFF( ss->pop_backoff, backoff_iteration );
      LFDS711_MISC_BARRIER_LOAD;
    }
  }
  while( result == 0 );

  *se = original_top[POINTER];

  LFDS711_BACKOFF_AUTOTUNE( ss->pop_backoff, backoff_iteration );

  return 1;
}

