#ifndef TPCWEI_SECURITY_H
#define TPCWEI_SECURITY_H

#include "p2p_core.h"

#ifdef __cplusplus
extern "C" {
#endif

#define P2P_PRIVATE_CODE_LENGTH 64
#define P2P_PUBLIC_CODE_LENGTH 128
#define P2P_SHA256_HEX_LENGTH 64

typedef struct P2PSecurityPairingResult {
    char private_code[P2P_PRIVATE_CODE_LENGTH + 1];
    char public_code[P2P_PUBLIC_CODE_LENGTH + 1];
    char private_hash_hex[P2P_SHA256_HEX_LENGTH + 1];
    char public_hash_hex[P2P_SHA256_HEX_LENGTH + 1];
} P2PSecurityPairingResult;

P2P_EXPORT int P2P_CALL p2p_security_generate_pairing_codes(const char* device_code, P2PSecurityPairingResult* out_result);
P2P_EXPORT int P2P_CALL p2p_security_private_to_public(const char* private_code, char* public_code_buffer, uint32_t public_code_buffer_length, char* public_hash_hex_buffer, uint32_t public_hash_buffer_length);
P2P_EXPORT int P2P_CALL p2p_security_private_group_to_public(const char* private_codes_text, char* public_code_buffer, uint32_t public_code_buffer_length, char* public_hash_hex_buffer, uint32_t public_hash_buffer_length);
P2P_EXPORT int P2P_CALL p2p_security_validate_pairing(const char* private_code, const char* public_code, uint8_t* out_is_valid);
P2P_EXPORT int P2P_CALL p2p_security_hash_sha256_hex(const uint8_t* data, uint32_t data_length, char* hash_hex_buffer, uint32_t hash_hex_buffer_length);

#ifdef __cplusplus
}
#endif

#endif
