/***** defines *****/
#define LFDS711_QUEUE_UMM_GET_KEY_FROM_ELEMENT( queue_umm_element )             ( (queue_umm_element).key )
#define LFDS711_QUEUE_UMM_SET_KEY_IN_ELEMENT( queue_umm_element, new_key )      ( (queue_umm_element).key = (void *) (lfds711_pal_uint_t) (new_key) )
#define LFDS711_QUEUE_UMM_GET_VALUE_FROM_ELEMENT( queue_umm_element )           ( (queue_umm_element).value )
#define LFDS711_QUEUE_UMM_SET_VALUE_IN_ELEMENT( queue_umm_element, new_value )  ( (queue_umm_element).value = (void *) (lfds711_pal_uint_t) (new_value) )
#define LFDS711_QUEUE_UMM_GET_USER_STATE_FROM_STATE( queue_umm_state )          ( (queue_umm_state).user_state )

/***** enums *****/
enum lfds711_queue_umm_query
{
  LFDS711_QUEUE_UMM_QUERY_SINGLETHREADED_GET_COUNT,
  LFDS711_QUEUE_UMM_QUERY_SINGLETHREADED_VALIDATE
};

/***** structures *****/
struct lfds711_queue_umm_element
{
  struct lfds711_queue_umm_element LFDS711_PAL_ALIGN(LFDS711_PAL_ALIGN_DOUBLE_POINTER)
    *volatile next[PAC_SIZE];

  void
    *key,
    *value;
};

struct lfds711_queue_umm_state
{
  struct lfds711_queue_umm_element LFDS711_PAL_ALIGN(LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES)
    *volatile enqueue[PAC_SIZE],
    *volatile dequeue[PAC_SIZE];

  lfds711_pal_uint_t volatile LFDS711_PAL_ALIGN(LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES)
    aba_counter;

  void LFDS711_PAL_ALIGN(LFDS711_PAL_ATOMIC_ISOLATION_IN_BYTES)
    *user_state;

  struct lfds711_misc_backoff_state
    dequeue_backoff,
    enqueue_backoff;
};

/***** public prototypes *****/
void lfds711_queue_umm_init_valid_on_current_logical_core( struct lfds711_queue_umm_state *qumms,
                                                           struct lfds711_queue_umm_element *qumme_dummy,
                                                           void *user_state );
  // TRD : used in conjunction with the #define LFDS711_MISC_MAKE_VALID_ON_CURRENT_LOGICAL_CORE_INITS_COMPLETED_BEFORE_NOW_ON_ANY_OTHER_LOGICAL_CORE

void lfds711_queue_umm_cleanup( struct lfds711_queue_umm_state *qumms,
                                void (*element_cleanup_callback)(struct lfds711_queue_umm_state *qumms, struct lfds711_queue_umm_element *qumme, enum lfds711_misc_flag dummy_element_flag) );

void lfds711_queue_umm_enqueue( struct lfds711_queue_umm_state *qumms,
                                struct lfds711_queue_umm_element *qumme );

int lfds711_queue_umm_dequeue( struct lfds711_queue_umm_state *qumms,
                               struct lfds711_queue_umm_element **qumme );

void lfds711_queue_umm_query( struct lfds711_queue_umm_state *qumms,
                              enum lfds711_queue_umm_query query_type,
                              void *query_input,
                              void *query_output );

