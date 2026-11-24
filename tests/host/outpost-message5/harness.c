/* Executes unchanged func_8002D664 (gHandleLinkedPropEntity) message 5 with shared golden vectors.
 *
 * 8002D664.c reads its globals through absolute literals (8002D664.c:185,187-189,192-193,199-202,
 * 208-209), which compile to `lui rX,0x800A`. run.sh rebases exactly those literals by -0x70000000 in
 * the compiled assembly and places every D_ symbol at the same rebased address, so the literal loads
 * and the symbolic D_8009FA58 load hit the same harness memory (as precinct_assault_test.sh of the
 * decomp does for func_8007A4CC). */
#include "game/types.h"

struct Inner {
    u8 pad00[0x18];
    u16 unk18;
    u8 pad1A[0x1E];
    u16 unk38;
};

struct State {
    u8 pad00[0x14];
    u16 unk14;
    u8 pad16[2];
    u8 unk18;
    u8 pad19[0x11B];
    struct Inner *unk134;
};

struct Ac {
    u8 pad00[0x10];
    u16 unk10;
    u8 pad12[0xAF];
    s8 unkC1;
};

struct Player {
    u8 pad00[0x18];
    u8 unk18;
    u8 pad19[0x93];
    struct Ac *unkAC;
};

struct Obj {
    u8 pad00[0x16];
    u8 unk16;
    u8 pad17[5];
    u16 unk1C;
    u8 pad1E[6];
    u32 flags;
    u8 pad28[0x64];
    u32 unk8C;
    u8 pad90[0x4F];
    u8 unkDF;
    u8 padE0[0x16];
    u8 unkF6;
    u8 unkF7;
    u8 padF8[6];
    u8 unkFE;
    u8 padFF[1];
    struct Obj *unk100;
    struct Obj *unk104;
    u8 pad108[0x10];
};

struct Found {
    u8 pad00[0x14];
    u16 unk14;
};

struct Menu {
    u8 pad00[0x64];
    s32 unk64[2];
    u8 pad6C[0x10];
    s32 unk7C[2];
    u8 pad84[0x5C];
    s32 unkE0;
};

/* Placed by run.sh at (retail address - 0x70000000), where the rebased literals point. */
extern s32 gPlayerCount __asm__("D_8009FA0C");
extern struct State *gSinglePlayerEntity __asm__("D_8009F9EC");
extern struct Menu *gGameSession __asm__("D_8009FA94");
extern struct Player *gPlayerEntities[2] __asm__("D_8009FA58");

s32 gHandleLinkedPropEntity(s32 message, struct Obj *obj, void *arg2, s32 arg3) __asm__("func_8002D664");

/* ---- stubs: engine services outside the message-5 scoring claim ---- */
static s32 g_base_calls, g_base_message;
static s32 g_lookup_key, g_found_present;
static struct Found g_found;

s32 gEntityBaseHandler(s32 message, struct Obj *object, void *data, s32 arg3) __asm__("func_8002AD58");
s32 gEntityBaseHandler(s32 message, struct Obj *object, void *data, s32 arg3)
{
    (void)object; (void)data; (void)arg3;
    g_base_calls++;
    g_base_message = message;
    return 0;
}

void gInitObjectFromDef(struct Obj *obj, void *data, s32 arg2) __asm__("func_8002C584");
void gInitObjectFromDef(struct Obj *obj, void *data, s32 arg2) { (void)obj; (void)data; (void)arg2; }
s32 gUpdatePropTick(struct Obj *obj, void *data) __asm__("func_8002CC30");
s32 gUpdatePropTick(struct Obj *obj, void *data) { (void)obj; (void)data; return 0; }
void gTryCaptureObjective(struct Obj *obj, void *data) __asm__("func_8002D1A0");
void gTryCaptureObjective(struct Obj *obj, void *data) { (void)obj; (void)data; }
void gRemoveGroundKeyEntry(s32 key) __asm__("func_80063194");
void gRemoveGroundKeyEntry(s32 key) { (void)key; }

struct Found *gFindUnitById(s32 id) __asm__("func_8004D664");
struct Found *gFindUnitById(s32 id)
{
    g_lookup_key = id;
    return g_found_present ? &g_found : 0;
}

/* ---- I/O ---- */
static int sys3(int nr, int a, long b, int c)
{
    register int v0 __asm__("$2") = nr;
    register int a0 __asm__("$4") = a;
    register long a1 __asm__("$5") = b;
    register int a2 __asm__("$6") = c;
    __asm__ volatile("syscall" : "+r"(v0) : "r"(a0), "r"(a1), "r"(a2) : "memory", "$7");
    return v0;
}

static void say(const char *s)
{
    int n = 0;
    while (s[n]) n++;
    sys3(4004, 1, (long)s, n);
}

static void say_dec(s32 v)
{
    char b[12];
    int i = 11;
    u32 u = v < 0 ? (u32)-v : (u32)v;
    b[i] = 0;
    do { b[--i] = '0' + u % 10; u /= 10; } while (u);
    if (v < 0) b[--i] = '-';
    say(b + i);
}

static void zero_bytes(void *dst, int n)
{
    u8 *p = (u8 *)dst;
    while (n-- > 0) *p++ = 0;
}

void __main(void) {}

#define NIN 15
#define NOUT 10

int main(void)
{
    static char text[16384];
    static const char *outNames[NOUT] = {
        "score0", "score1", "total0", "total1", "p0Count10", "p1Count10",
        "stCount38", "stTimer18", "e0Out", "lookupKey",
    };
    char *p, *name;
    int len = 0, n, i, count = 0, failures = 0;
    s32 v[NIN + NOUT], got[NOUT];
    struct Obj obj;
    struct Player p0, p1;
    struct Ac ac0, ac1;
    struct State st;
    struct Inner inner;
    struct Menu menu;

    while (len < (int)sizeof(text) - 1) {
        n = sys3(4003, 0, (long)(text + len), sizeof(text) - 1 - len);
        if (n < 0) return 2;
        if (n == 0) break;
        len += n;
    }
    if (len == (int)sizeof(text) - 1) return 3;
    text[len] = 0;
    p = text;

    while (*p) {
        while (*p == ' ' || *p == '\n' || *p == '\r' || *p == '\t') p++;
        if (*p == '#') { while (*p && *p != '\n') p++; continue; }
        if (!*p) break;
        name = p;
        while (*p && *p != ' ' && *p != '\t' && *p != '\n' && *p != '\r') p++;
        if (*p) *p++ = 0;
        for (i = 0; i < NIN + NOUT; i++) {
            int sign = 1;
            while (*p == ' ' || *p == '\t') p++;
            if (i == NIN) {
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

        zero_bytes(&obj, sizeof(obj));
        zero_bytes(&p0, sizeof(p0));
        zero_bytes(&p1, sizeof(p1));
        zero_bytes(&ac0, sizeof(ac0));
        zero_bytes(&ac1, sizeof(ac1));
        zero_bytes(&st, sizeof(st));
        zero_bytes(&inner, sizeof(inner));
        zero_bytes(&menu, sizeof(menu));
        zero_bytes(&g_found, sizeof(g_found));
        g_base_calls = 0;
        g_base_message = 0;
        g_lookup_key = -1;

        /* Outpost: type 0x24, teams F6=1 / F7=2, latch FE and flag 0x4000000 from the vector. */
        obj.unk16 = 0x24;
        obj.unkF6 = 1;
        obj.unkF7 = 2;
        obj.unkFE = (u8)v[8];
        obj.flags = v[9] ? 0x4000000 : 0;
        obj.unk8C = (u32)v[10];

        gPlayerCount = v[0];
        p0.unk18 = (u8)v[1];
        p0.unkAC = v[2] >= 0 ? &ac0 : 0;
        ac0.unkC1 = (s8)v[2];
        p1.unk18 = (u8)v[3];
        p1.unkAC = v[4] >= 0 ? &ac1 : 0;
        ac1.unkC1 = (s8)v[4];
        gPlayerEntities[0] = v[1] >= 0 ? &p0 : 0;
        gPlayerEntities[1] = v[3] >= 0 ? &p1 : 0;

        st.unk14 = (u16)v[7];
        st.unk18 = (u8)v[6];
        st.unk134 = &inner;
        inner.unk18 = (u16)v[13];
        gSinglePlayerEntity = v[5] ? &st : 0;

        g_found_present = v[11];
        g_found.unk14 = (u16)v[12];

        gGameSession = &menu;
        menu.unkE0 = v[14];

        gHandleLinkedPropEntity(5, &obj, 0, 0);

        got[0] = menu.unk64[0];
        got[1] = menu.unk64[1];
        got[2] = menu.unk7C[0];
        got[3] = menu.unk7C[1];
        got[4] = ac0.unk10;
        got[5] = ac1.unk10;
        got[6] = inner.unk38;
        got[7] = inner.unk18;
        got[8] = menu.unkE0;
        got[9] = g_lookup_key;
        for (i = 0; i < NOUT; i++) {
            if (got[i] != v[NIN + i]) {
                say("outpost-message5: FAIL "); say(name); say(" "); say(outNames[i]);
                say(" got "); say_dec(got[i]); say(" want "); say_dec(v[NIN + i]); say("\n");
                failures++;
            }
        }
        /* Every message-5 path ends in the base handler (8002D664.c:205,229). */
        if (g_base_calls != 1 || g_base_message != 5) {
            say("outpost-message5: FAIL "); say(name); say(" base handler\n");
            failures++;
        }
        count++;
    }
    say("outpost-message5: cases="); say_dec(count); say(" failures="); say_dec(failures); say("\n");
    return failures ? 1 : (count == 0 ? 7 : 0);
}
