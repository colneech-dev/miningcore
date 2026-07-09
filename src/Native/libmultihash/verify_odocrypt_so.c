// Consensus gate for the BUILT libmultihash shared library.
//
// Unlike build_test_odocrypt.sh (which compiles odocrypt.cpp in isolation and so
// cannot catch a bad multi-object link), this dlopen's the actual produced .so and
// exercises its exported odocrypt_export symbol against a fixed DigiByte-consensus
// vector. Wire it into build-libs-linux.sh so a miscompiled/mislinked OdoCrypt can
// never ship. Returns non-zero on mismatch.
//
// Build: cc verify_odocrypt_so.c -ldl -o verify_odocrypt_so
// Run:   ./verify_odocrypt_so /path/to/libmultihash.so
#include <dlfcn.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

// Known-good vector: 80-byte header 0x00..0x4f, OdoKey 0x683b9800.
// Expected digest produced by the DigiByte odo-miner reference (see test_odocrypt.cpp).
static const char *EXPECTED =
    "25b32ac37d536e657e514b0c99fcc69ae73a425b3e26b75fd8b912dbf527c1ad";

typedef void (*odocrypt_fn)(const char *, char *, uint32_t, uint32_t);

int main(int argc, char **argv)
{
    if(argc < 2) { fprintf(stderr, "usage: %s <libmultihash.so>\n", argv[0]); return 2; }

    // RTLD_LAZY so lazily-bound C++ helpers (unused by odocrypt) don't block the load;
    // RTLD_GLOBAL so libcrypto/libsodium symbols the .so expects resolve from the process.
    void *h = dlopen(argv[1], RTLD_LAZY | RTLD_GLOBAL);
    if(!h) { fprintf(stderr, "dlopen failed: %s\n", dlerror()); return 2; }

    odocrypt_fn odo = (odocrypt_fn) dlsym(h, "odocrypt_export");
    if(!odo) { fprintf(stderr, "odocrypt_export not found: %s\n", dlerror()); return 2; }

    // Same fixed 80-byte header as test_odocrypt.cpp (input[i] = i*7+3).
    char header[80];
    for(int i = 0; i < 80; i++) header[i] = (char) (i * 7 + 3);

    char out[32];
    odo(header, out, 80, 0x683b9800u);

    char got[65];
    for(int i = 0; i < 32; i++) sprintf(got + 2 * i, "%02x", (unsigned char) out[i]);

    if(strcmp(got, EXPECTED) == 0) {
        printf("PASS — built libmultihash OdoCrypt matches DigiByte consensus\n");
        return 0;
    }
    fprintf(stderr, "FAIL — OdoCrypt consensus mismatch\n  expected %s\n  got      %s\n", EXPECTED, got);
    return 1;
}
