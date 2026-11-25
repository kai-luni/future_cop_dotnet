/* Executes the unchanged retirement chain of a type-0x24 outpost against shared golden vectors:
 * gCullActiveEntitiesSolo (8004DF28), gProcessDeferredEntityUpdates (8004CE04), the pair pass
 * gProcessCellCollisionPairs / gTestEntityPairCollision (800623FC / 800620D8), the handlers
 * gHandleLinkedPropEntity (8002D664), gHandleType11EntityMessages (8002DEA8) and
 * gHandleDirectShotProjectile (80036624), the base handler chain 8002AD58 / 8002AA1C / 8002A940 /
 * 8003BA50 / 8003B65C / 8002A810 / 8004D958 / 8004D664, gAppendEntityToList (8004D3D4),
 * gSweepRetiredEntities (8004D598) and gRetireAndFreeAllEntities (8004CCDC).
 *
 * Per frame the harness runs the stages of the solo frame in retail order (800271A4.c:178, 182,
 * 191, 212). A hit frame appends a new projectile before the cull - it was fired on an earlier
 * frame - and hands every live D_800A8F90 entry (id > 0, flag 4 clear: 80062C3C.c step 1 and 5) to
 * the unchanged pair pass as one cell list.
 *
 * Stand-ins (not retail): func_80061CAC (gTestPartPairHit) reports a hit, which is the port's
 * reading of the box overlap of 800620D8.c:94 as the hit (the per-part element test is not ported);
 * the projectile carries 0x200 in its flags, the port's assumption that a projectile pair passes
 * 800620D8.c:75. Every other service below only records its call. */
#include "game/types.h"

#define U8(e, o) (*(u8 *)((u8 *)(e) + (o)))
#define S16(e, o) (*(s16 *)((u8 *)(e) + (o)))
#define U16(e, o) (*(u16 *)((u8 *)(e) + (o)))
#define S32(e, o) (*(s32 *)((u8 *)(e) + (o)))
#define PTR(e, o) (*(void **)((u8 *)(e) + (o)))

#define BLOCK 0x120
#define MAXENT 48

/* Placed by run.sh at (retail address - 0x70000000). */
extern s32 gPlayerCount __asm__("D_8009FA0C");
extern void *gSinglePlayerEntity __asm__("D_8009F9EC");
extern void *gGameSession __asm__("D_8009FA94");
extern void *gPlayerEntities[2] __asm__("D_8009FA58");
extern void *gUnitListHead __asm__("D_8009FA40");
extern void *gListTailBranch __asm__("D_8009FA88");
extern s32 gEntityIdCounter __asm__("D_8009F7A0");
extern s32 gProximityScriptDeadline __asm__("D_8009F7A4");
extern s32 gFrameTime[] __asm__("D_8009FAA4");
extern void *gFocusPositions[] __asm__("D_8009F9A0");
extern s32 gCommandList[] __asm__("D_800A8F90");
extern u8 *gSlotSpawnDescs[] __asm__("D_800A0C70");

s32 gHandleLinkedPropEntity(s32, void *, void *, s32) __asm__("func_8002D664");
s32 gHandleType11EntityMessages(s32, void *, void *, s32) __asm__("func_8002DEA8");
s32 gHandleDirectShotProjectile(s32, void *, void *, s32) __asm__("func_80036624");
void gCullActiveEntitiesSolo(void) __asm__("func_8004DF28");
void gProcessDeferredEntityUpdates(void) __asm__("func_8004CE04");
void gProcessCellCollisionPairs(void *) __asm__("func_800623FC");
void gSweepRetiredEntities(void) __asm__("func_8004D598");
void gRetireAndFreeAllEntities(void) __asm__("func_8004CCDC");
void gAppendEntityToList(void *) __asm__("func_8004D3D4");

/* ---- recorded services ---- */
static s32 g_footprints, g_footprint_owners, g_foot_mask, g_refreshes, g_unit_retired;
static s32 g_dmg_scripts, g_last_dead, g_ret_scripts, g_last_ret_arg;
static u8 g_freed[MAXENT];
static u8 g_blocks[MAXENT][BLOCK];

void gSpawnEntitySlotTypes(s32 slot, void *e, void *record, s32 mask, s32 mode) __asm__("func_8003B898");
void gSpawnEntitySlotTypes(s32 slot, void *e, void *record, s32 mask, s32 mode)
{
    (void)slot; (void)e; (void)mode;
    g_footprints++;
    if (record != 0) g_footprint_owners++;
    g_foot_mask = mask;
}
void gRefreshEntitySlotSpawns(void *e, s32 mask) __asm__("func_8003BAE4");
void gRefreshEntitySlotSpawns(void *e, s32 mask) { (void)e; (void)mask; g_refreshes++; }
void gRecordSpawnedUnitRetirement(void *e) __asm__("func_80061020");
void gRecordSpawnedUnitRetirement(void *e) { (void)e; g_unit_retired++; }
s32 gInterpretEntityScript(s32 script, s32 arg1, s32 arg2, void *e) __asm__("func_80069D68");
s32 gInterpretEntityScript(s32 script, s32 arg1, s32 arg2, void *e)
{
    (void)arg2; (void)e;
    if (script == 0x1111) { g_dmg_scripts++; g_last_dead = arg1; }
    if (script == 0x2222) { g_ret_scripts++; g_last_ret_arg = arg1; }
    return 0;
}
void gFreeBlock(void *block) __asm__("func_800181D4");
void gFreeBlock(void *block)
{
    s32 i;
    for (i = 0; i < MAXENT; i++)
        if ((void *)g_blocks[i] == block) g_freed[i] = 1;
}
s32 gTestPartPairHit(void *a, void *b) __asm__("func_80061CAC");
s32 gTestPartPairHit(void *a, void *b) { (void)a; (void)b; return 0x10001; }

/* ---- inert services outside the claim ---- */
void func_800175F8(void) {}
s32 func_80019FCC(void) { return 0; }
void func_8002A5F4(void) {}
s32 func_8002AB8C(void) { return 0; }
s32 func_8002ACB4(void) { return 0; }
void func_8002C584(void) {}
s32 func_8002CC30(void) { return 0; }
void func_8002D1A0(void) {}
s32 func_8002DC94(void) { return 0; }
void func_80031690(void) {}
void func_80035904(void) {}
void func_80035B7C(void) {}
void func_800410BC(void) {}
void func_80044B34(void) {}
s32 func_80045678(void) { return -1; }
void func_8004572C(void) {}
void func_800457B8(void) {}
void func_8004DEE0(void) {}
void func_80063194(void) {}
s32 func_80095900(void) { return 0; }
s32 func_80095C30(void) { return 0; }

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
#define NOUT 26

struct Node { void *object; struct Node *next; };

int main(int argc, char **argv)
{
    static char text[16384];
    static const char *outNames[NOUT] = {
        "outHealth", "outAlive", "outFreed", "outFlags", "out8C",
        "childHealth", "childFlags", "childAlive", "childFreed",
        "score0", "score1", "total0", "total1", "p0Count10", "stCount38", "stTimer18", "e0",
        "footprints", "footprintOwners", "footMask", "refreshes", "unitRetired",
        "dmgScripts", "lastDead", "retScripts", "lastRetArg",
    };
    static u8 p0[0xC4], ac0[0xC4], st[0x138], inner[0x40], menu[0xE4];
    static u8 *slotDesc[1];
    static struct Node nodes[MAXENT];
    char *p, *name;
    int len = 0, n, i, f, count = 0, failures = 0, print = argc > 1;
    s32 v[NIN + NOUT], got[NOUT];
    u8 *outpost, *child;
    s32 used;

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
        for (i = 0; i < NIN + (print ? 0 : NOUT); i++) {
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
        if (print) { while (*p && *p != '\n') p++; }
        while (*p == ' ' || *p == '\t' || *p == '\r') p++;
        if (*p != '\n' && *p != 0) return 6;

        zero_bytes(g_blocks, sizeof(g_blocks));
        zero_bytes(g_freed, sizeof(g_freed));
        zero_bytes(p0, sizeof(p0)); zero_bytes(ac0, sizeof(ac0));
        zero_bytes(st, sizeof(st)); zero_bytes(inner, sizeof(inner)); zero_bytes(menu, sizeof(menu));
        for (i = 0; i < 256; i++) gSlotSpawnDescs[i] = 0;
        g_footprints = g_footprint_owners = g_foot_mask = g_refreshes = g_unit_retired = 0;
        g_dmg_scripts = g_last_dead = g_ret_scripts = g_last_ret_arg = 0;

        /* 1P Precinct Assault: player slot 0 on team 1 (score index 0), the drone (id 287) on team 2. */
        gPlayerCount = 1;
        U8(p0, 0x18) = 1;
        PTR(p0, 0xAC) = ac0;
        gPlayerEntities[0] = p0;
        gPlayerEntities[1] = 0;
        U16(st, 0x14) = 287;
        U8(st, 0x18) = 2;
        PTR(st, 0x134) = inner;
        U16(inner, 0x18) = 500;
        gSinglePlayerEntity = st;
        S32(menu, 0xE0) = 4;
        gGameSession = menu;
        gFocusPositions[0] = 0;
        gFrameTime[0] = 0;
        gProximityScriptDeadline = 0x7FFFFFFF;
        gCommandList[0] = 0;
        gCommandList[1] = 64;                                   /* D_800A8F94: the cull's cap */
        if (v[7]) gSlotSpawnDescs[v[6] & 0xFF] = (u8 *)slotDesc;

        /* Outpost id 1 then its child id 2, linked child first (8004D444.c, 8002C584.c:297). */
        outpost = g_blocks[0];
        child = g_blocks[1];
        used = 2;
        PTR(outpost, 0x04) = (void *)gHandleLinkedPropEntity;
        S32(outpost, 0x08) = 1;
        U16(outpost, 0x14) = 113;
        U8(outpost, 0x16) = 0x24;
        U8(outpost, 0x19) = 0x15;
        S16(outpost, 0x1C) = (s16)v[0];
        S16(outpost, 0x1E) = (s16)v[0];
        U8(outpost, 0x20) = (u8)v[6];
        S32(outpost, 0x24) = (0x00910149 | v[5]) & (v[4] ? ~1 : ~0);  /* 8002D1A0.c:212 on a claim */
        S32(outpost, 0x50) = v[9] ? 0 : 0x100000;
        S32(outpost, 0x58) = v[9] ? 0 : 0x100000;
        S32(outpost, 0x68) = 463 << 4;
        S16(outpost, 0x6C) = 152;
        S16(outpost, 0x6E) = 437;
        U16(outpost, 0x90) = 0x7F00;
        S32(outpost, 0x94) = (v[12] & 1) ? 0x1111 : 0;
        S32(outpost, 0x98) = (v[12] & 2) ? 0x2222 : 0;
        U8(outpost, 0xF6) = 1;
        U8(outpost, 0xF7) = 2;
        U8(outpost, 0xFE) = (u8)v[4];
        PTR(outpost, 0x104) = child;

        PTR(child, 0x04) = (void *)gHandleType11EntityMessages;
        S32(child, 0x08) = 2;
        U16(child, 0x14) = 113;
        U8(child, 0x16) = 0x24;
        U8(child, 0x19) = 0x15;
        S16(child, 0x1C) = (s16)v[0];
        S16(child, 0x1E) = (s16)v[0];
        U8(child, 0x20) = (u8)v[6];
        S32(child, 0x24) = 0x00A10051;
        S32(child, 0x50) = S32(outpost, 0x50);
        S32(child, 0x58) = S32(outpost, 0x58);
        S32(child, 0x68) = 401 << 4;
        S16(child, 0x6C) = 255;
        S16(child, 0x6E) = 309;
        U16(child, 0x90) = 0x7F00;
        PTR(child, 0xB4) = outpost;

        PTR(child, 0x00) = outpost;
        PTR(outpost, 0x00) = 0;
        gUnitListHead = child;
        gListTailBranch = outpost;
        gEntityIdCounter = 2;
        if (v[13]) S32(outpost, 0x24) |= 0x10000000;             /* 8004D4F8.c:59 */

        for (f = 0; f < v[3]; f++) {
            if (f < v[2] && S32(outpost, 0x08) != 0 && used < MAXENT) {
                u8 *shot = g_blocks[used++];
                PTR(shot, 0x04) = (void *)gHandleDirectShotProjectile;
                S32(shot, 0x08) = ++gEntityIdCounter;
                U16(shot, 0x14) = 12 | 0x8000;                    /* 8005E47C.c:73 */
                U8(shot, 0x16) = 2;
                U8(shot, 0x18) = 1;
                S16(shot, 0x1A) = (s16)v[1];                      /* 8005E47C.c:101 */
                S32(shot, 0x24) = 0x0080020A;                     /* 8005E47C.c:85, 80035904.c:239, + 0x200 */
                S32(shot, 0x5C) = v[14];
                S32(shot, 0x60) = v[10];
                S32(shot, 0x68) = 94 << 4;
                S16(shot, 0x6C) = 15;
                S16(shot, 0x6E) = 92;
                U16(shot, 0x90) = 0x7F00;
                gAppendEntityToList(shot);
            }
            gCullActiveEntitiesSolo();                            /* 800271A4.c:178 */
            gProcessDeferredEntityUpdates();                      /* :182 */
            if (f < v[2]) {                                       /* :191 */
                s32 k = 0;
                for (i = 0; i < gCommandList[0]; i++) {
                    void *e = ((void **)&gCommandList[2])[i];
                    if (S32(e, 0x08) <= 0 || (S32(e, 0x24) & 4)) continue;
                    if (e == (void *)child && !v[8]) continue;
                    nodes[k].object = e;
                    nodes[k].next = 0;
                    if (k > 0) nodes[k - 1].next = &nodes[k];
                    k++;
                }
                if (k > 0) gProcessCellCollisionPairs(&nodes[0]);
            }
            gSweepRetiredEntities();                              /* :212 */
        }
        if (v[11]) gRetireAndFreeAllEntities();

        got[0] = S16(outpost, 0x1C);
        got[1] = S32(outpost, 0x08) != 0;
        got[2] = g_freed[0];
        got[3] = S32(outpost, 0x24);
        got[4] = S32(outpost, 0x8C);
        got[5] = S16(child, 0x1C);
        got[6] = S32(child, 0x24);
        got[7] = S32(child, 0x08) != 0;
        got[8] = g_freed[1];
        got[9] = S32(menu, 0x64);
        got[10] = S32(menu, 0x68);
        got[11] = S32(menu, 0x7C);
        got[12] = S32(menu, 0x80);
        got[13] = U16(ac0, 0x10);
        got[14] = U16(inner, 0x38);
        got[15] = U16(inner, 0x18);
        got[16] = S32(menu, 0xE0);
        got[17] = g_footprints;
        got[18] = g_footprint_owners;
        got[19] = g_foot_mask;
        got[20] = g_refreshes;
        got[21] = g_unit_retired;
        got[22] = g_dmg_scripts;
        got[23] = g_last_dead;
        got[24] = g_ret_scripts;
        got[25] = g_last_ret_arg;
        if (print) {
            say(name);
            for (i = 0; i < NIN; i++) { say(" "); say_dec(v[i]); }
            say(" ->");
            for (i = 0; i < NOUT; i++) { say(" "); say_dec(got[i]); }
            say("\n");
        } else {
            for (i = 0; i < NOUT; i++) {
                if (got[i] != v[NIN + i]) {
                    say("outpost-retirement: FAIL "); say(name); say(" "); say(outNames[i]);
                    say(" got "); say_dec(got[i]); say(" want "); say_dec(v[NIN + i]); say("\n");
                    failures++;
                }
            }
        }
        count++;
    }
    say("outpost-retirement: cases="); say_dec(count); say(" failures="); say_dec(failures); say("\n");
    return failures ? 1 : (count == 0 ? 7 : 0);
}
