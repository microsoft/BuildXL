/***** includes *****/
#include "lfds711_ringbuffer_internal.h"





/****************************************************************************/
void lfds711_ringbuffer_init_valid_on_current_logical_core( struct lfds711_ringbuffer_state *rs,
                                                            struct lfds711_ringbuffer_element *re_array_inc_dummy,
                                                            lfds711_pal_uint_t number_elements_inc_dummy,
                                                            void *user_state )
{
  lfds711_pal_uint_t
    loop;

  LFDS711_PAL_ASSERT( rs != NULL );
  LFDS711_PAL_ASSERT( re_array_inc_dummy != NULL );
  LFDS711_PAL_ASSERT( number_elements_inc_dummy >= 2 );
  // TRD : user_state can be NULL

  rs->user_state = user_state;

  re_array_inc_dummy[0].qumme_use = &re_array_inc_dummy[0].qumme;

  lfds711_freelist_init_valid_on_current_logical_core( &rs->fs, NULL, 0, rs );
  lfds711_queue_umm_init_valid_on_current_logical_core( &rs->qumms, &re_array_inc_dummy[0].qumme, rs );

  for( loop = 1 ; loop < number_elements_inc_dummy ; loop++ )
  {
    re_array_inc_dummy[loop].qumme_use = &re_array_inc_dummy[loop].qumme;
    LFDS711_FREELIST_SET_VALUE_IN_ELEMENT( re_array_inc_dummy[loop].fe, &re_array_inc_dummy[loop] );
    lfds711_freelist_push( &rs->fs, &re_array_inc_dummy[loop].fe, NULL );
  }

  LFDS711_MISC_BARRIER_STORE;

  lfds711_misc_force_store();

  return;
}

