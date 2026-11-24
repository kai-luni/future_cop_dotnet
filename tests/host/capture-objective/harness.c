/* Executes unchanged func_8002D1A0 (gTryCaptureObjective) with shared golden vectors. */
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

void gInterpretEntityScript(u8 *script, s32 a1, s32 a2) __asm__("func_80069D68");
void gInterpretEntityScript(u8 *script, s32 a1, s32 a2) { (void)script; (void)a1; (void)a2; }

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
    char text[8192], *p;
    int len = 0, n, i, count = 0;
    s32 v[25];
    struct Ent outpost, child, other, p0, p1, sp;
    struct Sub sub0, sub1;
    struct Trk trk;
    struct Menu menu;

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
        /* skip vector name token */
        while (*p && *p != ' ' && *p != '\t' && *p != '\n' && *p != '\r') p++;
        for (i = 0; i < 25; i++) {
            int sign = 1;
            while (*p == ' ' || *p == '\t') p++;
            if (i == 14) {
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
        zero_bytes(&p0, sizeof(p0));
        zero_bytes(&p1, sizeof(p1));
        zero_bytes(&sp, sizeof(sp));
        zero_bytes(&sub0, sizeof(sub0));
        zero_bytes(&sub1, sizeof(sub1));
        zero_bytes(&trk, sizeof(trk));
        zero_bytes(&menu, sizeof(menu));

        outpost.unk14 = 112;
        outpost.unk16 = 0x24;
        outpost.unk24 = 1;
        outpost.unkF6 = 1;
        outpost.unkF7 = 2;
        outpost.unkF8 = 1;
        outpost.unkF9 = 2;
        outpost.unkFA = 60;
        outpost.unkFB = 0;
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

        gPlayerCount = v[0];
        gPlayerEntities[0] = &p0;
        gPlayerEntities[1] = v[2] >= 0 ? &p1 : 0;
        gSinglePlayerEntity = v[3] ? &sp : 0;
        gGameSession = &menu;
        menu.unkE0 = v[13];

        gTryCaptureObjective(&outpost, &other);

        if ((s32)outpost.unkFE != v[14]) return 10 + count;
        if ((s32)outpost.unk18 != v[15]) return 20 + count;
        if (menu.unk64[0] != v[16]) return 30 + count;
        if (menu.unk64[1] != v[17]) return 40 + count;
        if (menu.unk7C[0] != v[18]) return 50 + count;
        if (menu.unk7C[1] != v[19]) return 60 + count;
        if ((s32)sub0.unk12 != v[20]) return 70 + count;
        if ((s32)sub1.unk12 != v[21]) return 80 + count;
        if ((s32)trk.unk3A != v[22]) return 90 + count;
        if ((s32)trk.unk18 != v[23]) return 100 + count;
        if (menu.unkE0 != v[24]) return 110 + count;
        count++;
    }
    return count == 6 ? 0 : 7;
}
