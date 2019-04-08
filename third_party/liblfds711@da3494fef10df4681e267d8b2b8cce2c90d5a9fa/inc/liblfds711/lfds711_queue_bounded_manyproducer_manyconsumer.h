/***** defines *****/
#define LFDS711_QUEUE_BMM_GET_USER_STATE_FROM_STATE( queue_bmm_state )  ( (queue_bmm_state).user_state )

/***** enums *****/
enum lfds711_queue_bmm_query
{
  LFDS711_QUEUE_BMM_QUERY_GET_POTENTIALLY_INACCURATE_COUNT,
  LFDS711_QUEUE_BMM_QUERY_SINGLETHREADED_VALIDATE
};

/***** structures *****/
struct lfds711_queue_bmm_element
{
  lfds711_pal_uint_t volatile
    sequence_number;

  void
    *volatile key,
    *volatile value;
};

struct lfds711_queue_bmm_state
{
  lfds711_pal_uint_t
    number_elements,
    mask;

  lfds711_pal_uint_t volatile LFDS711_PAL_ALIGN(LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES)
    read_index,
    write_index;

  struct lfds711_queue_bmm_element
    *element_array;

  void
    *user_state;

  struct lfds711_misc_backoff_state
    dequeue_backoff,
    enqueue_backoff;
};

/***** public prototypes *****/
void lfds711_queue_bmm_init_valid_on_current_logical_core( struct lfds711_queue_bmm_state *qbmms,
                                                           struct lfds711_queue_bmm_element *element_array,
                                                           lfds711_pal_uint_t number_elements,
                                                           void *user_state );

void lfds711_queue_bmm_cleanup( struct lfds711_queue_bmm_state *qbmms,
                                void (*element_cleanup_callback)(struct lfds711_queue_bmm_state *qbmms,
                                                                 void *key,
                                                                 void *value) );

int lfds711_queue_bmm_enqueue( struct lfds711_queue_bmm_state *qbmms,
                               void *key,
                               void *value );

int lfds711_queue_bmm_dequeue( struct lfds711_queue_bmm_state *qbmms,
                                      void **key,
                                      void **value );

void lfds711_queue_bmm_query( struct lfds711_queue_bmm_state *qbmms,
                              enum lfds711_queue_bmm_query query_type,
                              void *query_input,
                              void *query_output );

