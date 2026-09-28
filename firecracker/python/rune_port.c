#include <stdint.h>
#include <time.h>

#include "py/runtime.h"
#include "py/builtin.h"
#include "py/mphal.h"
#include "shared/timeutils/timeutils.h"

uint64_t mp_hal_time_ns(void) {
    struct timespec ts;
    clock_gettime(CLOCK_REALTIME, &ts);

    return (uint64_t)ts.tv_sec * 1000000000ULL
        + (uint64_t)ts.tv_nsec;
}

mp_uint_t mp_hal_ticks_ms(void) {
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);

    return (mp_uint_t)(
        (uint64_t)ts.tv_sec * 1000ULL
        + (uint64_t)ts.tv_nsec / 1000000ULL
    );
}

mp_uint_t mp_hal_ticks_us(void) {
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);

    return (mp_uint_t)(
        (uint64_t)ts.tv_sec * 1000000ULL
        + (uint64_t)ts.tv_nsec / 1000ULL
    );
}

mp_uint_t mp_hal_ticks_cpu(void) {
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);

    return (mp_uint_t)(
        (uint64_t)ts.tv_sec * 1000000000ULL
        + (uint64_t)ts.tv_nsec
    );
}

void mp_hal_delay_ms(mp_uint_t ms) {
    struct timespec req = {
        .tv_sec = ms / 1000,
        .tv_nsec = (long)(ms % 1000) * 1000000L,
    };

    while (nanosleep(&req, &req) == -1) {
    }
}

void mp_hal_delay_us(mp_uint_t us) {
    struct timespec req = {
        .tv_sec = us / 1000000,
        .tv_nsec = (long)(us % 1000000) * 1000L,
    };

    while (nanosleep(&req, &req) == -1) {
    }
}

mp_obj_t mp_time_time_get(void) {
    struct timespec ts;
    clock_gettime(CLOCK_REALTIME, &ts);

    mp_float_t value =
        (mp_float_t)ts.tv_sec
        + (mp_float_t)ts.tv_nsec / 1000000000.0;

    return mp_obj_new_float(value);
}

void mp_time_localtime_get(timeutils_struct_time_t *tm) {
    time_t now = time(NULL);
    struct tm result;

    gmtime_r(&now, &result);

    tm->tm_year = result.tm_year + 1900;
    tm->tm_mon = result.tm_mon + 1;
    tm->tm_mday = result.tm_mday;
    tm->tm_hour = result.tm_hour;
    tm->tm_min = result.tm_min;
    tm->tm_sec = result.tm_sec;
    tm->tm_wday = (result.tm_wday + 6) % 7;
    tm->tm_yday = result.tm_yday + 1;
}

mp_obj_t mp_builtin_open(
    size_t n_args,
    const mp_obj_t *args,
    mp_map_t *kwargs
) {
    (void)n_args;
    (void)args;
    (void)kwargs;

    mp_raise_msg(
        &mp_type_OSError,
        MP_ERROR_TEXT("filesystem access is disabled")
    );

    return MP_OBJ_NULL;
}

MP_DEFINE_CONST_FUN_OBJ_KW(
    mp_builtin_open_obj,
    1,
    mp_builtin_open
);

