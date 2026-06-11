// test_odocrypt.cpp — standalone build-and-test harness for Miningcore's
// OdoCrypt native path. Proves libmultihash's odocrypt_export produces the
// DigiByte CONSENSUS hash (bit-exact with DigiByte Core 8.26.2 and with the
// odo-miner FPGA).
//
// Background: Miningcore's bundled odocrypt.cpp originally used a different,
// simplified RNG (struct Rand) instead of the consensus OdoRandom LCG, so it
// produced wrong hashes and every share was rejected. It has been replaced
// with DigiByte 8.26.2's crypto/odocrypt.cpp. This harness guards against a
// regression and lets you validate the algorithm without building the full
// .NET stack.
//
// Build & run (from src/Native/libmultihash, needs gcc/g++):
//   g++ -O2 -std=c++17 -c odocrypt.cpp -o odocrypt.o
//   gcc -O2          -c KeccakP-800-reference.c -o keccak.o
//   g++ -O2 -std=c++17 test_odocrypt.cpp odocrypt.o keccak.o -o test_odocrypt
//   ./test_odocrypt
//
// Exit code 0 and "PASS" on success; non-zero and "FAIL" on mismatch.

#include <cstdio>
#include <cstring>
#include <cstdint>

#include "odocrypt.h"
extern "C" {
#include "KeccakP-800-SnP.h"
void KeccakP800_Permute_12rounds(void *state);
}

// Replicates libmultihash/hashodo.h odocrypt_hash() (the body behind
// odocrypt_export), so this harness exercises the exact production path.
static void odocrypt_hash(const char *input, char *output, uint32_t len, uint32_t key)
{
    char cipher[KeccakP800_stateSizeInBytes] = {};
    memcpy(cipher, input, len);
    cipher[len] = 1;
    OdoCrypt(key).Encrypt(cipher, cipher);
    KeccakP800_Permute_12rounds(cipher);
    memcpy(output, cipher, 32);
}

int main()
{
    // Fixed vector: key = a realistic floored-ntime OdoKey, deterministic
    // 80-byte header. The expected digest was produced by the odo-miner
    // reference (hps/odocrypt_state.c + upstream Keccak), which is itself
    // verified bit-exact against DigiByte 8.26.2's crypto/odocrypt.cpp.
    const uint32_t key = 0x683b9800u;
    uint8_t input[80];
    for (int i = 0; i < 80; i++)
        input[i] = (uint8_t)(i * 7 + 3);

    // Expected 32-byte digest (little-endian, as emitted).
    const char *expected_hex =
        "25b32ac37d536e657e514b0c99fcc69ae73a425b3e26b75fd8b912dbf527c1ad";

    char out[32];
    odocrypt_hash((const char *)input, out, 80, key);

    char got_hex[65];
    for (int i = 0; i < 32; i++)
        sprintf(got_hex + 2 * i, "%02x", (uint8_t)out[i]);

    printf("key      : 0x%08x\n", key);
    printf("expected : %s\n", expected_hex);
    printf("got      : %s\n", got_hex);

    if (strcmp(got_hex, expected_hex) == 0) {
        printf("PASS — Miningcore OdoCrypt matches DigiByte consensus\n");
        return 0;
    }
    printf("FAIL — hash mismatch (odocrypt.cpp is not the consensus version)\n");
    return 1;
}
