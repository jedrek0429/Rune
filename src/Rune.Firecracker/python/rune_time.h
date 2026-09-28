#pragma once

#include "py/obj.h"
#include "shared/timeutils/timeutils.h"

void mp_time_localtime_get(timeutils_struct_time_t *tm);
mp_obj_t mp_time_time_get(void);
