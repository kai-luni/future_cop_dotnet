/* Executes unchanged func_8002D1A0 (gTryCaptureObjective) with shared golden vectors,
 * covering 1P/2P scoring, type-0x12 carrier dereference, child-part transfer (obj->unk104),
 * state bytes (+0xA6/+0xDE), and post-capture script dispatch (obj->unk9C & flag 0x100000). */
#include "game/types.h"

struct Coord {
    u8 pad00[0x34];
    s32 unk34;
    s32 unk38;
    s32 unk3C;
    s32 unk40;
};

struct Sub {
    u8 pad00[0x12];
    u16 unk12;
    u8 pad14[0x2C];
    s32 unk40;
    u8 pad44[0x7D];
    s8 unkC1;
};

struct Ent;

struct Trk {
    u8 pad00[0x12];
    u16 unk12;
    u8 pad14[4];
    u16 unk18;
    u8 pad1A[0x20];
    u16 unk3A;
};

struct Ent {
    u8 pad00[0x14];
    u16 unk14;
    u8 unk16;
    u8 pad17;
    u8 unk18;
    u8 pad19[0x0A];
    u8 unk23;
    u32 unk24;
    struct Coord coord;
    u8 pad6C[0x26];
    u8 unk92;
    u8 unk93;
    u8 pad94[8];
    u8 *unk9C;
    u8 pad_a0[6];
    u8 unkA6;
    u8 padA7;
    u16 unkA8;
    u8 padAA[2];
    struct Sub *unkAC;
    u8 padB0[4];
    struct Ent *unkB4;
    u8 padB8[0x24];
    u16 unkDC;
    u8 unkDE;
    u8 padDF[0x11];
    u8 unkF0[6];
    u8 unkF6;
    u8 unkF7;
    u8 unkF8;
    u8 unkF9;
    u8 unkFA;
    u8 unkFB;
    s16 unkFC;
    u8 unkFE;
    u8 padFF[5];
    struct Ent *unk104;
    u8 pad108[0x2C];
    struct Trk *unk134;
};

struct Menu {
    u8 pad00[0x64];
    s32 unk64[2];
    u8 pad6C[0x10];
    s32 unk7C[2];
    u8 pad84[0x5C];
    s32 unkE0;
};

struct Ent *gPlayerEntities[2] __asm__("D_8009FA58");
s32 gPlayerCount __asm__("D_8009FA0C");
struct Ent *gSinglePlayerEntity __asm__("D_8009F9EC");
struct Menu *gGameSession __asm__("D_8009FA94");

static int g_script_called;
static s32 g_script_arg2;

void gInterpretEntityScript(u8 *script, s32 a1, s32 a2) __asm__("func_80069D68");
void gInterpretEntityScript(u8 *script, s32 a1, s32 a2)
{
    (void)script; (void)a1;
    g_script_called++;
    g_script_arg2 = a2;
}

void gTryCaptureObjective(struct Ent *self, struct Ent *other) __asm__("func_8002D1A0");

static int read0(char *buf, int count)
{
    register int v0 __asm__("$2") = 4003;
    register int a0 __asm__("$4") = 0;
    register char *a1 __asm__("$5") = buf;
    register int a2 __asm__("$6") = count;
    __asm__ volatile("syscall" : "+r"(v0) : "r"(a0), "r"(a1), "r"(a2) : "memory", "$7");
    return v0;
}

static void zero_bytes(void *dst, int n)
{
    u8 *p = (u8 *)dst;
    while (n-- > 0) *p++ = 0;
}

void __main(void) {}

int main(void)
{
    char text[16384], *p;
    int len = 0, n, i, count = 0;
    /* Inputs (18): playerCount p0Team p1Team spActive spTeam spId otherId otherType otherTeam
     *              otherX otherY otherZ spTimerIn e0In carrierMode carriedId scriptFlag scriptByte
     * Outputs (18): mode outTeam score0 score1 total0 total1 p0Cap p1Cap spCap spTimerOut e0Out
     *               childTeam child92 child93 stateA6 stateDE scriptCalled scriptArg2 */
    s32 v[36];
    struct Ent outpost, child, other, carried, p0, p1, sp;
    struct Sub sub0, sub1;
    struct Trk trk;
    struct Menu menu;
    u8 scriptBuf[4];

    while (len < (int)sizeof(text) - 1) {
        n = read0(text + len, sizeof(text) - 1 - len);
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
        while (*p && *p != ' ' && *p != '\t' && *p != '\n' && *p != '\r') p++;
        for (i = 0; i < 36; i++) {
            int sign = 1;
            while (*p == ' ' || *p == '\t') p++;
            if (i == 18) {
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

        zero_bytes(&outpost, sizeof(outpost));
        zero_bytes(&child, sizeof(child));
        zero_bytes(&other, sizeof(other));
        zero_bytes(&carried, sizeof(carried));
        zero_bytes(&p0, sizeof(p0));
        zero_bytes(&p1, sizeof(p1));
        zero_bytes(&sp, sizeof(sp));
        zero_bytes(&sub0, sizeof(sub0));
        zero_bytes(&sub1, sizeof(sub1));
        zero_bytes(&trk, sizeof(trk));
        zero_bytes(&menu, sizeof(menu));
        g_script_called = 0;
        g_script_arg2 = -1;

        scriptBuf[0] = (u8)v[17];
        scriptBuf[1] = 0;

        outpost.unk14 = 112;
        outpost.unk16 = 0x24;
        outpost.unk24 = 1u | (v[16] ? 0x100000u : 0u);
        outpost.unk9C = v[17] >= 0 ? scriptBuf : 0;
        outpost.unkF6 = 1;
        outpost.unkF7 = 2;
        outpost.unkF8 = 1;
        outpost.unkF9 = 2;
        outpost.unkFA = 60;
        outpost.unkFB = 5;
        outpost.unkFC = 0x400;
        outpost.unk104 = &child;

        p0.unk14 = 12;
        p0.unk16 = 1;
        p0.unk18 = (u8)v[1];
        p0.unkAC = &sub0;
        sub0.unkC1 = 0;

        p1.unk14 = 16;
        p1.unk16 = 1;
        p1.unk18 = (u8)(v[2] < 0 ? 0 : v[2]);
        p1.unkAC = &sub1;
        sub1.unkC1 = 1;

        sp.unk14 = (u16)v[5];
        sp.unk16 = 0x25;
        sp.unk18 = (u8)v[4];
        sp.unk134 = &trk;
        trk.unk18 = (u16)v[12];
        trk.unk3A = 0;

        other.unk14 = (u16)v[6];
        other.unk16 = (u8)v[7];
        other.unk18 = (u8)v[8];
        other.coord.unk34 = v[9];
        other.coord.unk38 = v[10];
        other.coord.unk3C = v[11];
        other.coord.unk40 = 0;

        /* carrierMode: 0 = none, 1 = carried player at (0,0,0) with carriedId & team=otherTeam,
         * 2 = carrier with null unkB4, 3 = carried player with sub0.unk40 = 2 (busy). */
        if (v[14] == 1 || v[14] == 3) {
            carried.unk14 = (u16)v[15];
            carried.unk16 = 1;
            carried.unk18 = (u8)v[8];
            carried.coord.unk34 = 0;
            carried.coord.unk38 = 0;
            carried.coord.unk3C = 0;
            other.unkB4 = &carried;
            if (v[14] == 3) sub0.unk40 = 2;
        }

        gPlayerCount = v[0];
        gPlayerEntities[0] = &p0;
        gPlayerEntities[1] = v[2] >= 0 ? &p1 : 0;
        gSinglePlayerEntity = v[3] ? &sp : 0;
        gGameSession = &menu;
        menu.unkE0 = v[13];

        gTryCaptureObjective(&outpost, &other);

        if ((s32)outpost.unkFE != v[18]) return 10 + count;
        if ((s32)outpost.unk18 != v[19]) return 20 + count;
        if (menu.unk64[0] != v[20]) return 30 + count;
        if (menu.unk64[1] != v[21]) return 40 + count;
        if (menu.unk7C[0] != v[22]) return 50 + count;
        if (menu.unk7C[1] != v[23]) return 60 + count;
        if ((s32)sub0.unk12 != v[24]) return 70 + count;
        if ((s32)sub1.unk12 != v[25]) return 80 + count;
        if ((s32)trk.unk3A != v[26]) return 90 + count;
        if ((s32)trk.unk18 != v[27]) return 100 + count;
        if (menu.unkE0 != v[28]) return 110 + count;
        if ((s32)child.unk18 != v[29]) return 120 + count;
        if ((s32)child.unk92 != v[30]) return 130 + count;
        if ((s32)child.unk93 != v[31]) return 140 + count;
        if ((s32)outpost.unkA6 != v[32]) return 150 + count;
        if ((s32)outpost.unkDE != v[33]) return 160 + count;
        if (g_script_called != v[34]) return 170 + count;
        if (g_script_arg2 != v[35]) return 180 + count;
        count++;
    }
    return count == 12 ? 0 : 7;
}
