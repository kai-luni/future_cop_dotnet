/* Executes unchanged PS1 C spawner runtime units (func_80060E74, func_800616A0, func_8006125C,
 * func_800614BC, func_8006158C, func_8002AE54) against shared golden vectors. */
#include "game/types.h"

struct Stats { u8 pad[2]; u16 a; u8 p4[2]; u16 b; u8 p8[2]; u16 c; u8 pc[2]; u16 d; };
struct PlayerStats { u8 pad[0x2a]; u16 a; u8 p2c[2]; u16 b; u8 p30[2]; u16 c; u8 p34[2]; u16 d; };
struct SpPlayer { u8 pad[0x18]; u8 id; u8 p19[0x134 - 0x19]; struct PlayerStats *stats; };
struct OtherPlayer { u8 pad[0x18]; u8 id; u8 p19[0xac - 0x19]; struct Stats *stats; };
struct Mode { u8 pad[0x5c]; s32 active; };

struct TsacSub {
    u8 pad00[0x0C];
    s16 delay;
    s16 maxConcurrent;
    u16 flags;
    s16 remaining;
    s16 timer;
    u16 totalSpawned;
    s32 arg1;
    s32 arg2;
    void *handler;
    void *lsraList;
    u8 *tactPtr;
    void *owner;
};

struct SpawnerNode {
    void *blob;
    struct TsacSub *sub;
    s32 unk08;
    u8 pad0C[0x10];
    s32 id;
    s32 blobSize;
    u8 pad24[0x0C];
    s32 unk30;
    u8 pad34[4];
    s32 unk38;
};

struct Registry {
    s32 count;
    struct SpawnerNode *items[0x80];
};

extern void gRegisterUnitSpawner(void *arg0) __asm__("func_80060E74");
extern void gUpdateRegisteredSpawners(s32 elapsed) __asm__("func_800616A0");
extern void gApplySpawnerCommandById(s32 id, s32 cmd, s32 arg2) __asm__("func_8006158C");
extern void *gGetEntityHandlerForType(u8 type) __asm__("func_8002AE54");

extern struct Registry *gRegistryTable __asm__("D_8009F800");
extern struct Mode *gGameSession __asm__("D_8009FA94");
extern struct SpPlayer *gSinglePlayerEntity __asm__("D_8009F9EC");
extern struct OtherPlayer *gPlayerEntities __asm__("D_8009FA58");
extern struct OtherPlayer *gPlayerEntity2 __asm__("D_8009FA5C");

/* ---- engine stubs ---- */
static s32 g_rand_val;
static s32 g_gate_ret;
static int g_spawn_calls;
static int g_post_calls;
static struct TsacSub g_tsac;
static struct { u32 tag; u32 size; u32 id; s32 count; } g_lsra;
static u8 g_tact_buf[0x40];

void func_80019A74(void *r) { (void)r; }
void *func_80019BF0(s32 a, s32 b) { (void)a; (void)b; return 0; }
s32 func_80019668(void) { return g_rand_val; }

void *func_80019CA0(void *blob, s32 size, u32 tag, s32 id)
{
    (void)blob; (void)size; (void)id;
    if (tag == 0x74534143u) return &g_tsac;
    if (tag == 0x6152534Cu) return &g_lsra;
    return 0;
}

void *func_80019C38(void *blob, s32 size, u32 tag, s32 id)
{
    (void)blob; (void)size; (void)id;
    if (tag == 0x74414354u) return g_tact_buf + 4;
    return 0;
}

s32 gInterpretEntityScript(s32 script, s32 a1, s32 a2, s32 a3) __asm__("func_80069D68");
s32 gInterpretEntityScript(s32 script, s32 a1, s32 a2, s32 a3)
{
    (void)a1; (void)a2; (void)a3;
    if (script == g_tsac.arg1) return g_gate_ret;
    if (script == g_tsac.arg2) g_post_calls++;
    return 1;
}

s32 gSpawnTargetableEntityId(void *def) __asm__("func_8004D4F8");
s32 gSpawnTargetableEntityId(void *def)
{
    (void)def;
    g_spawn_calls++;
    return 1;
}

/* Stubs for handler symbols referenced by 8002AE54.c jump table */
#define STUB_FN(name) void name(void); void name(void) {}
STUB_FN(func_8002AD58) STUB_FN(func_8002D664) STUB_FN(func_8002E034) STUB_FN(func_8002E5C4)
STUB_FN(func_8002E784) STUB_FN(func_8002F018) STUB_FN(func_8002F504) STUB_FN(func_8002FC90)
STUB_FN(func_80030A30) STUB_FN(func_80031120) STUB_FN(func_80032F3C) STUB_FN(func_800357C0)
STUB_FN(func_800382B4) STUB_FN(func_800395A8) STUB_FN(func_800398EC) STUB_FN(func_80039F28)
STUB_FN(func_8003A1B8) STUB_FN(func_8003AC04) STUB_FN(func_8003B0B8) STUB_FN(func_80041B6C)
STUB_FN(func_80048A6C) STUB_FN(func_800588D4) STUB_FN(func_80062FC0) STUB_FN(func_8006CF10)
STUB_FN(func_8003151C) STUB_FN(func_80039EB8) STUB_FN(func_80030C78) STUB_FN(func_80038754)
STUB_FN(func_8003B1D8) STUB_FN(func_80042614) STUB_FN(func_80042AF8) STUB_FN(func_80044314)
STUB_FN(func_8004463C) STUB_FN(func_8005C8F8) STUB_FN(func_8005908C)

static int read0(char *buf, int count)
{
    register int v0 __asm__("$2") = 4003;
    register int a0 __asm__("$4") = 0;
    register char *a1 __asm__("$5") = buf;
    register int a2 __asm__("$6") = count;
    __asm__ volatile("syscall" : "+r"(v0) : "r"(a0), "r"(a1), "r"(a2) : "memory", "$7");
    return v0;
}

static int write1(const char *buf, int count)
{
    register int v0 __asm__("$2") = 4004;
    register int a0 __asm__("$4") = 1;
    register const char *a1 __asm__("$5") = buf;
    register int a2 __asm__("$6") = count;
    __asm__ volatile("syscall" : "+r"(v0) : "r"(a0), "r"(a1), "r"(a2) : "memory", "$7");
    return v0;
}

static void zero_bytes(void *dst, int n)
{
    u8 *p = (u8 *)dst;
    while (n-- > 0) *p++ = 0;
}

static void put_num(int v)
{
    char buf[16];
    int i = 0, neg = 0;
    if (v < 0) { neg = 1; v = -v; }
    if (v == 0) buf[i++] = '0';
    while (v > 0) { buf[i++] = (char)('0' + (v % 10)); v /= 10; }
    if (neg) buf[i++] = '-';
    while (i-- > 0) write1(&buf[i], 1);
}

static void put_str(const char *s)
{
    int n = 0;
    while (s[n]) n++;
    write1(s, n);
}

void __main(void) {}

int main(void)
{
    char text[16384], *p;
    int len = 0, n, i, cases = 0, failures = 0;
    /* Inputs (13): rawDelay maxConc initFlags initRem randVal cmd cmdArg elapsed gateRet entityType team infoType sessionActive
     * Outputs (9): scaledDelay initTimer spawns postCalls totalSpawned remAfter timerAfter flagsAfter statSum */
    s32 v[22];
    struct Registry reg;
    struct SpawnerNode node;
    struct Mode mode;
    struct SpPlayer sp;
    struct PlayerStats spStats;
    struct OtherPlayer p0;
    struct Stats p0Stats;

    while (len < (int)sizeof(text) - 1) {
        n = read0(text + len, sizeof(text) - 1 - len);
        if (n < 0) return 2;
        if (n == 0) break;
        len += n;
    }
    text[len] = 0;
    p = text;

    while (*p) {
        int initTimer;
        s32 actual[9];
        while (*p == ' ' || *p == '\n' || *p == '\r' || *p == '\t') p++;
        if (*p == '#') { while (*p && *p != '\n') p++; continue; }
        if (!*p) break;
        while (*p && *p != ' ' && *p != '\t' && *p != '\n' && *p != '\r') p++;
        for (i = 0; i < 22; i++) {
            int sign = 1;
            while (*p == ' ' || *p == '\t') p++;
            if (i == 13) {
                if (p[0] != '-' || p[1] != '>') return 4;
                p += 2;
                while (*p == ' ' || *p == '\t') p++;
            }
            if (*p == '-') { sign = -1; p++; }
            if (*p < '0' || *p > '9') return 5;
            v[i] = 0;
            while (*p >= '0' && *p <= '9') { v[i] = v[i] * 10 + *p - '0'; p++; }
            v[i] *= sign;
        }
        while (*p == ' ' || *p == '\t' || *p == '\r') p++;
        if (*p != '\n' && *p != 0) return 6;

        zero_bytes(&reg, sizeof(reg));
        zero_bytes(&node, sizeof(node));
        zero_bytes(&g_tsac, sizeof(g_tsac));
        zero_bytes(&g_lsra, sizeof(g_lsra));
        zero_bytes(g_tact_buf, sizeof(g_tact_buf));
        zero_bytes(&mode, sizeof(mode));
        zero_bytes(&sp, sizeof(sp));
        zero_bytes(&spStats, sizeof(spStats));
        zero_bytes(&p0, sizeof(p0));
        zero_bytes(&p0Stats, sizeof(p0Stats));
        g_spawn_calls = 0;
        g_post_calls = 0;

        g_tsac.delay = (s16)v[0];
        g_tsac.maxConcurrent = (s16)v[1];
        g_tsac.flags = (u16)v[2];
        g_tsac.remaining = (s16)v[3];
        g_rand_val = v[4];
        g_gate_ret = v[8];

        g_lsra.size = 0x0C;
        g_tact_buf[0] = 77;
        g_tact_buf[4] = (u8)v[9];
        g_tact_buf[0x1C] = (u8)v[10];
        g_tact_buf[0x1D] = (u8)v[11];

        node.id = 77;
        node.unk38 = 1;
        node.unk30 = 1; /* arg2 = 1 + 1 = 2 */

        gRegistryTable = &reg;
        mode.active = v[12];
        gGameSession = &mode;
        sp.id = 2;
        sp.stats = &spStats;
        gSinglePlayerEntity = &sp;
        p0.id = 1;
        p0.stats = &p0Stats;
        gPlayerEntities = &p0;
        gPlayerEntity2 = 0;

        gRegisterUnitSpawner(&node);
        initTimer = g_tsac.timer;

        if (v[5] != 0) {
            gApplySpawnerCommandById(77, v[5], v[6]);
        }
        gUpdateRegisteredSpawners(v[7]);

        actual[0] = g_tsac.delay;
        actual[1] = initTimer;
        actual[2] = g_spawn_calls;
        actual[3] = g_post_calls;
        actual[4] = g_tsac.totalSpawned;
        actual[5] = g_tsac.remaining;
        actual[6] = g_tsac.timer;
        actual[7] = g_tsac.flags;
        actual[8] = spStats.a + spStats.b * 10 + spStats.c * 100 + spStats.d * 1000
                  + p0Stats.a + p0Stats.b * 10 + p0Stats.c * 100 + p0Stats.d * 1000;

        cases++;
        for (i = 0; i < 9; i++) {
            if (actual[i] != v[13 + i]) {
                failures++;
                put_str("FAIL case ");
                put_num(cases);
                put_str(" col ");
                put_num(i);
                put_str(": got ");
                put_num(actual[i]);
                put_str(" want ");
                put_num(v[13 + i]);
                put_str("\n");
            }
        }
    }

    put_str("csac-spawner: cases=");
    put_num(cases);
    put_str(" failures=");
    put_num(failures);
    put_str("\n");
    return failures == 0 ? 0 : 1;
}
