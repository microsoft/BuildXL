/***** includes *****/
#include "lfds711_queue_bounded_singleproducer_singleconsumer_internal.h"





/****************************************************************************/
void lfds711_queue_bss_init_valid_on_current_logical_core( struct lfds711_queue_bss_state *qbsss,
                                                           struct lfds711_queue_bss_element *element_array,
                                                           lfds711_pal_uint_t number_elements,
                                                           void *user_state )
{
  LFDS711_PAL_ASSERT( qbsss != NULL );
  LFDS711_PAL_ASSERT( element_array != NULL );
  LFDS711_PAL_ASSERT( number_elements >= 2 );
  LFDS711_PAL_ASSERT( ( number_elements & (number_elements-1) ) == 0 ); // TRD : number_elements must be a positive integer power of 2
  // TRD : user_state can be NULL

  /* TRD : the use of mask and the restriction on a power of two
           upon the number of elements bears some remark

           in this queue, there are a fixed number of elements
           we have a read index and a write index
           when we write, and thre is space to write, we increment the write index
           (if no space to write, we just return)
           when we read, and there are elements to be read, we after reading increment the read index
           (if no elements to read, we just return)
           the problem is - how do we handle wrap around?
           e.g. when I write, but my write index is now equal to the number of elements
           the usual solution is to modulus the write index by the nunmber of elements
           problem is modulus is slow
           there is a better way
           first, we restrict the number of elements to be a power of two
           so imagine we have a 64-bit system and we set the number of elements to be 2^64
           this gives us a bit pattern of 1000 0000 0000 0000 (...etc, lots of zeros)
           now (just roll with this for a bit) subtract one from this
           this gives us a mask (on a two's compliment machine)
           0111 1111 1111 1111 (...etc, lots of ones)
           so what we do now, when we increment an index (think of the write index as the example)
           we bitwise and it with the mask
           now think about thwt happens
           all the numbers up to 2^64 will be unchanged - their MSB is never set, and we and with all the other bits
           but when we finally hit 2^64 and need to roll over... bingo!
           we drop MSB (which we finally have) and have the value 0!
           this is exactly what we want
           bitwise and is much faster than modulus
  */

  qbsss->number_elements = number_elements;
  qbsss->mask = qbsss->number_elements - 1;
  qbsss->read_index = 0;
  qbsss->write_index = 0;
  qbsss->element_array = element_array;
  qbsss->user_state = user_state;

  LFDS711_MISC_BARRIER_STORE;

  lfds711_misc_force_store();

  return;
}

