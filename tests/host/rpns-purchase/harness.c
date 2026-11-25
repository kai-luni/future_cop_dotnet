/* Executes unchanged func_80069D68 (gInterpretEntityScript, 80069D68.c) with shared golden
 * vectors for CONFT.MIS purchase scripts.
 *
 * All PS1 globals in 80069D68.c are referenced via extern __asm__("D_800...") symbols and
 * relocated by -0x70000000 into .retailram via --defsym in run.sh.
 */
#include "game/types.h"

extern u8 gScriptVmDisabled __asm__("D_8009F840");
extern s32 gOperandStack[] __asm__("D_800A3320");
extern s32 *gVmCountdowns __asm__("D_8009F9A4");
extern s32 *gOpcodeTable __asm__("D_8009F9F8");
extern s32 *gGameSession __asm__("D_8009FA94");
extern s32 *gScriptVmVars __asm__("D_8009FAD4");
extern s8 gRandomTable[] __asm__("D_800A8540");

extern s32 gInterpretEntityScript(u8 *script, s32 arg1, s32 arg2, void *entity) __asm__("func_80069D68");

/* Linux MIPS o32 syscalls (same minimal runtime as tests/host/outpost-message5/harness.c). */
static s32 sys_read(s32 fd, void *buf, s32 len)
{
    register s32 a0 __asm__("$4") = fd;
    register s32 a1 __asm__("$5") = (s32)buf;
    register s32 a2 __asm__("$6") = len;
    register s32 v0 __asm__("$2") = 4003;
    register s32 a3 __asm__("$7");
    __asm__ volatile("syscall" : "+r"(v0), "=r"(a3) : "r"(a0), "r"(a1), "r"(a2) : "memory");
    return a3 ? -v0 : v0;
}

static s32 sys_write(s32 fd, const void *buf, s32 len)
{
    register s32 a0 __asm__("$4") = fd;
    register s32 a1 __asm__("$5") = (s32)buf;
    register s32 a2 __asm__("$6") = len;
    register s32 v0 __asm__("$2") = 4004;
    register s32 a3 __asm__("$7");
    __asm__ volatile("syscall" : "+r"(v0), "=r"(a3) : "r"(a0), "r"(a1), "r"(a2) : "memory");
    return a3 ? -v0 : v0;
}

static void sys_exit(s32 code)
{
    register s32 a0 __asm__("$4") = code;
    register s32 v0 __asm__("$2") = 4001;
    __asm__ volatile("syscall" : : "r"(v0), "r"(a0));
    for (;;) {}
}

static void put_str(const char *s)
{
    s32 n = 0;
    while (s[n]) n++;
    sys_write(2, s, n);
}

static void put_int(s32 v)
{
    char b[16];
    s32 i = 15;
    u32 u;
    b[i] = 0;
    if (v < 0) { put_str("-"); u = (u32)(-v); } else { u = (u32)v; }
    if (u == 0) { b[--i] = '0'; }
    while (u > 0) { b[--i] = '0' + (u % 10); u /= 10; }
    put_str(&b[i]);
}

/* Recorded engine side effects from 80069D68.c stubs. */
static s32 s_spw_count;
static s32 s_spw_id;
static s32 s_spw_cmd;
static s32 s_spw_arg2;
static s32 s_evt_id;

s32 func_80019668(void) { return 0; }
s32 func_80029A60(s32 a, void *e, s32 m) { return 0; }
void func_800298EC(s32 a) { s_evt_id = a; }
void func_80029974(s32 a) {}
s32 func_8004DE80(s32 b, s32 a) { return 0; }
void func_8004DA84(void *e, s32 b, s32 a, s32 m) {}
void func_8004DC64(s32 c, s32 b, s32 a) {}
void func_8004DD68(s32 c, s32 b, s32 a) {}
void func_8004DDF4(s32 c, s32 b, s32 a) {}
void func_8004F0F0(s32 c, s32 b, s32 a) {}
void func_8006158C(s32 c, s32 b, s32 a) { s_spw_count++; s_spw_id = c; s_spw_cmd = b; s_spw_arg2 = a; }
void func_80061600(s32 c, s32 b, s32 a) {}
void func_800841C4(s32 c, s32 b, s32 a) {}

static s32 s_session[0x50];
static s32 s_ptr_targets[16];
static s32 s_vars[64];
static s32 s_countdowns[32];
static s32 s_optab[32];
static char s_inbuf[65536];
static u8 s_script[256];

static s32 hex_val(char c)
{
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
    return -1;
}

static s32 parse_int(const char **pp)
{
    const char *p = *pp;
    s32 neg = 0, v = 0;
    while (*p == ' ' || *p == '\t') p++;
    if (*p == '-') { neg = 1; p++; }
    while (*p >= '0' && *p <= '9') { v = v * 10 + (*p - '0'); p++; }
    *pp = p;
    return neg ? -v : v;
}

void __main(void) {}

void main(void)
{
    s32 n = 0, r;
    const char *p;
    s32 cases = 0;

    while (n < (s32)sizeof(s_inbuf) - 1) {
        r = sys_read(0, s_inbuf + n, (s32)sizeof(s_inbuf) - 1 - n);
        if (r <= 0) break;
        n += r;
    }
    s_inbuf[n] = 0;

    p = s_inbuf;
    while (*p) {
        const char *name;
        s32 name_len = 0;
        s32 slen = 0;
        s32 i, in[9], exp[14], got[14];
        s32 ret;

        while (*p == ' ' || *p == '\t' || *p == '\r' || *p == '\n') p++;
        if (!*p) break;
        if (*p == '#') { while (*p && *p != '\n') p++; continue; }

        name = p;
        while (*p && *p != ' ' && *p != '\t') { p++; name_len++; }
        while (*p == ' ' || *p == '\t') p++;
        while (*p && *p != ' ' && *p != '\t') {
            s32 hi = hex_val(*p++);
            s32 lo = hex_val(*p++);
            s_script[slen++] = (u8)((hi << 4) | lo);
        }
        for (i = 0; i < 9; i++) in[i] = parse_int(&p);
        while (*p == ' ' || *p == '\t') p++;
        if (p[0] == '-' && p[1] == '>') p += 2;
        for (i = 0; i < 14; i++) exp[i] = parse_int(&p);
        while (*p && *p != '\n') p++;

        /* Reset VM state */
        gScriptVmDisabled = 0;
        gGameSession = s_session;
        gScriptVmVars = s_vars;
        gVmCountdowns = s_countdowns;
        gOpcodeTable = s_optab;
        for (i = 0; i < 0x50; i++) s_session[i] = 0;
        for (i = 0; i < 16; i++) {
            s_ptr_targets[i] = 0;
            s_session[0x39 + i] = (s32)&s_ptr_targets[i];
        }
        for (i = 0; i < 64; i++) s_vars[i] = 0;
        for (i = 0; i < 32; i++) s_countdowns[i] = -10;
        s_spw_count = 0; s_spw_id = -1; s_spw_cmd = -1; s_spw_arg2 = -1; s_evt_id = -1;

        s_session[25] = in[0];
        s_session[26] = in[1];
        s_session[27] = in[2];
        s_session[28] = in[3];
        s_session[29] = in[4];
        s_session[30] = in[5];
        if (in[6] >= 0 && in[6] < 64) s_vars[in[6]] = in[7];
        s_session[3] = in[8];

        ret = gInterpretEntityScript(s_script, 0, -1, 0);

        got[0] = ret;
        got[1] = s_session[25];
        got[2] = s_session[26];
        got[3] = s_session[27];
        got[4] = s_session[28];
        got[5] = s_session[29];
        got[6] = s_session[30];
        got[7] = (in[6] >= 0 && in[6] < 64) ? s_vars[in[6]] : 0;
        got[8] = s_ptr_targets[3];
        got[9] = s_spw_count;
        got[10] = s_spw_id;
        got[11] = s_spw_cmd;
        got[12] = s_spw_arg2;
        got[13] = s_evt_id;

        for (i = 0; i < 14; i++) {
            if (got[i] != exp[i]) {
                sys_write(2, name, name_len);
                put_str(": field "); put_int(i);
                put_str(" expected "); put_int(exp[i]);
                put_str(" got "); put_int(got[i]);
                put_str("\n");
                sys_exit(1);
            }
        }
        cases++;
    }
    if (cases < 10) { put_str("too few cases\n"); sys_exit(2); }
    sys_exit(0);
}
