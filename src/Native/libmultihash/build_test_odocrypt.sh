#!/bin/bash
# Build and run the standalone OdoCrypt consensus test (no .NET needed).
set -e
cd "$(dirname "$0")"
g++ -O2 -std=c++17 -c odocrypt.cpp -o /tmp/odo_odocrypt.o
gcc -O2 -c KeccakP-800-reference.c -o /tmp/odo_keccak.o
g++ -O2 -std=c++17 test_odocrypt.cpp /tmp/odo_odocrypt.o /tmp/odo_keccak.o -o /tmp/test_odocrypt
/tmp/test_odocrypt
