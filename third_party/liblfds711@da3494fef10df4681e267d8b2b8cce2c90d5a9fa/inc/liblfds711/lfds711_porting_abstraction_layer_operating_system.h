/****************************************************************************/
#if( defined _WIN32 && !defined KERNEL_MODE )

  #ifdef LFDS711_PAL_OPERATING_SYSTEM
    #error More than one porting abstraction layer matches the current platform in "lfds711_porting_abstraction_layer_operating_system.h".
  #endif

  #define LFDS711_PAL_OPERATING_SYSTEM

  #include <assert.h>

  #define LFDS711_PAL_OS_STRING             "Windows"
  #define LFDS711_PAL_ASSERT( expression )  if( !(expression) ) LFDS711_MISC_DELIBERATELY_CRASH;

#endif





/****************************************************************************/
#if( defined _WIN32 && defined KERNEL_MODE )

  #ifdef LFDS711_PAL_OPERATING_SYSTEM
    #error More than one porting abstraction layer matches the current platform in "lfds711_porting_abstraction_layer_operating_system.h".
  #endif

  #define LFDS711_PAL_OPERATING_SYSTEM

  #include <assert.h>
  #include <wdm.h>

  #define LFDS711_PAL_OS_STRING             "Windows"
  #define LFDS711_PAL_ASSERT( expression )  if( !(expression) ) LFDS711_MISC_DELIBERATELY_CRASH;

#endif





/****************************************************************************/
#if( defined __linux__ && !defined KERNEL_MODE )

  #ifdef LFDS711_PAL_OPERATING_SYSTEM
    #error More than one porting abstraction layer matches the current platform in "lfds711_porting_abstraction_layer_operating_system.h".
  #endif

  #define LFDS711_PAL_OPERATING_SYSTEM

  #define LFDS711_PAL_OS_STRING             "Linux"
  #define LFDS711_PAL_ASSERT( expression )  if( !(expression) ) LFDS711_MISC_DELIBERATELY_CRASH;

#endif





/****************************************************************************/
#if( defined __linux__ && defined KERNEL_MODE )

  #ifdef LFDS711_PAL_OPERATING_SYSTEM
    #error More than one porting abstraction layer matches the current platform in "lfds711_porting_abstraction_layer_operating_system.h".
  #endif

  #define LFDS711_PAL_OPERATING_SYSTEM

  #include <linux/module.h>

  #define LFDS711_PAL_OS_STRING             "Linux"
  #define LFDS711_PAL_ASSERT( expression )  BUG_ON( expression )

#endif





/****************************************************************************/
#if( MAC_OS_SANDBOX )

  #define LFDS711_PAL_OPERATING_SYSTEM

  #include <IOKit/IOLib.h>

  #define LFDS711_PAL_OS_STRING            "macOS"
  #define LFDS711_PAL_ASSERT( expression ) assert( expression )

#endif





/****************************************************************************/
#if( !defined LFDS711_PAL_OPERATING_SYSTEM )

  #error No matching porting abstraction layer in lfds711_porting_abstraction_layer_operating_system.h

#endif

