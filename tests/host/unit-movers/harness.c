/* Host harness: unchanged PS1 retail C unit movers under qemu-mipsel.
 *
 * Links the unmodified retail units from future-cop-lapd-ps1/src/game:
 *   Type 9 flyer:   80032180 (gInitHoverEnemy), 800324E0 (gUpdateHoverEnemy),
 *                   800320E4 (gPickHoverWanderTarget)
 *   Type 5/28 rail: 80050DAC (gFindRailPathCell), 80050E0C (gInitRailSegment),
 *                   8004F330 (gSelectNextPathNode), 8004F764 (gUpdatePathMoverSegment),
 *                   8004FFD0 (gApplyPathCurveOffsets), 8005027C (gStepPathMover),
 *                   80050D14 (gUpdatePathMoverMotion), 8003802C (gUpdateFallMotion)
 *   Math / PRNG:    8001A750 8001A644 80019F54 8001AB78 8001A6F8 8001ACA4
 *                   80019310 800193F8 8001A074 8001A100 8001A180 80019828
 *                   80018818 800187C8 8001846C 80095C30 80019668 80019720 800197A8
 *
 * Software stand-ins ONLY for COP2 instructions (GTE OP 80010974, LZCS 80095C14,
 * and 80019904 reading D_8009F5B0 initialized by real 80019828) and unused side
 * effects (GTE matrix upload/transform, anim track, entity base handler, target search).
 */
#include "game/types.h"

extern u8 D_RAM[];
extern u16 *D_8009F5B0;
static u16 g_recip_buf[0x400];
void *func_80017EBC(s32 size, s32 tag) { (void)size; (void)tag; return g_recip_buf; }

/* Real retail units under test */
extern void func_80019828(void);
extern void func_800197A8(s32 seed);
extern s32 func_80032180(s32 msg, void *obj, void *arg);
extern s32 func_800324E0(s32 unused, void *obj, s32 dt);
extern void func_80050E0C(void *arg0, void *out, void *arg2, void *arg3, s32 *arg4);
extern void func_8003802C(s32 arg0, void *arg1, s32 arg2);

void __main(void) {}

/* ---- Linux MIPS o32 syscalls (stdout / open / read / close / exit) ---- */
static long sys3(long n, long a, long b, long c)
{
    register long v0 __asm__("$2") = n;
    register long a0 __asm__("$4") = a;
    register long a1 __asm__("$5") = b;
    register long a2 __asm__("$6") = c;
    register long a3 __asm__("$7") = 0;
    __asm__ volatile("syscall" : "+r"(v0), "+r"(a3) : "r"(a0), "r"(a1), "r"(a2)
                     : "memory", "$8", "$9", "$10", "$11", "$12", "$13", "$14", "$15", "$24", "$25");
    return a3 ? -v0 : v0;
}

static void put_str(const char *s)
{
    int n = 0;
    while (s[n]) n++;
    sys3(4004, 1, (long)s, n);
}

static void put_int(s32 v)
{
    char buf[16];
    int i = 0;
    u32 u;
    if (v < 0) {
        put_str("-");
        u = (u32)(-(v + 1)) + 1u;
    } else {
        u = (u32)v;
    }
    if (u == 0) {
        put_str("0");
        return;
    }
    while (u > 0) {
        buf[i++] = (char)('0' + (u % 10));
        u /= 10;
    }
    while (i > 0) {
        char c = buf[--i];
        sys3(4004, 1, (long)&c, 1);
    }
}

static void put_sp_int(s32 v)
{
    put_str(" ");
    put_int(v);
}

/* ---- COP2 software equivalents (matching MechMath.cs) ---- */
s32 func_80095C14(u32 value)
{
    u32 x = ((s32)value < 0) ? ~value : value;
    s32 n = 0;
    while (n < 32 && (x & 0x80000000u) == 0) {
        x <<= 1;
        n++;
    }
    return n;
}

s32 func_80019904(s32 value)
{
    s32 lzc = func_80095C14((u32)value);
    s32 shift = 0x16 - (lzc & ~1);
    s32 index, entry, mantissa;
    if (shift <= 0) shift = 0;
    index = value >> (shift & 31);
    entry = D_8009F5B0[index];
    mantissa = (entry >> 5) << (entry & 0x1F);
    return mantissa >> ((shift >> 1) & 31);
}

void func_80010974(s32 *a, s32 *b, s32 *out)
{
    s32 d1 = (s16)(a[0] >> 4), d2 = (s16)(a[1] >> 4), d3 = (s16)(a[2] >> 4);
    s32 i1 = (s16)(b[0] >> 4), i2 = (s16)(b[1] >> 4), i3 = (s16)(b[2] >> 4);
    s32 r0 = ((d2 * i3 - d3 * i2) >> 12) << 4;
    s32 r1 = ((d3 * i1 - d1 * i3) >> 12) << 4;
    s32 r2 = ((d1 * i2 - d2 * i1) >> 12) << 4;
    out[0] = r0;
    out[1] = r1;
    out[2] = r2;
}

/* ---- Environment stubs for non-motion side effects ---- */
static s32 g_ground_height;
static u8 g_dummy_model[0x40];
static u8 g_dummy_frames[0x40];

s32 func_80066F78(s32 *pos, s32 a1, s32 a2, s32 a3)
{
    (void)pos; (void)a1; (void)a2; (void)a3;
    return g_ground_height;
}

s32 func_80069A38(s32 *pos)
{
    (void)pos;
    return g_ground_height;
}

s32 func_8002AD58(s32 msg, u8 *obj, void *arg, s32 extra)
{
    (void)msg; (void)extra;
    if (msg == 2 && arg != 0) {
        u8 *src = *(u8 **)((u8 *)arg + 8);
        obj[0x16] = src[0x04];
        *(s32 *)(obj + 0x50) = *(s32 *)(src + 0x08);
        *(s32 *)(obj + 0x54) = *(s32 *)(src + 0x0C);
        *(s32 *)(obj + 0x58) = *(s32 *)(src + 0x10);
        *(u32 *)(obj + 0x24) = *(u32 *)(src + 0x14);
        *(s16 *)(obj + 0x1C) = *(s16 *)(src + 0x18);
        *(u16 *)(obj + 0x1E) = *(u16 *)(src + 0x18);
        obj[0x20] = src[0x20];
        *(void **)(obj + 0x28) = g_dummy_model;
    }
    return 0;
}

void func_80060668(void *a, void *b) { (void)a; (void)b; }
void func_80044988(void *a, void *b, u8 c, s32 d) { (void)a; (void)b; (void)c; (void)d; }
void func_80044B34(void *a, void *b, s32 c, s32 d) { (void)a; (void)b; (void)c; (void)d; }
void func_800175F8(void *a, s32 b) { (void)a; (void)b; }
void *func_8004E80C(void *a, s32 b, s32 c, s32 d, s32 e, s32 f, s32 g)
{ (void)a; (void)b; (void)c; (void)d; (void)e; (void)f; (void)g; return 0; }
void func_8004E5F0(void *a, void *b, s32 c, s32 d) { (void)a; (void)b; (void)c; (void)d; }
void func_8003BA50(void *a, s32 b) { (void)a; (void)b; }
void func_8003B898(s32 a, void *b, s32 c, s32 d, s32 e) { (void)a; (void)b; (void)c; (void)d; (void)e; }
void *func_8004D664(s32 a) { (void)a; return 0; }
void func_80069D68(s32 a, s32 b, s32 c, void *d) { (void)a; (void)b; (void)c; (void)d; }
void func_80060238(void *a, s32 b, void *c, s32 d, s32 e) { (void)a; (void)b; (void)c; (void)d; (void)e; }
void func_80010750(void *m) { (void)m; }
void func_80010234(void *in, void *out, s32 n) { (void)in; (void)out; (void)n; }
void func_800188D0(s32 y, s32 p, s32 r, void *out) { (void)y; (void)p; (void)r; (void)out; }
void func_80037EB4(void *o) { (void)o; }
void func_8004D958(void *o) { (void)o; }
void func_8004F188(void *a, void *b) { (void)a; (void)b; }
u8 func_80050B54(void *a, s32 b, void *c) { (void)a; (void)b; (void)c; return 0; }

/* ---- helpers ---- */
static void zero_mem(u8 *p, int n) { int i; for (i = 0; i < n; i++) p[i] = 0; }
static void wr16(u8 *p, int off, u16 v) { p[off] = (u8)v; p[off + 1] = (u8)(v >> 8); }
static void wr32(u8 *p, int off, u32 v) {
    p[off] = (u8)v; p[off + 1] = (u8)(v >> 8); p[off + 2] = (u8)(v >> 16); p[off + 3] = (u8)(v >> 24);
}
static s32 rd32(u8 *p, int off) {
    return (s32)(p[off] | ((u32)p[off + 1] << 8) | ((u32)p[off + 2] << 16) | ((u32)p[off + 3] << 24));
}
static s16 rds16(u8 *p, int off) { return (s16)(p[off] | (p[off + 1] << 8)); }

static void load_slus(const char *path)
{
    long fd = sys3(4005, (long)path, 0, 0); /* O_RDONLY */
    if (fd < 0) sys3(4001, 2, 0, 0);
    /* Skip 0x800 PS-X EXE header, copy body to D_RAM (0x80010000 -> 0x10010000) */
    {
        u8 hdr[0x800];
        long n, total = 0;
        while (total < 0x800) {
            n = sys3(4003, fd, (long)(hdr + total), 0x800 - total);
            if (n <= 0) break;
            total += n;
        }
        total = 0;
        while (total < 0x90000) {
            n = sys3(4003, fd, (long)(D_RAM + total), 0x90000 - total);
            if (n <= 0) break;
            total += n;
        }
    }
    sys3(4006, fd, 0, 0);
}

static void build_flyer_src(u8 *src, s32 x, s32 z, s16 spdRaw, u16 turnRate,
                            u16 f110, u16 f114, u16 f112, u8 state, u8 fb,
                            s16 spreadZ, s16 spreadX)
{
    zero_mem(src, 0x54);
    src[0x04] = 9;
    wr32(src, 0x08, (u32)x);
    wr32(src, 0x10, (u32)z);
    wr16(src, 0x18, 0x100);
    src[0x34] = 3;
    src[0x35] = 4;
    wr16(src, 0x36, 1);
    src[0x38] = state;
    src[0x39] = fb;
    wr16(src, 0x3A, 0x7000);
    wr16(src, 0x3C, 0x200);
    wr16(src, 0x3E, f110);
    wr16(src, 0x40, f114);
    wr16(src, 0x42, f112);
    wr16(src, 0x44, turnRate);
    wr16(src, 0x46, (u16)spdRaw);
    wr16(src, 0x48, (u16)spreadZ);
    wr16(src, 0x4A, (u16)spreadX);
    wr16(src, 0x4C, 5120);
    wr16(src, 0x4E, 1280);
}

static void run_flyer_csac33(void)
{
    u8 obj[0x140];
    u8 src[0x54];
    void *wrap[3];
    int i;
    zero_mem(obj, sizeof(obj));
    build_flyer_src(src, 0x137400, 0xD0200, 2048, 0x1400, 0x333, 0x399, 0x400, 1, 0x02, 0x6000, 0x5000);
    wrap[0] = 0; wrap[1] = 0; wrap[2] = src;
    g_ground_height = 0x2000;
    func_800197A8(0x457);
    func_80032180(2, obj, wrap);

    put_str("flyer_csac33_init");
    put_sp_int(rd32(obj, 0xF4));
    put_sp_int(rds16(obj, 0xF8));
    put_sp_int(rd32(obj, 0x118));
    put_sp_int(rd32(obj, 0x11C));
    put_sp_int(rd32(obj, 0x50));
    put_sp_int(rd32(obj, 0x54));
    put_sp_int(rd32(obj, 0x58));
    put_sp_int(rd32(obj, 0x78));
    put_sp_int(rd32(obj, 0x7C));
    put_sp_int(rd32(obj, 0x80));
    put_str("\n");

    for (i = 1; i <= 6; i++) {
        func_800324E0(6, obj, 8);
        if (i == 1 || i == 6) {
            put_str(i == 1 ? "flyer_csac33_t1" : "flyer_csac33_t6");
            put_sp_int(rd32(obj, 0x50));
            put_sp_int(rd32(obj, 0x54));
            put_sp_int(rd32(obj, 0x58));
            put_sp_int(rd32(obj, 0x78));
            put_sp_int(rd32(obj, 0x7C));
            put_sp_int(rd32(obj, 0x80));
            put_sp_int(rd32(obj, 0x3C)); /* Up38.y = BasisRows[1][1] */
            put_str("\n");
        }
    }
}

static void run_flyer_csac6_clamp(void)
{
    u8 obj[0x140];
    u8 src[0x54];
    void *wrap[3];
    int i;
    zero_mem(obj, sizeof(obj));
    build_flyer_src(src, 0x127E00, 0xD03FE, 2457, 0x1400, 0x333, 0x399, 0x400, 1, 0x00, 0x7800, 0x7000);
    wrap[0] = 0; wrap[1] = 0; wrap[2] = src;
    g_ground_height = 0x1000;
    func_800197A8(0x457);
    func_80032180(2, obj, wrap);

    put_str("flyer_csac6_init");
    put_sp_int(rd32(obj, 0xF4));
    put_sp_int(rd32(obj, 0x50));
    put_sp_int(rd32(obj, 0x54));
    put_sp_int(rd32(obj, 0x58));
    put_str("\n");

    func_800324E0(6, obj, 8);
    /* Raise terrain above flyer to trigger pos.y < gL clamp and +0x1800 climb */
    g_ground_height = 0x8000;
    for (i = 2; i <= 6; i++) {
        func_800324E0(6, obj, 8);
        if (i == 2 || i == 6) {
            put_str(i == 2 ? "flyer_csac6_clamp_t2" : "flyer_csac6_climb_t6");
            put_sp_int(rd32(obj, 0x50));
            put_sp_int(rd32(obj, 0x54));
            put_sp_int(rd32(obj, 0x58));
            put_sp_int(rd32(obj, 0x7C));
            put_str("\n");
        }
    }
}

static void write_cnet_node(u8 *buf, int idx, u32 n0, u32 n1, u32 n2, u32 n3, s16 x, s16 y, s16 z)
{
    int off = 0x10 + idx * 12;
    u32 w0 = ((n0 & 0x3FF) << 22) | ((n1 & 0x3FF) << 12) | ((n2 & 0x3FF) << 2);
    u16 w4 = (u16)((n3 & 0x3FF) << 6);
    wr32(buf, off + 0, w0);
    wr16(buf, off + 4, w4);
    wr16(buf, off + 6, (u16)x);
    wr16(buf, off + 8, (u16)y);
    wr16(buf, off + 10, (u16)z);
}

static void run_rail_csac75(void)
{
    u8 cnet[0x60];
    u8 *cnet_desc[1];
    u8 arg0[0x14];
    u8 obj[0x160];
    int i;

    zero_mem(cnet, sizeof(cnet));
    wr16(cnet, 0x0E, 3); /* count = 3 */
    write_cnet_node(cnet, 0, 1, 0x3FF, 0x3FF, 0x3FF, 0x8D3, 0x10, 0xE18);
    write_cnet_node(cnet, 1, 2, 0x3FF, 0x3FF, 0x3FF, 0x8D3, 0x18, 0xD2A);
    write_cnet_node(cnet, 2, 0x3FF, 0x3FF, 0x3FF, 0x3FF, 0x903, 0x20, 0xCF6);
    cnet_desc[0] = cnet;

    /* Exact CONFT Csac 75 tuning block (src + 0x34..0x46) */
    zero_mem(arg0, sizeof(arg0));
    wr16(arg0, 0x00, 0x0000);
    wr16(arg0, 0x02, 1024);
    wr16(arg0, 0x04, 0x0133);
    wr16(arg0, 0x06, 0x1000);
    wr16(arg0, 0x08, 1228);
    wr16(arg0, 0x0A, 614);
    wr16(arg0, 0x0C, 0x0666);
    wr16(arg0, 0x0E, 0x2000);
    wr16(arg0, 0x10, 0x0002);
    arg0[0x12] = 0x04;

    zero_mem(obj, sizeof(obj));
    *(void **)(obj + 0x28) = g_dummy_model;
    wr32(obj, 0x50, 0x8D300);
    wr32(obj, 0x54, 0);
    wr32(obj, 0x58, 0xE1800);

    func_800197A8(0x457);
    func_80050E0C(arg0, obj + 0xDC, cnet_desc, obj, (s32 *)(obj + 0x50));

    put_str("rail_csac75_init");
    put_sp_int(rds16(obj, 0xDC + 0x30));
    put_sp_int(rds16(obj, 0xDC + 0x36));
    put_sp_int(rds16(obj, 0xDC + 0x38));
    put_sp_int(rds16(obj, 0xDC + 0x20));
    put_sp_int(rd32(obj, 0x50));
    put_sp_int(rd32(obj, 0x54));
    put_sp_int(rd32(obj, 0x58));
    put_sp_int(rd32(obj, 0x78));
    put_sp_int(rd32(obj, 0x7C));
    put_sp_int(rd32(obj, 0x80));
    put_str("\n");

    for (i = 1; i <= 152; i++) {
        func_8003802C(6, obj, 8);
        if (i == 1 || i == 12 || i == 152) {
            put_str(i == 1 ? "rail_csac75_t1" : (i == 12 ? "rail_csac75_t12" : "rail_csac75_t152"));
            put_sp_int(rd32(obj, 0x84));
            put_sp_int(rd32(obj, 0xDC + 0x1C));
            put_sp_int(rds16(obj, 0xDC + 0x44));
            put_sp_int(rd32(obj, 0x50));
            put_sp_int(rd32(obj, 0x54));
            put_sp_int(rd32(obj, 0x58));
            put_str("\n");
        }
    }
}

static void run_rail_csac57(void)
{
    u8 cnet[0x60];
    u8 *cnet_desc[1];
    u8 arg0[0x14];
    u8 obj[0x160];
    int i;

    zero_mem(cnet, sizeof(cnet));
    wr16(cnet, 0x0E, 2);
    write_cnet_node(cnet, 0, 1, 0x3FF, 0x3FF, 0x3FF, 0x1522, 0x10, 0xBEA);
    write_cnet_node(cnet, 1, 0x3FF, 0x3FF, 0x3FF, 0x3FF, 0x14F8, 0x10, 0xD0C);
    cnet_desc[0] = cnet;

    /* Exact CONFT Csac 57 (type 28 heavy tank) tuning block (src + 0x34..0x46) */
    zero_mem(arg0, sizeof(arg0));
    wr16(arg0, 0x00, 0x0008);
    wr16(arg0, 0x02, 1024);
    wr16(arg0, 0x04, 0x00CC);
    wr16(arg0, 0x06, 0x1000);
    wr16(arg0, 0x08, 1228);
    wr16(arg0, 0x0A, 614);
    wr16(arg0, 0x0C, 0x0666);
    wr16(arg0, 0x0E, 0x2000);
    wr16(arg0, 0x10, 0x0000);
    arg0[0x12] = 0x00;

    zero_mem(obj, sizeof(obj));
    *(void **)(obj + 0x28) = g_dummy_model;
    wr32(obj, 0x50, 0x152200);
    wr32(obj, 0x54, 0);
    wr32(obj, 0x58, 0xBEA00);

    func_800197A8(0x457);
    func_80050E0C(arg0, obj + 0xDC, cnet_desc, obj, (s32 *)(obj + 0x50));
    for (i = 0; i < 12; i++)
        func_8003802C(6, obj, 8);

    put_str("rail_csac57_t12");
    put_sp_int(rds16(obj, 0xDC + 0x30));
    put_sp_int(rds16(obj, 0xDC + 0x26));
    put_sp_int(rd32(obj, 0x84));
    put_sp_int(rd32(obj, 0x50));
    put_sp_int(rd32(obj, 0x54));
    put_sp_int(rd32(obj, 0x58));
    put_str("\n");
}

int main(int argc, char **argv)
{
    __asm__ volatile("la $gp, _gp");
    if (argc < 2) return 2;
    load_slus(argv[1]);
    *(void **)(g_dummy_model + 0x34) = g_dummy_frames;
    func_80019828();
    run_flyer_csac33();
    run_flyer_csac6_clamp();
    run_rail_csac75();
    run_rail_csac57();
    return 0;
}
