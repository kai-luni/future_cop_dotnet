/* Executes unchanged PS1 C map-occupancy units (func_8003F79C, func_8003FA34, func_8003B838,
 * func_8003B7D8, func_8003BC0C, func_8003BF50, func_8003FAD4, func_8003BC7C, func_8003C404,
 * func_8003C590, func_8003C500, func_8003C4C4, func_8003B3D8) against shared golden vectors. */
#include "game/types.h"

struct MapObj {
    u8 pad00[0x14];
    u16 id;
    u8 kind;
    u8 pad17[5];
    s16 unk1C;
    s16 unk1E;
    u8 tableIndex;
    u8 pad21[3];
    u32 flags;
    u8 pad28[0x98];
    struct MapObj *linkC0;
    u8 padC4[4];
    struct MapObj *linkC8;
};

struct PoolNode {
    struct PoolNode *next;
    void *object;
};

struct FixedPool {
    s32 remaining;
    struct PoolNode nodes[0x100];
};

extern void gInitMissionSpawnState(void) __asm__("func_8003F79C");
extern void gQueueOrApplyMapOccupancy(void *object) __asm__("func_8003FA34");
extern u8 gGetSpawnDescColumn(void *object) __asm__("func_8003B838");
extern u8 gGetDestructionSpawnColumn(void *object) __asm__("func_8003B7D8");
extern void gSpawnEntityPrimaryType(void *object) __asm__("func_8003BC0C");
extern s32 gGetEntitySpawnBank(void *object) __asm__("func_8003BF50");
extern void gFinishLevelLoad(void) __asm__("func_8003FAD4");
extern void gSetSlotSpawnDescriptor(u8 *data) __asm__("func_8003BC7C");
extern void gInstallSpawnRecord(void *data) __asm__("func_8003C404");
extern u32 *gGetSpawnTypeFlagPtr(u32 column, u32 row) __asm__("func_8003B3D8");

extern s32 gScreenState[] __asm__("D_8009FA00");
extern void *gDeferredRegPool __asm__("D_8009F70C");
extern struct PoolNode *gDeferredRegList __asm__("D_8009F708");
extern u32 gSpawnTableRow __asm__("D_8009F310");

/* ---- engine stubs ---- */
static struct FixedPool g_pool;
static u8 g_heap[4096];
static s32 g_heap_pos;

static int g_spawn_count;
static int g_spawn_ids[8];
static int g_spawn_cols[8];
static int g_spawn_banks[8];

void gInitTracerPool(void) __asm__("func_8006EDC4");
void gInitTracerPool(void) {}
void gInitEffectPools(void) __asm__("func_8005E1BC");
void gInitEffectPools(void) {}

void *func_800194DC(s32 count, s32 size, s32 tag)
{
    (void)size; (void)tag;
    g_pool.remaining = count;
    return &g_pool;
}

void *func_800195A4(void *pool)
{
    struct FixedPool *p = (struct FixedPool *)pool;
    if (p == 0 || p->remaining <= 0) return 0;
    p->remaining--;
    return &p->nodes[p->remaining];
}

void func_80019584(s32 arg0) { (void)arg0; }

u8 *gAllocMemory(s32 size, s32 type) __asm__("func_80017EBC");
u8 *gAllocMemory(s32 size, s32 type)
{
    u8 *p;
    (void)type;
    size = (size + 3) & ~3;
    if (g_heap_pos + size > (s32)sizeof(g_heap)) return 0;
    p = g_heap + g_heap_pos;
    g_heap_pos += size;
    return p;
}

void gFreeBlock(void *block) __asm__("func_800181D4");
void gFreeBlock(void *block) { (void)block; }

void *memcpy(void *dst, const void *src, u32 n)
{
    u8 *d = (u8 *)dst;
    const u8 *s = (const u8 *)src;
    while (n-- > 0) *d++ = *s++;
    return dst;
}

void gLoadImage(s16 *rect, u32 *color) __asm__("func_80092F38");
void gLoadImage(s16 *rect, u32 *color) { (void)rect; (void)color; }
void gDrawSync(s32 arg0) __asm__("func_80092C8C");
void gDrawSync(s32 arg0) { (void)arg0; }

void gSpawnTypeAtOwner(void *object, u8 kind, s32 arg2, s32 row, s32 arg4) __asm__("func_800402DC");
void gSpawnTypeAtOwner(void *object, u8 kind, s32 arg2, s32 row, s32 arg4)
{
    struct MapObj *obj = (struct MapObj *)object;
    (void)arg2; (void)arg4;
    if (g_spawn_count < 8) {
        g_spawn_ids[g_spawn_count] = obj ? obj->id : -1;
        g_spawn_cols[g_spawn_count] = kind;
        g_spawn_banks[g_spawn_count] = row;
    }
    g_spawn_count++;
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
    /* Inputs (12): screenState dropPool slot colKind destCol objKind flags linkType linkKind cellType cellNext cellUnk20
     * Outputs (9): col destCol bank immCount immCol immBank replayCount replayFirstId cellFlag4 */
    s32 v[21];

    while (len < (int)sizeof(text) - 1) {
        n = read0(text + len, sizeof(text) - 1 - len);
        if (n < 0) return 2;
        if (n == 0) break;
        len += n;
    }
    text[len] = 0;
    p = text;

    while (*p) {
        char *nameStart;
        struct MapObj obj1, obj2, linked;
        int col, destCol, bank, immCount, immCol, immBank, replayCount, replayFirstId, cellFlag4;
        s32 actual[9];
        while (*p == ' ' || *p == '\n' || *p == '\r' || *p == '\t') p++;
        if (*p == '#') { while (*p && *p != '\n') p++; continue; }
        if (!*p) break;
        nameStart = p;
        while (*p && *p != ' ' && *p != '\t' && *p != '\n' && *p != '\r') p++;
        for (i = 0; i < 21; i++) {
            int sign = 1;
            while (*p == ' ' || *p == '\t') p++;
            if (i == 12) {
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

        g_heap_pos = 0;
        g_spawn_count = 0;
        zero_bytes(g_spawn_ids, sizeof(g_spawn_ids));
        zero_bytes(g_spawn_cols, sizeof(g_spawn_cols));
        zero_bytes(g_spawn_banks, sizeof(g_spawn_banks));
        gInitMissionSpawnState();
        gDeferredRegList = 0;

        gScreenState[0] = v[0];
        if (v[1] != 0) gDeferredRegPool = 0;

        if (v[2] != 0 && (v[3] != 0 || v[4] != 0)) {
            u8 desc[0x30];
            zero_bytes(desc, sizeof(desc));
            desc[0x14] = (u8)v[2];
            desc[0x18] = (u8)v[3];
            desc[0x19] = (u8)v[4];
            gSetSlotSpawnDescriptor(desc);
        }

        if (v[9] != 0) {
            u8 *rec = gAllocMemory(0x50, 2);
            zero_bytes(rec, 0x50);
            rec[0x04] = (u8)v[9];
            rec[0x14] = 10;
            rec[0x15] = (u8)v[10];
            *(s32 *)(rec + 0x20) = v[11];
            *(s16 *)(rec + 0x28) = 0;
            *(s16 *)(rec + 0x2A) = 0;
            gSpawnTableRow = 0;
            gInstallSpawnRecord(rec);
            if (v[10] != 0) {
                u8 *rec2 = gAllocMemory(0x50, 2);
                zero_bytes(rec2, 0x50);
                rec2[0x04] = (u8)v[9];
                rec2[0x14] = (u8)v[10];
                rec2[0x15] = 10;
                *(s32 *)(rec2 + 0x20) = v[11];
                *(s16 *)(rec2 + 0x28) = 0;
                *(s16 *)(rec2 + 0x2A) = 0;
                gInstallSpawnRecord(rec2);
            }
        }

        zero_bytes(&obj1, sizeof(obj1));
        zero_bytes(&obj2, sizeof(obj2));
        zero_bytes(&linked, sizeof(linked));
        linked.kind = (u8)v[8];

        obj1.id = 101;
        obj1.kind = (u8)v[5];
        obj1.tableIndex = (u8)v[2];
        obj1.flags = (u32)v[6];
        if (v[7] == 1) obj1.linkC0 = &linked;
        if (v[7] == 2) obj1.linkC8 = &linked;

        obj2 = obj1;
        obj2.id = 202;

        col = gGetSpawnDescColumn(&obj1);
        destCol = gGetDestructionSpawnColumn(&obj1);
        bank = gGetEntitySpawnBank(&obj1);

        gQueueOrApplyMapOccupancy(&obj1);
        gQueueOrApplyMapOccupancy(&obj2);

        immCount = g_spawn_count;
        immCol = immCount > 0 ? g_spawn_cols[0] : 0;
        immBank = immCount > 0 ? g_spawn_banks[0] : 0;

        g_spawn_count = 0;
        zero_bytes(g_spawn_ids, sizeof(g_spawn_ids));
        gFinishLevelLoad();

        replayCount = g_spawn_count;
        replayFirstId = replayCount > 0 ? g_spawn_ids[0] : 0;
        cellFlag4 = v[9] != 0 ? ((*(u8 *)gGetSpawnTypeFlagPtr(10, 0) & 4) != 0 ? 1 : 0) : 0;

        actual[0] = col;
        actual[1] = destCol;
        actual[2] = bank;
        actual[3] = immCount;
        actual[4] = immCol;
        actual[5] = immBank;
        actual[6] = replayCount;
        actual[7] = replayFirstId;
        actual[8] = cellFlag4;

        cases++;
        for (i = 0; i < 9; i++) {
            if (actual[i] != v[12 + i]) {
                failures++;
                put_str("FAIL case ");
                put_num(cases);
                put_str(" col ");
                put_num(i);
                put_str(": got ");
                put_num(actual[i]);
                put_str(" want ");
                put_num(v[12 + i]);
                put_str("\n");
            }
        }
        (void)nameStart;
    }

    put_str("map-occupancy: cases=");
    put_num(cases);
    put_str(" failures=");
    put_num(failures);
    put_str("\n");
    return failures == 0 ? 0 : 1;
}
