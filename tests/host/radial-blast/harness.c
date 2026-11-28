/* Executes unchanged func_800410BC (gApplyRadialContactDamage) together with its two wrappers
 * func_800413EC / func_80041428 and func_8001A750 (the planar distance) with shared golden
 * vectors. Covers the radius cutoff, the inner dead zone, the vertical band, the direction
 * cone, the falloff rounding, the +0x08 / 0x200080 skip flags and the parameter block the
 * wrappers publish and clear.
 *
 * Inputs (39): x y z strength radius srcWord srcPresent context coneFlag bandBits threshold
 *              axisX dirZ mode count, then per object i (4x): id flags ox oz oyc oyr
 * Outputs (23): scale radius, then per object i (4x): calls msg unk0 unkE,
 *               posOk arg3Ok, then the post-call context coneFlag bandBits
 *
 * mode 0 = plain func_800410BC (globals from the inputs), 1 = func_800413EC (banded, mode word
 * from bandBits), 2 = func_80041428 (cone, threshold/axis from threshold/axisX/dirZ << 4).
 *
 * Build with -DEMIT to print the computed output line instead of comparing it; that is how
 * tests/golden/radial-blast.vectors was produced. */
#include "game/types.h"

struct Vec3s32 {
    s32 x;
    s32 y;
    s32 z;
};

struct Object;
typedef void (*ObjectFunc)(s32, struct Object *, void *, s32);

struct Object {
    u8 pad00[4];
    ObjectFunc unk04;
    s32 unk08;
    u8 pad0C[0x18];
    s32 unk24;
    u8 pad28[0x28];
    s32 unk50;
    u8 pad54[4];
    s32 unk58;
    u8 pad5C[4];
    s32 unk60;
    u8 pad64[4];
    s32 unk68;
};

struct Arg3 {
    u8 pad00[0x14];
    u16 unk14;
};

struct CallArgs {
    s32 unk0;
    struct Vec3s32 *unk4;
    struct Arg3 *unk8;
    s16 unkC;
    s16 unkE;
};

/* The blast parameter block and the active effect list the walker reads. */
s32 D_8009F324;
u8 D_8009F328;
u8 D_8009F329;
s32 D_8009F700;
s32 D_8009F704;
s16 D_8009F710;
s16 D_8009F718;
s16 D_8009F71C;

/* D_800A8F98 points at the entry array; the count lives two words in front of it. */
struct Object *gListStore[6];
__asm__(".globl D_800A8F98\n\tD_800A8F98 = gListStore + 8");

extern void gApplyRadialContactDamage(struct Vec3s32 *pos, s32 arg1, s32 arg2,
                                      struct Arg3 *arg3) __asm__("func_800410BC");
extern void gApplyBandedRadialDamage(s32 arg0, s32 arg1, s32 arg2, s32 context, s32 arg4,
                                     s32 mode) __asm__("func_800413EC");
extern void gApplyConeContactDamage(s32 arg0, s32 arg1, s32 arg2, s32 context,
                                    struct Vec3s32 *position, s32 height,
                                    s32 arg6) __asm__("func_80041428");

#define MAXOBJ 4
#define NIN (15 + 6 * MAXOBJ)
#define NOUT (2 + 4 * MAXOBJ + 2 + 3)

static struct Object gObjs[MAXOBJ];
static struct Vec3s32 *gCenter;
static struct Arg3 *gSource;
static int gCalls[MAXOBJ];
static s32 gMsg[MAXOBJ], gUnk0[MAXOBJ], gUnkE[MAXOBJ];
static int gPosOk[MAXOBJ], gArg3Ok[MAXOBJ];

static void handler(s32 msg, struct Object *obj, void *args, s32 a3)
{
    struct CallArgs *ca = (struct CallArgs *)args;
    int i;

    (void)a3;
    for (i = 0; i < MAXOBJ; i++) {
        if (obj != &gObjs[i]) {
            continue;
        }
        if (gCalls[i] == 0) {
            gMsg[i] = msg;
            gUnk0[i] = ca->unk0;
            gUnkE[i] = ca->unkE;
            gPosOk[i] = ca->unk4 == gCenter;
            gArg3Ok[i] = ca->unk8 == gSource;
        }
        gCalls[i]++;
        return;
    }
}

static int read0(char *buf, int count)
{
    register int v0 __asm__("$2") = 4003;
    register int a0 __asm__("$4") = 0;
    register char *a1 __asm__("$5") = buf;
    register int a2 __asm__("$6") = count;
    __asm__ volatile("syscall" : "+r"(v0) : "r"(a0), "r"(a1), "r"(a2) : "memory", "$7");
    return v0;
}

static int write0(char *buf, int count)
{
    register int v0 __asm__("$2") = 4004;
    register int a0 __asm__("$4") = 1;
    register char *a1 __asm__("$5") = buf;
    register int a2 __asm__("$6") = count;
    __asm__ volatile("syscall" : "+r"(v0) : "r"(a0), "r"(a1), "r"(a2) : "memory", "$7");
    return v0;
}

static void zero_bytes(void *dst, int n)
{
    u8 *p = (u8 *)dst;
    while (n-- > 0) {
        *p++ = 0;
    }
}

static void run(s32 *v, s32 *o)
{
    struct Vec3s32 center, conePos;
    struct Arg3 src;
    struct Arg3 *srcp;
    int i;

    zero_bytes(gObjs, sizeof(gObjs));
    zero_bytes(gListStore, sizeof(gListStore));
    zero_bytes(gCalls, sizeof(gCalls));
    zero_bytes(gMsg, sizeof(gMsg));
    zero_bytes(gUnk0, sizeof(gUnk0));
    zero_bytes(gUnkE, sizeof(gUnkE));
    for (i = 0; i < MAXOBJ; i++) {
        gPosOk[i] = 1;
        gArg3Ok[i] = 1;
    }

    center.x = v[0];
    center.y = v[1];
    center.z = v[2];
    gCenter = &center;
    src.unk14 = (u16)v[5];
    srcp = v[6] ? &src : 0;
    gSource = srcp;

    for (i = 0; i < v[14]; i++) {
        s32 *w = &v[15 + 6 * i];
        gObjs[i].unk04 = handler;
        gObjs[i].unk08 = w[0];
        gObjs[i].unk24 = w[1];
        gObjs[i].unk50 = w[2];
        gObjs[i].unk58 = w[3];
        gObjs[i].unk60 = w[4];
        gObjs[i].unk68 = w[5];
        gListStore[2 + i] = &gObjs[i];
    }
    ((s32 *)gListStore)[0] = v[14];

    D_8009F324 = v[7];
    D_8009F328 = (u8)v[8];
    D_8009F329 = (u8)v[9];
    D_8009F710 = (s16)v[10];
    D_8009F718 = (s16)v[11];
    D_8009F71C = (s16)v[12];
    D_8009F700 = -1;
    D_8009F704 = -1;

    switch (v[13]) {
    case 1:
        gApplyBandedRadialDamage((s32)&center, v[3], v[4], v[7], (s32)srcp, v[9]);
        break;
    case 2:
        conePos.x = v[11] << 4;
        conePos.y = 0;
        conePos.z = v[12] << 4;
        gApplyConeContactDamage((s32)&center, v[3], v[4], v[7], &conePos, v[10] << 4, (s32)srcp);
        break;
    default:
        gApplyRadialContactDamage(&center, v[3], v[4], srcp);
        break;
    }

    o[0] = D_8009F700;
    o[1] = D_8009F704;
    for (i = 0; i < MAXOBJ; i++) {
        o[2 + 4 * i + 0] = gCalls[i];
        o[2 + 4 * i + 1] = gMsg[i];
        o[2 + 4 * i + 2] = gUnk0[i];
        o[2 + 4 * i + 3] = gUnkE[i];
    }
    o[18] = gPosOk[0] & gPosOk[1] & gPosOk[2] & gPosOk[3];
    o[19] = gArg3Ok[0] & gArg3Ok[1] & gArg3Ok[2] & gArg3Ok[3];
    o[20] = D_8009F324;
    o[21] = D_8009F328;
    o[22] = D_8009F329;
}

#ifdef EMIT
static void emit_line(char *name, int namelen, s32 *o)
{
    char buf[512];
    int len = 0, i;
    s32 x;
    int sign;
    char digits[12];
    int nd;

    for (i = 0; i < namelen; i++) {
        buf[len++] = name[i];
    }
    buf[len++] = ' ';
    buf[len++] = '-';
    buf[len++] = '>';
    for (i = 0; i < NOUT; i++) {
        buf[len++] = ' ';
        x = o[i];
        sign = 0;
        if (x < 0) {
            sign = 1;
            x = -x;
        }
        nd = 0;
        do {
            digits[nd++] = (char)('0' + (int)(x % 10));
            x /= 10;
        } while (x != 0);
        if (sign) {
            buf[len++] = '-';
        }
        while (nd > 0) {
            buf[len++] = digits[--nd];
        }
    }
    buf[len++] = '\n';
    write0(buf, len);
}
#endif

void __main(void) {}

int main(void)
{
    char text[65536], *p, *name;
    int len = 0, n, i, namelen, count = 0;
    s32 v[NIN], o[NOUT];

    while (len < (int)sizeof(text) - 1) {
        n = read0(text + len, sizeof(text) - 1 - len);
        if (n < 0) {
            return 2;
        }
        if (n == 0) {
            break;
        }
        len += n;
    }
    if (len == (int)sizeof(text) - 1) {
        return 3;
    }
    text[len] = 0;
    p = text;

    while (*p) {
        while (*p == ' ' || *p == '\n' || *p == '\r' || *p == '\t') {
            p++;
        }
        if (*p == '#') {
            while (*p && *p != '\n') {
                p++;
            }
            continue;
        }
        if (!*p) {
            break;
        }
        name = p;
        while (*p && *p != ' ' && *p != '\t' && *p != '\n' && *p != '\r') {
            p++;
        }
        namelen = (int)(p - name);
        for (i = 0; i < NIN; i++) {
            int sign = 1;
            while (*p == ' ' || *p == '\t') {
                p++;
            }
#ifdef EMIT
            if (p[0] == '-' && p[1] == '>') {
                break;
            }
#endif
            if (*p == '-') {
                sign = -1;
                p++;
            }
            if (*p < '0' || *p > '9') {
                return 5;
            }
            v[i] = 0;
            while (*p >= '0' && *p <= '9') {
                v[i] = v[i] * 10 + *p - '0';
                p++;
            }
            v[i] *= sign;
        }
#ifndef EMIT
        if (i != NIN) {
            return 5;
        }
        while (*p == ' ' || *p == '\t') {
            p++;
        }
        if (p[0] != '-' || p[1] != '>') {
            return 4;
        }
        p += 2;
        for (i = 0; i < NOUT; i++) {
            int sign = 1;
            while (*p == ' ' || *p == '\t') {
                p++;
            }
            if (*p == '-') {
                sign = -1;
                p++;
            }
            if (*p < '0' || *p > '9') {
                return 5;
            }
            o[i] = 0;
            while (*p >= '0' && *p <= '9') {
                o[i] = o[i] * 10 + *p - '0';
                p++;
            }
            o[i] *= sign;
        }
#endif
        while (*p == ' ' || *p == '\t' || *p == '\r') {
            p++;
        }
        if (*p != '\n' && *p != 0) {
            return 6;
        }

#ifdef EMIT
        {
            s32 out[NOUT];
            run(v, out);
            emit_line(name, namelen, out);
        }
#else
        {
            s32 out[NOUT];
            run(v, out);
            for (i = 0; i < NOUT; i++) {
                if (out[i] != o[i]) {
                    return 10 + i;
                }
            }
            count++;
        }
#endif
    }
#ifdef EMIT
    return 0;
#else
    return count == EXPECTED ? 0 : 7;
#endif
}
