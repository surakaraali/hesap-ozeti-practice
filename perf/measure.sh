#!/bin/sh
# Median of 5 warm calls for each URL path given. The API must be running on :5196.
# Usage: perf/measure.sh "/accounts/1/transactions?page=1" ...
for path in "$@"; do
  curl -s -o /dev/null "http://localhost:5196$path"   # warm-up
  t=$(for i in 1 2 3 4 5; do curl -s -o /dev/null -w "%{time_total}\n" "http://localhost:5196$path"; done | sort -n | sed -n 3p)
  printf "%6.0f ms  %s\n" "$(echo "$t * 1000" | bc)" "$path"
done
