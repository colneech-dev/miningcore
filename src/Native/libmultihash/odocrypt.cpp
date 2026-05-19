#include "odocrypt.h"
#include <algorithm>
#include <cstring>

namespace {
    struct Rand {
        uint64_t state;
        Rand(uint32_t seed) : state(seed) { for (int i = 0; i < 12; i++) next(); }
        uint64_t next() {
            state = state * 6364136223846793005ULL + 1442695040888963407ULL;
            return state;
        }
        uint32_t boundedNext(uint32_t bound) {
            uint32_t threshold = -bound % bound;
            while (true) { uint32_t maybe = next() >> 32; if (maybe >= threshold) return maybe % bound; }
        }
        template<class T, size_t N>
        void permutation(T (&arr)[N]) {
            for (size_t i = 0; i < N; i++) arr[i] = i;
            for (size_t i = 1; i < N; i++) std::swap(arr[i], arr[boundedNext(i+1)]);
        }
    };
}

OdoCrypt::OdoCrypt(uint32_t key) {
    Rand r(key);
    for (int i = 0; i < SMALL_SBOX_COUNT; i++) {
        uint8_t perm[1 << SMALL_SBOX_WIDTH];
        r.permutation(perm);
        for (int j = 0; j < (1 << SMALL_SBOX_WIDTH); j++) Sbox1[i][j] = perm[j];
    }
    for (int i = 0; i < LARGE_SBOX_COUNT; i++) {
        uint16_t perm[1 << LARGE_SBOX_WIDTH];
        r.permutation(perm);
        for (int j = 0; j < (1 << LARGE_SBOX_WIDTH); j++) Sbox2[i][j] = perm[j];
    }
    for (int i = 0; i < 2; i++) {
        for (int j = 0; j < PBOX_SUBROUNDS; j++)
            for (int k = 0; k < STATE_SIZE/2; k++) Permutation[i].mask[j][k] = r.next();
        for (int j = 0; j < PBOX_SUBROUNDS-1; j++)
            for (int k = 0; k < STATE_SIZE/2; k++) Permutation[i].rotation[j][k] = r.boundedNext(WORD_BITS-1) + 1;
    }
    {
        int available[WORD_BITS-1];
        for (int i = 0; i < WORD_BITS-1; i++) available[i] = i+1;
        for (int i = 0; i < ROTATION_COUNT; i++) {
            int j = r.boundedNext(WORD_BITS-1-i);
            Rotations[i] = available[j];
            available[j] = available[WORD_BITS-2-i];
        }
    }
    for (int i = 0; i < ROUNDS; i++) RoundKey[i] = r.next() >> (64 - 14);
}

void OdoCrypt::Encrypt(char cipher[DIGEST_SIZE], const char plain[DIGEST_SIZE]) const {
    uint64_t state[STATE_SIZE];
    Unpack(state, plain);
    PreMix(state);
    for (int round = 0; round < ROUNDS; round++) {
        ApplyPbox(state, Permutation[0]);
        ApplySboxes(state, Sbox1, Sbox2);
        ApplyPbox(state, Permutation[1]);
        ApplyRotations(state, Rotations);
        ApplyRoundKey(state, RoundKey[round]);
    }
    Pack(state, cipher);
}

void OdoCrypt::Decrypt(char plain[DIGEST_SIZE], const char cipher[DIGEST_SIZE]) const {
    uint64_t state[STATE_SIZE];
    Unpack(state, cipher);
    for (int round = ROUNDS-1; round >= 0; round--) {
        ApplyRoundKey(state, RoundKey[round]);
        ApplyRotations(state, Rotations);
        ApplyInvPbox(state, Permutation[1]);
        uint8_t invSbox1[SMALL_SBOX_COUNT][1 << SMALL_SBOX_WIDTH];
        for (int i = 0; i < SMALL_SBOX_COUNT; i++)
            for (int j = 0; j < (1 << SMALL_SBOX_WIDTH); j++) invSbox1[i][Sbox1[i][j]] = j;
        uint16_t invSbox2[LARGE_SBOX_COUNT][1 << LARGE_SBOX_WIDTH];
        for (int i = 0; i < LARGE_SBOX_COUNT; i++)
            for (int j = 0; j < (1 << LARGE_SBOX_WIDTH); j++) invSbox2[i][Sbox2[i][j]] = j;
        ApplySboxes(state, invSbox1, invSbox2);
        ApplyInvPbox(state, Permutation[0]);
    }
    PreMix(state);
    Pack(state, plain);
}

void OdoCrypt::Unpack(uint64_t state[STATE_SIZE], const char bytes[DIGEST_SIZE]) {
    for (int i = 0; i < STATE_SIZE; i++) {
        state[i] = 0;
        for (int j = 0; j < WORD_BITS/8; j++) state[i] |= (uint64_t)(uint8_t)bytes[i*(WORD_BITS/8) + j] << (8*j);
    }
}

void OdoCrypt::Pack(const uint64_t state[STATE_SIZE], char bytes[DIGEST_SIZE]) {
    for (int i = 0; i < STATE_SIZE; i++)
        for (int j = 0; j < WORD_BITS/8; j++) bytes[i*(WORD_BITS/8) + j] = state[i] >> (8*j);
}

void OdoCrypt::PreMix(uint64_t state[STATE_SIZE]) {
    uint64_t total = 0;
    for (int i = 0; i < STATE_SIZE; i++) total ^= state[i];
    total ^= total >> 32;
    for (int i = 0; i < STATE_SIZE; i++) state[i] ^= total;
}

void OdoCrypt::ApplySboxes(uint64_t state[STATE_SIZE],
    const uint8_t sbox1[SMALL_SBOX_COUNT][1 << SMALL_SBOX_WIDTH],
    const uint16_t sbox2[LARGE_SBOX_COUNT][1 << LARGE_SBOX_WIDTH]) {
    const uint64_t smallMask = (1 << SMALL_SBOX_WIDTH) - 1;
    const uint64_t largeMask = (1 << LARGE_SBOX_WIDTH) - 1;
    int smallIndex = 0;
    for (int i = 0; i < STATE_SIZE; i++) {
        uint64_t next = 0; int pos = 0;
        for (int j = 0; j < WORD_BITS / (SMALL_SBOX_WIDTH + LARGE_SBOX_WIDTH); j++) {
            next |= (uint64_t)sbox1[smallIndex][state[i] >> pos & smallMask] << pos;
            pos += SMALL_SBOX_WIDTH; smallIndex++;
            next |= (uint64_t)sbox2[i][state[i] >> pos & largeMask] << pos;
            pos += LARGE_SBOX_WIDTH;
        }
        state[i] = next;
    }
}

void OdoCrypt::ApplyMaskedSwaps(uint64_t state[STATE_SIZE], const uint64_t mask[STATE_SIZE/2]) {
    for (int i = 0; i < STATE_SIZE/2; i++) {
        uint64_t diff = (state[2*i] ^ state[2*i+1]) & mask[i];
        state[2*i] ^= diff; state[2*i+1] ^= diff;
    }
}

void OdoCrypt::ApplyWordShuffle(uint64_t state[STATE_SIZE], int m) {
    uint64_t next[STATE_SIZE];
    for (int i = 0; i < STATE_SIZE; i++) next[i] = state[i*m % STATE_SIZE];
    for (int i = 0; i < STATE_SIZE; i++) state[i] = next[i];
}

void OdoCrypt::ApplyPboxRotations(uint64_t state[STATE_SIZE], const int rotation[STATE_SIZE/2]) {
    for (int i = 0; i < STATE_SIZE/2; i++) {
        int r = rotation[i];
        state[2*i+1] = (state[2*i+1] << r) | (state[2*i+1] >> (WORD_BITS-r));
    }
}

void OdoCrypt::ApplyPbox(uint64_t state[STATE_SIZE], const Pbox& perm) {
    for (int i = 0; i < PBOX_SUBROUNDS-1; i++) {
        ApplyMaskedSwaps(state, perm.mask[i]);
        ApplyWordShuffle(state, PBOX_M);
        ApplyPboxRotations(state, perm.rotation[i]);
    }
    ApplyMaskedSwaps(state, perm.mask[PBOX_SUBROUNDS-1]);
}

void OdoCrypt::ApplyInvPbox(uint64_t state[STATE_SIZE], const Pbox& perm) {
    ApplyMaskedSwaps(state, perm.mask[PBOX_SUBROUNDS-1]);
    for (int i = PBOX_SUBROUNDS-2; i >= 0; i--) {
        int invRotation[STATE_SIZE/2];
        for (int j = 0; j < STATE_SIZE/2; j++) invRotation[j] = WORD_BITS - perm.rotation[i][j];
        ApplyPboxRotations(state, invRotation);
        ApplyWordShuffle(state, INV_PBOX_M);
        ApplyMaskedSwaps(state, perm.mask[i]);
    }
}

void OdoCrypt::ApplyRotations(uint64_t state[STATE_SIZE], const int rotations[ROTATION_COUNT]) {
    uint64_t next[STATE_SIZE];
    for (int i = 0; i < STATE_SIZE; i++) next[i] = state[i];
    for (int i = 0; i < STATE_SIZE; i++)
        for (int j = 0; j < ROTATION_COUNT; j++) {
            int r = rotations[j];
            next[i] ^= (state[i] << r) | (state[i] >> (WORD_BITS-r));
        }
    for (int i = 0; i < STATE_SIZE; i++) state[i] = next[i];
}

void OdoCrypt::ApplyRoundKey(uint64_t state[STATE_SIZE], int roundKey) {
    for (int i = 0; i < STATE_SIZE; i++)
        for (int j = 0; j < WORD_BITS; j += 16) state[i] ^= (uint64_t)roundKey << j;
}
