#!/usr/bin/env bash
set -euo pipefail

[[ $# -eq 2 ]] || { echo "usage: $0 <source.rb> <artifact>" >&2; exit 2; }
source="$1"
artifact="$2"
work="$(dirname "$artifact")"
irep="$work/rune_irep.c"
launcher="$work/rune-ruby.c"

mrbc -B rune_irep -o "$irep" "$source"
cat >"$launcher" <<'EOF'
#include <mruby.h>
#include <mruby/error.h>
#include <mruby/irep.h>
#include "rune_irep.c"

int main(void) {
    mrb_state *mrb = mrb_open();
    if (mrb == NULL) return 1;
    mrb_load_irep(mrb, rune_irep);
    if (mrb->exc != NULL) {
        mrb_print_error(mrb);
        mrb_close(mrb);
        return 1;
    }
    mrb_close(mrb);
    return 0;
}
EOF

cc -O2 -s -I"$work" "$launcher" /opt/rune/mruby/libmruby.a -lm -o "$artifact"
