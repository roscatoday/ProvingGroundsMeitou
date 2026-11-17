// Force-included by golden/gen.sh (g++ -include): the MSVC-only CRT calls the upstream pure files use, mapped onto standard C.
#pragma once
#include <cstdio>
#include <cstring>
#include <cstdarg>
#include <cstddef>
#include <strings.h>
inline int sprintf_s(char* b, size_t n, const char* f, ...) { va_list a; va_start(a, f); int r = std::vsnprintf(b, n, f, a); va_end(a); return r; }
template <size_t N> inline int sprintf_s(char (&b)[N], const char* f, ...) { va_list a; va_start(a, f); int r = std::vsnprintf(b, N, f, a); va_end(a); return r; }
#define _snprintf std::snprintf
#define _stricmp strcasecmp
#define _strnicmp strncasecmp
inline int strncpy_s(char* d, size_t n, const char* s, size_t c) { if (!n) return 22; size_t k = c < n - 1 ? c : n - 1; std::strncpy(d, s, k); d[k] = 0; return 0; }
template <size_t N> inline int strncpy_s(char (&d)[N], const char* s, size_t c) { return strncpy_s(d, N, s, c); }
inline int strcpy_s(char* d, size_t n, const char* s) { return strncpy_s(d, n, s, std::strlen(s)); }
template <size_t N> inline int strcpy_s(char (&d)[N], const char* s) { return strncpy_s(d, N, s, std::strlen(s)); }
inline int strcat_s(char* d, size_t n, const char* s) { size_t k = std::strlen(d); if (k + std::strlen(s) + 1 > n) return 34; std::strcat(d, s); return 0; }
template <size_t N> inline int strcat_s(char (&d)[N], const char* s) { return strcat_s(d, N, s); }
