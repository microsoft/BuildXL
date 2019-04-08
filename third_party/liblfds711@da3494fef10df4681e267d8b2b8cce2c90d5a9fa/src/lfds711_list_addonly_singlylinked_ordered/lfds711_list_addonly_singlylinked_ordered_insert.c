/***** includes *****/
#include "lfds711_list_addonly_singlylinked_ordered_internal.h"





/****************************************************************************/
enum lfds711_list_aso_insert_result lfds711_list_aso_insert( struct lfds711_list_aso_state *lasos,
                                                             struct lfds711_list_aso_element *lasoe,
                                                             struct lfds711_list_aso_element **existing_lasoe )
{
  char unsigned 
    result;

  enum lfds711_misc_flag
    finished_flag = LFDS711_MISC_FLAG_LOWERED;

  int
    compare_result = 0;

  lfds711_pal_uint_t
    backoff_iteration = LFDS711_BACKOFF_INITIAL_VALUE;

  struct lfds711_list_aso_element
    *volatile lasoe_temp = NULL,
    *volatile lasoe_trailing;

  LFDS711_PAL_ASSERT( lasos != NULL );
  LFDS711_PAL_ASSERT( lasoe != NULL );
  LFDS711_PAL_ASSERT( (lfds711_pal_uint_t) &lasoe->next % LFDS711_PAL_ALIGN_SINGLE_POINTER == 0 );
  LFDS711_PAL_ASSERT( (lfds711_pal_uint_t) &lasoe->value % LFDS711_PAL_ALIGN_SINGLE_POINTER == 0 );
  // TRD : existing_lasoe can be NULL

  /* TRD : imagine a list, sorted small to large

           we arrive at an element
           we obtain its next pointer
           we check we are greater than the current element and smaller than the next element
           this means we have found the correct location to insert
           we try to CAS ourselves in; in the meantime,
           someone else has *aready* swapped in an element which is smaller than we are

           e.g.

           the list is { 1, 10 } and we are the value 5

           we arrive at 1; we check the next element and see it is 10
           so we are larger than the current element and smaller than the next
           we are in the correct location to insert and we go to insert...

           in the meantime, someone else with the value 3 comes along
           he too finds this is the correct location and inserts before we do
           the list is now { 1, 3, 10 } and we are trying to insert now after
           1 and before 3!

           our insert CAS fails, because the next pointer of 1 has changed aready;
           but we see we are in the wrong location - we need to move forward an
           element
  */

  LFDS711_MISC_BARRIER_LOAD;

  /* TRD : we need to begin with the leading dummy element
           as the element to be inserted
           may be smaller than all elements in the list
  */

  lasoe_trailing = lasos->start;
  lasoe_temp = lasos->start->next;

  while( finished_flag == LFDS711_MISC_FLAG_LOWERED )
  {
    if( lasoe_temp == NULL )
      compare_result = -1;

    if( lasoe_temp != NULL )
    {
      LFDS711_MISC_BARRIER_LOAD;
      compare_result = lasos->key_compare_function( lasoe->key, lasoe_temp->key );
    }

    if( compare_result == 0 )
    {
      if( existing_lasoe != NULL )
        *existing_lasoe = lasoe_temp;

      switch( lasos->existing_key )
      {
        case LFDS711_LIST_ASO_EXISTING_KEY_OVERWRITE:
          LFDS711_LIST_ASO_SET_VALUE_IN_ELEMENT( *lasoe_temp, lasoe->value );
          return LFDS711_LIST_ASO_INSERT_RESULT_SUCCESS_OVERWRITE;
        break;

        case LFDS711_LIST_ASO_EXISTING_KEY_FAIL:
          return LFDS711_LIST_ASO_INSERT_RESULT_FAILURE_EXISTING_KEY;
        break;
      }

      finished_flag = LFDS711_MISC_FLAG_RAISED;
    }

    if( compare_result < 0 )
    {
      lasoe->next = lasoe_temp;
      LFDS711_MISC_BARRIER_STORE;
      LFDS711_PAL_ATOMIC_CAS( &lasoe_trailing->next, (struct lfds711_list_aso_element **) &lasoe->next, lasoe, LFDS711_MISC_CAS_STRENGTH_WEAK, result );

      if( result == 1 )
        finished_flag = LFDS711_MISC_FLAG_RAISED;
      else
      {
        LFDS711_BACKOFF_EXPONENTIAL_BACKOFF( lasos->insert_backoff, backoff_iteration );
        // TRD : if we fail to link, someone else has linked and so we need to redetermine our position is correct
        lasoe_temp = lasoe_trailing->next;
      }
    }

    if( compare_result > 0 )
    {
      // TRD : move trailing along by one element
      lasoe_trailing = lasoe_trailing->next;

      /* TRD : set temp as the element after trailing
               if the new element we're linking is larger than all elements in the list,
               lasoe_temp will now go to NULL and we'll link at the end
      */
      lasoe_temp = lasoe_trailing->next;
    }
  }

  LFDS711_BACKOFF_AUTOTUNE( lasos->insert_backoff, backoff_iteration );

  return LFDS711_LIST_ASO_INSERT_RESULT_SUCCESS;
}

