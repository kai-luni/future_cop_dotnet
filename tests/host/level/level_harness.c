/* Level terrain harness (test-only, NOT retail code).
 *
 * Executes the ORIGINAL units from future-cop-lapd-ps1/src/game under
 * qemu-mipsel and checks them against the shared vectors
 * future_cop_dotnet/tests/golden/level.vectors:
 *   func_80063DC8  GRDB map header parse (world clip rect, cell counts)
 *   func_800639D4  Ctil "Sect" relocation (table pointers, tileset index, SLFX)
 *   func_800646BC  (asm) height-grid projection into the 17x17x3 vertex buffer
 *   func_80065134  cell word walk of one 4x4 sub-block
 *   func_800648E8  one map-cell packet emit (calls func_800648BC, asm NCLIP)
 *   func_80066F78  gGetGroundHeight (with func_80067480 surface sample)
 *
 * GTE: qemu has no COP2. Every cop2 word raises SIGILL and the handler runs
 * that one instruction against a software register file (same model as
 * tests/host/env_harness.c, plus NCLIP), then resumes at pc+4. The hardwired
 * KSEG0 load of D_8009FA0C (screen width mode) inside func_800646BC faults and
 * is answered for exactly that instruction word, as in env_harness.c.
 *
 * Inputs: argv[1] vectors, argv[2] payload blob (Ctil/Cptc payloads extracted
 * from the retail .MIS by level_test.sh), argv[3] retail SLUS_007.39 (tables
 * D_8009AFE8 / D_8009B1A4), optional argv[4] "--emit" prints every vector line
 * with the observed outputs instead of comparing. */
typedef unsigned int u32;
typedef int s32;
typedef short s16;
typedef unsigned short u16;
typedef unsigned char u8;
typedef signed char s8;

/* units under test (retail symbols) */
extern void func_80063DC8(void *res);
extern void func_800639D4(u8 *data, u32 size, s32 id);
extern void func_800646BC(void);
extern void func_80065134(void *obj, void *list, int a, int b, unsigned int index);
extern s32 func_80066F78(s32 *position, s32 *output, s32 *a2, s32 *a3);
extern void func_80064460(s32 arg0, s32 arg1, void *out);
extern void *D_8009F9E4;           /* gCamera */
extern s32 func_800833A8(s32 name);
extern s32 D_8009C1CC[];           /* the five PA module names (pointers) */
extern s32 D_8009F3D8, D_8009F3E0; /* pointer words to "CW\\" and "PA\\" */
static char module_names[5][16], prefix_cw[8], prefix_pa[8];
/* BIOS A(0x17) strcmp: test infrastructure, the BIOS is not part of the decomp */
s32 func_8008CCA0(s32 a, s32 b)
{
    const u8 *p = (const u8 *)a, *q = (const u8 *)b;
    while (*p && *p == *q) { p++; q++; }
    return (s32)*p - (s32)*q;
}
extern s32 D_8009FA0C;             /* gPlayerCount (also the width mode of func_800646BC) */
static s32 camera_record[0x40];

/* retail globals, placed at (retail address - 0x70000000) by the link */
extern void *D_8009F80C;           /* gTileMapTable */
extern void *D_8009F810;
extern void *D_8009F820;           /* gCellMap */
extern u8 **D_8009F808;            /* gTileBlockTable: pointer to the 0x1A0-byte block func_80063428 allocates */
static u8 *block_table[0x68];
extern s32 D_800A2950[];           /* gWorldClipRect */
extern s32 D_800A2970[];           /* gClipRect */
extern u8 D_8009AFE8[];            /* gVertexIndexTable */
extern u8 D_8009B1A4[];            /* gTileShapeTable */
extern u32 *D_8009F9F0;            /* gPrimPoolNext */
extern u32 *D_8009FA28;            /* gPrimPoolEnd */
extern u32 D_800A8E80[];           /* gTexpageBaseTable */
extern u8 D_800A2630[];            /* gRampVariantTable */
extern u16 D_8009F83C;             /* gTileColourWord */
extern u8 D_800A6F38[];            /* gTileDefTable {s8 count; pad; u32 values[]} */
extern u8 D_800A2931[];            /* SLFX flag byte */
extern void *D_8009FA04;

extern u8 SCRATCH[];               /* 0x1F800000, linked section */

void __main(void) {}

/* ---------------------------------------------------------------- stubs */
static u8 alloc_arena[0x4000];
static s32 alloc_size, alloc_tag, grid1_w, grid1_h, grid2_w, grid2_h;
void *func_80017EBC(s32 size, s32 tag) { alloc_size = size; alloc_tag = tag; return alloc_arena; }
void func_800496D0(s32 w, s32 h) { grid1_w = w; grid1_h = h; }
void func_800834C4(s32 w, s32 h) { grid2_w = w; grid2_h = h; }
s32 *func_8006320C(s32 *pos, s32 *out, s32 *a, u8 *b) { (void)pos; (void)out; (void)a; (void)b; return 0; }
void func_8001A180(s32 *v) { (void)v; }

/* ------------------------------------------------------------- syscalls */
static long sys4(long n, long a, long b, long c, long d)
{
    register long v0 __asm__("$2") = n;
    register long a0 __asm__("$4") = a;
    register long a1 __asm__("$5") = b;
    register long a2 __asm__("$6") = c;
    register long a3 __asm__("$7") = d;
    __asm__ volatile("syscall" : "+r"(v0), "+r"(a3) : "r"(a0), "r"(a1), "r"(a2)
                     : "memory", "$8", "$9", "$10", "$11", "$12", "$13", "$14", "$15", "$24", "$25");
    return v0;
}
static void say(const char *s) { int n = 0; while (s[n]) n++; sys4(4004, 1, (long)s, n, 0); }
static void say_hex(u32 v)
{
    char b[12]; int i, h;
    b[0] = ' '; b[1] = '0'; b[2] = 'x';
    for (i = 0; i < 8; i++) { h = (v >> (28 - 4 * i)) & 15; b[3 + i] = (char)(h < 10 ? '0' + h : 'a' + h - 10); }
    b[11] = 0;
    say(b);
}
static void say_dec(u32 v)
{
    char b[12]; int i = 11;
    b[i] = 0;
    do { b[--i] = (char)('0' + v % 10); v /= 10; } while (v);
    say(b + i);
}
static void say_sdec(s32 v) { if (v < 0) { say("-"); say_dec((u32)-v); } else say_dec((u32)v); }
static int read_file(const char *path, u8 *buf, int cap)
{
    int fd = (int)sys4(4005, (long)path, 0, 0, 0), n, total = 0;
    if (fd < 0) return -1;
    while ((n = (int)sys4(4003, fd, (long)(buf + total), cap - total, 0)) > 0) {
        total += n;
        if (total >= cap) { sys4(4006, fd, 0, 0, 0); return -1; }
    }
    sys4(4006, fd, 0, 0, 0);
    return total;
}

/* ------------------------------------------------------------------ GTE */
static u32 gd[32], gc[32];
static u32 g_width_mode;
static int g_trap_error;
static s32 sx16(u32 v) { return (s32)(s16)v; }
static u32 rd_data(int r)
{
    switch (r) {
    case 1: case 3: case 5: case 8: case 9: case 10: case 11: return (u32)sx16(gd[r]);
    case 7: case 16: case 17: case 18: case 19: return gd[r] & 0xFFFF;
    case 15: return gd[14];
    default: return gd[r];
    }
}
static void wr_data(int r, u32 v)
{
    if (r == 15) { gd[12] = gd[13]; gd[13] = gd[14]; gd[14] = v; return; }
    gd[r] = v;
}
static u32 rd_ctrl(int r)
{
    switch (r) {
    case 4: case 12: case 20: case 26: case 27: case 29: case 30: return (u32)sx16(gc[r]);
    default: return gc[r];
    }
}
static void wr_ctrl(int r, u32 v)
{
    if (r == 31) { v &= 0x7FFFF000u; if (v & 0x7F87E000u) v |= 0x80000000u; }
    gc[r] = v;
}
static s32 mat_el(int base, int i, int j)
{
    int idx = i * 3 + j;
    u32 w = gc[base + idx / 2];
    return (idx & 1) ? (s32)(s16)(w >> 16) : (s32)(s16)w;
}
static s32 sat_ir(long long v, int lm, u32 *fl, u32 bit)
{
    long long lo = lm ? 0 : -0x8000;
    if (v > 0x7FFF) { *fl |= bit; return 0x7FFF; }
    if (v < lo) { *fl |= bit; return (s32)lo; }
    return (s32)v;
}
static void mac_ovf(long long v, int i, u32 *fl)
{
    if (v > 0x7FFFFFFFFFFLL) *fl |= 0x40000000u >> i;
    else if (v < -0x80000000000LL) *fl |= 0x08000000u >> i;
}
static void finish_flag(u32 fl) { if (fl & 0x7F87E000u) fl |= 0x80000000u; gc[31] = fl; }
/* gte_quot of tests/host/gte_ref.c (rounded H*65536/SZ3, overflow H >= 2*SZ3) */
static s32 gte_quot(s32 h, s32 sz3, s32 *overflow)
{
    u32 num, q;
    *overflow = h >= 2 * sz3;
    if (*overflow) return 0x1FFFF;
    num = (u32)h * 65536u + (u32)(sz3 / 2);
    q = num / (u32)sz3;
    return q > 0x1FFFF ? 0x1FFFF : (s32)q;
}
static int gte_cmd(u32 insn)
{
    int sf = (insn >> 19) & 1, lm = (insn >> 10) & 1, i;
    u32 fl = 0;
    if ((insn & 0x3F) == 0x06) { /* NCLIP */
        s32 x0 = sx16(gd[12]), y0 = sx16(gd[12] >> 16), x1 = sx16(gd[13]), y1 = sx16(gd[13] >> 16);
        s32 x2 = sx16(gd[14]), y2 = sx16(gd[14] >> 16);
        long long m = (long long)x0 * y1 + (long long)x1 * y2 + (long long)x2 * y0
                    - (long long)x0 * y2 - (long long)x1 * y0 - (long long)x2 * y1;
        if (m > 0x7FFFFFFFLL) fl |= 0x10000; else if (m < -0x80000000LL) fl |= 0x8000;
        gd[24] = (u32)(s32)m;
        finish_flag(fl);
        return 0;
    }
    if ((insn & 0x3F) == 0x12) { /* MVMVA (env_harness.c decode) */
        int mx = (insn >> 17) & 3, vv = (insn >> 15) & 3, cv = (insn >> 13) & 3;
        s32 vec[3];
        if (mx == 3 || cv == 2) return -1;
        if (vv == 3) { vec[0] = sx16(gd[9]); vec[1] = sx16(gd[10]); vec[2] = sx16(gd[11]); }
        else { vec[0] = sx16(gd[vv * 2]); vec[1] = sx16(gd[vv * 2] >> 16); vec[2] = sx16(gd[vv * 2 + 1]); }
        for (i = 0; i < 3; i++) {
            long long mac = 0;
            if (cv != 3) mac = (long long)(s32)gc[(cv == 0 ? 5 : 13) + i] * 4096;
            mac += (long long)mat_el(mx * 8, i, 0) * vec[0] + (long long)mat_el(mx * 8, i, 1) * vec[1]
                 + (long long)mat_el(mx * 8, i, 2) * vec[2];
            mac_ovf(mac, i, &fl);
            if (sf) mac >>= 12;
            gd[25 + i] = (u32)(s32)mac;
            gd[9 + i] = (u32)sat_ir(mac, lm, &fl, 0x01000000u >> i);
        }
        finish_flag(fl);
        return 0;
    }
    if ((insn & 0x3F) == 0x01) { /* RTPS */
        s32 vx = sx16(gd[0]), vy = sx16(gd[0] >> 16), vz = sx16(gd[1]);
        s32 ir[3], sz3, q, ovf, sx, sy;
        long long raw3 = 0, mac0;
        for (i = 0; i < 3; i++) {
            long long mac = (long long)(s32)gc[5 + i] * 4096 + (long long)mat_el(0, i, 0) * vx
                          + (long long)mat_el(0, i, 1) * vy + (long long)mat_el(0, i, 2) * vz;
            mac_ovf(mac, i, &fl);
            if (i == 2) raw3 = mac;
            if (sf) mac >>= 12;
            gd[25 + i] = (u32)(s32)mac;
            if (i < 2) ir[i] = sat_ir(mac, lm, &fl, 0x01000000u >> i);
            else {
                u32 dummy = 0;
                ir[2] = sat_ir(mac, lm, &dummy, 1);
                sat_ir(raw3 >> 12, 0, &fl, 0x00400000u);
            }
            gd[9 + i] = (u32)ir[i];
        }
        raw3 >>= 12;
        if (raw3 > 0xFFFF) { fl |= 0x40000; sz3 = 0xFFFF; }
        else if (raw3 < 0) { fl |= 0x40000; sz3 = 0; }
        else sz3 = (s32)raw3;
        gd[16] = gd[17]; gd[17] = gd[18]; gd[18] = gd[19]; gd[19] = (u32)sz3;
        q = gte_quot((s32)(gc[26] & 0xFFFF), sz3, &ovf);
        if (ovf) fl |= 0x20000;
        mac0 = (long long)ir[0] * q + (s32)gc[24];
        if (mac0 > 0x7FFFFFFFLL) fl |= 0x10000; else if (mac0 < -0x80000000LL) fl |= 0x8000;
        mac0 >>= 16;
        if (mac0 > 0x3FF) { fl |= 0x4000; sx = 0x3FF; } else if (mac0 < -0x400) { fl |= 0x4000; sx = -0x400; } else sx = (s32)mac0;
        mac0 = (long long)ir[1] * q + (s32)gc[25];
        if (mac0 > 0x7FFFFFFFLL) fl |= 0x10000; else if (mac0 < -0x80000000LL) fl |= 0x8000;
        mac0 >>= 16;
        if (mac0 > 0x3FF) { fl |= 0x2000; sy = 0x3FF; } else if (mac0 < -0x400) { fl |= 0x2000; sy = -0x400; } else sy = (s32)mac0;
        gd[12] = gd[13]; gd[13] = gd[14]; gd[14] = ((u32)sy << 16) | ((u32)sx & 0xFFFF);
        mac0 = (long long)sx16(gc[27]) * q + (s32)gc[28];
        gd[24] = (u32)(s32)mac0;
        mac0 >>= 12;
        if (mac0 > 0x1000) { fl |= 0x1000; mac0 = 0x1000; } else if (mac0 < 0) { fl |= 0x1000; mac0 = 0; }
        gd[8] = (u32)mac0;
        finish_flag(fl);
        return 0;
    }
    return -1;
}
static u32 get_reg(unsigned char *uc, int r) { return *(u32 *)(uc + 40 + 8 * r); }
static void set_reg(unsigned char *uc, int r, u32 v)
{
    u32 *p = (u32 *)(uc + 40 + 8 * r);
    if (r == 0) return;
    p[0] = v; p[1] = (v & 0x80000000u) ? 0xFFFFFFFFu : 0;
}
static void trap_fail(u32 pc, u32 insn)
{
    say("level: unhandled trap at"); say_hex(pc); say_hex(insn); say("\n");
    sys4(4001, 3, 0, 0, 0);
}
static void on_sigill(int sig, void *info, void *ucv)
{
    unsigned char *uc = ucv;
    u32 *pcp = (u32 *)(uc + 32);
    u32 pc = pcp[0], insn = *(u32 *)pc;
    int rt = (insn >> 16) & 31, rd = (insn >> 11) & 31;
    (void)sig; (void)info;
    if ((insn >> 26) == 0x32) { /* lwc2 rt, imm(base) (func_800640D8) */
        u32 addr = get_reg(uc, (insn >> 21) & 31) + (u32)(s32)(s16)(insn & 0xFFFF);
        wr_data(rt, *(u32 *)addr);
        pcp[0] = pc + 4; pcp[1] = 0xFFFFFFFFu;
        return;
    }
    if ((insn >> 26) != 0x12) trap_fail(pc, insn);
    if (insn & 0x02000000u) { if (gte_cmd(insn & 0x01FFFFFFu) < 0) trap_fail(pc, insn); }
    else switch ((insn >> 21) & 31) {
    case 0: set_reg(uc, rt, rd_data(rd)); break;
    case 2: set_reg(uc, rt, rd_ctrl(rd)); break;
    case 4: wr_data(rd, get_reg(uc, rt)); break;
    case 6: wr_ctrl(rd, get_reg(uc, rt)); break;
    default: trap_fail(pc, insn);
    }
    pcp[0] = pc + 4; pcp[1] = 0xFFFFFFFFu;
}
static void on_sigsegv(int sig, void *info, void *ucv)
{
    unsigned char *uc = ucv;
    u32 *pcp = (u32 *)(uc + 32);
    u32 pc = pcp[0], insn = *(u32 *)pc;
    (void)sig; (void)info;
    /* lw $t2, %lo(D_8009FA0C)($t2) with $t2 = 0x800A0000 (func_800646BC) */
    if (insn != 0x8D4AFA0Cu || get_reg(uc, 10) != 0x800A0000u) { g_trap_error = 1; trap_fail(pc, insn); }
    set_reg(uc, 10, g_width_mode);
    pcp[0] = pc + 4; pcp[1] = 0xFFFFFFFFu;
}
struct ksa { u32 flags; void (*handler)(int, void *, void *); u32 mask[4]; };
static int install(int sig, void (*h)(int, void *, void *))
{
    struct ksa sa; int i;
    sa.flags = 8;
    sa.handler = h;
    for (i = 0; i < 4; i++) sa.mask[i] = 0;
    return (int)sys4(4194, sig, (long)&sa, 0, 16);
}

/* func_800646BC takes its arguments in $a0..$a3 only (handwritten). */
static void project_grid(void *obj, void *dst, s16 *rect, u32 mask)
{
    register void *a0 __asm__("$4") = obj;
    register void *a1 __asm__("$5") = dst;
    register s16 *a2 __asm__("$6") = rect;
    register u32 a3 __asm__("$7") = mask;
    __asm__ volatile("jal func_800646BC\n\tnop" : "+r"(a0), "+r"(a1), "+r"(a2), "+r"(a3) :
                     : "memory", "$2", "$3", "$8", "$9", "$10", "$11", "$12", "$13", "$14", "$15",
                       "$24", "$25", "$31");
}

/* --------------------------------------------------------- level state */
#define MAXSECT 64
static u8 blob[0x40000];
static int blob_len;
static u8 *sect_ptr[MAXSECT];
static u32 sect_size[MAXSECT];
static u8 *grdb_ptr; static u32 grdb_size;
static int sect_loaded[MAXSECT], grdb_loaded;
static u8 vbuf[17 * 17 * 3 * 8];
#define POOL_WORDS (13 * 4096)
static u32 pool[POOL_WORDS + 16];

static u32 fnv(const u8 *p, int n)
{
    u32 h = 0x811C9DC5u; int i;
    for (i = 0; i < n; i++) { h ^= p[i]; h *= 0x01000193u; }
    return h;
}

static int parse_blob(void)
{
    int p = 0;
    while (p + 12 <= blob_len) {
        u32 kind = *(u32 *)(blob + p), id = *(u32 *)(blob + p + 4), size = *(u32 *)(blob + p + 8);
        p += 12;
        if (kind == 1 && id < MAXSECT) { sect_ptr[id] = blob + p; sect_size[id] = size; }
        else if (kind == 2) { grdb_ptr = blob + p; grdb_size = size; }
        p += (int)((size + 3) & ~3u);
    }
    return grdb_ptr != 0;
}

/* -------------------------------------------------------------- vectors */
#define MAXV 200
#define ROMOFF(a) ((a) - 0x80010000u + 0x800u)
static s32 v[MAXV];
static int nin, nout, line_no, failures, ncase, emit;
static int per_kind[8];
static const char *kinds[] = { "GRDB", "SECT", "TILE", "GROUND", "WINDOW", "MODPREFIX", 0 };
static s32 got[MAXV];
static int ngot;

static void put(s32 x) { got[ngot++] = x; }

static int streq(const char *a, const char *b) { while (*a && *a == *b) { a++; b++; } return *a == *b; }

static void load_grdb(void)
{
    struct { u8 *base; u8 pad[0x1C]; u32 size; } res;
    if (grdb_loaded) return;
    res.base = grdb_ptr; res.size = grdb_size;
    func_80063DC8(&res);
    grdb_loaded = 1;
}
static void load_sect(int id)
{
    if (sect_loaded[id] || !sect_ptr[id]) return;
    func_800639D4(sect_ptr[id], sect_size[id], id);
    sect_loaded[id] = 1;
}
static void load_all(void)
{
    int i;
    load_grdb();
    for (i = 0; i < MAXSECT; i++) load_sect(i);
}

static void run_case(const char *kind)
{
    int i;
    ngot = 0;
    if (streq(kind, "GRDB") && nin == 0) {
        s32 *g;
        load_grdb();
        g = (s32 *)D_8009F80C;
        put((s32)((u8 *)g - grdb_ptr));
        put(g[0]); put(g[1]); put(g[2]); put(g[3]); put(g[4]); put(g[5]); put(g[6]);
        for (i = 0; i < 6; i++) put(D_800A2950[i]);
        put(D_800A2970[4]); put(D_800A2970[5]);
        put(alloc_size); put(alloc_tag); put(grid1_w); put(grid1_h); put(grid2_w); put(grid2_h);
    } else if (streq(kind, "SECT") && nin == 1) {
        int id = v[0];
        u8 *s;
        load_sect(id);
        s = sect_ptr[id];
        if (!s) { say("level: missing section\n"); sys4(4001, 2, 0, 0, 0); }
        put((s32)(((u8 **)s)[0] - s)); put((s32)(((u8 **)s)[1] - s)); put((s32)(((u8 **)s)[2] - s));
        put((s32)(s8)s[0x36F]);
        put((s32)(D_8009F808[id] == s));
        put((s32)(s8)D_800A6F38[0]);
        put(D_800A2931[0]);
    } else if (streq(kind, "TILE") && nin == 21) {
        /* id rx ry rz mask16 wmode h ofx ofy r00..r22 a b colourword (v[0..20]) */
        int id = v[0], k;
        s16 rect[4];
        u8 *s;
        u32 m;
        load_all();
        s = sect_ptr[id];
        if (!s) { say("level: missing section\n"); sys4(4001, 2, 0, 0, 0); }
        rect[0] = (s16)v[1]; rect[1] = (s16)v[2]; rect[2] = (s16)v[3]; rect[3] = 0;
        g_width_mode = (u32)v[5];
        D_8009FA0C = v[5];
        for (i = 0; i < 32; i++) gc[i] = 0;
        gc[0] = ((u32)(u16)v[9]) | ((u32)(u16)v[10] << 16);
        gc[1] = ((u32)(u16)v[11]) | ((u32)(u16)v[12] << 16);
        gc[2] = ((u32)(u16)v[13]) | ((u32)(u16)v[14] << 16);
        gc[3] = ((u32)(u16)v[15]) | ((u32)(u16)v[16] << 16);
        gc[4] = (u32)(u16)v[17];
        gc[26] = (u32)v[6]; gc[24] = (u32)v[7]; gc[25] = (u32)v[8];
        for (i = 0; i < (int)sizeof(vbuf); i++) vbuf[i] = 0x5A;
        project_grid(s, vbuf, rect, (u32)v[4] << 16);
        k = 0;
        for (i = 0; i < 17 * 17 * 3; i++) if (vbuf[i * 8 + 7] != 0x5A) k++;
        put((s32)fnv(vbuf, sizeof(vbuf)));
        put(k);
        /* the 0x70-byte shape table copy func_8006531C makes before the cell pass */
        for (i = 0; i < 0x70; i++) SCRATCH[i] = D_8009B1A4[i];
        D_8009F83C = (u16)v[20];
        for (i = 0; i < POOL_WORDS; i++) pool[i] = 0;
        D_8009F9F0 = pool;
        D_8009FA28 = pool + POOL_WORDS;
        m = (u32)v[4];
        {
            int a, b;
            for (a = 0xC0; a >= 0; a -= 0x40)
                for (b = 0xC; b >= 0; b -= 4) {
                    if (m & 1) func_80065134(s, vbuf, v[18], v[19], (u32)(a + b));
                    m >>= 1;
                }
        }
        k = (int)(D_8009F9F0 - pool) / 13;
        put(k);
        put((s32)fnv((u8 *)pool, k * 13 * 4));
        for (i = 0; i < 13; i++) put((s32)pool[i]);
    } else if (streq(kind, "WINDOW") && nin == 15) {
        /* eye_x eye_y eye_z arg0 arg1 players r00..r22 -> 25 x (mask, section id or -1, x, y, z) */
        s32 out[25 * 4];
        int e, j;
        load_all();
        for (i = 0; i < 0x40; i++) camera_record[i] = 0;
        camera_record[0x7C / 4] = v[0]; camera_record[0x80 / 4] = v[1]; camera_record[0x84 / 4] = v[2];
        camera_record[0] = v[0]; camera_record[1] = v[1]; camera_record[2] = v[2];
        D_8009F9E4 = camera_record;
        D_8009FA0C = v[5];
        for (i = 0; i < 32; i++) gc[i] = 0;
        gc[0] = ((u32)(u16)v[6]) | ((u32)(u16)v[7] << 16);
        gc[1] = ((u32)(u16)v[8]) | ((u32)(u16)v[9] << 16);
        gc[2] = ((u32)(u16)v[10]) | ((u32)(u16)v[11] << 16);
        gc[3] = ((u32)(u16)v[12]) | ((u32)(u16)v[13] << 16);
        gc[4] = (u32)(u16)v[14];
        for (i = 0; i < 25 * 4; i++) out[i] = 0x5A5A5A5A;
        func_80064460(v[3], v[4], out);
        for (e = 0; e < 25; e++) {
            s32 *ent = out + e * 4;
            s32 id = -1;
            put(ent[0]);
            if ((u32)ent[1] != 0x5A5A5A5Au)
                for (j = 0; j < MAXSECT; j++) if (sect_ptr[j] && (void *)sect_ptr[j] == (void *)ent[1]) id = j;
            put(id);
            if ((u32)ent[2] == 0x5A5A5A5Au) { put(0x5A5A); put(0x5A5A); put(0x5A5A); }
            else { put((s16)ent[2]); put((s16)(ent[2] >> 16)); put((s16)ent[3]); }
        }
    } else if (streq(kind, "MODPREFIX") && nin >= 1 && nin < 40) {
        /* module name as character codes -> 1 = "PA\\" prefix (D_8009F3E0), 0 = "CW\\" (D_8009F3D8) */
        static char name[48];
        s32 r;
        for (i = 0; i < nin; i++) name[i] = (char)v[i];
        name[nin] = 0;
        r = func_800833A8((s32)name);
        put(r == D_8009F3E0 ? 1 : r == D_8009F3D8 ? 0 : 2);
    } else if (streq(kind, "GROUND") && nin == 3) {
        s32 pos[3], attr = 0x5A5A, h;
        load_all();
        pos[0] = v[0]; pos[1] = v[1]; pos[2] = v[2];
        h = func_80066F78(pos, 0, &attr, 0);
        put(h); put(attr);
    } else {
        say("level: malformed vector line "); say_dec((u32)line_no); say("\n");
        sys4(4001, 2, 0, 0, 0);
    }
    for (i = 0; kinds[i]; i++) if (streq(kind, kinds[i])) per_kind[i]++;
    ncase++;
    if (emit) {
        say(kind);
        for (i = 0; i < nin; i++) { say(" "); say_sdec(v[i]); }
        say(" =");
        for (i = 0; i < ngot; i++) say_hex((u32)got[i]);
        say("\n");
        return;
    }
    if (nout != ngot) {
        say("level: FAIL line "); say_dec((u32)line_no); say(" output count "); say_dec((u32)nout);
        say(" want "); say_dec((u32)ngot); say("\n");
        failures++;
        return;
    }
    for (i = 0; i < ngot; i++)
        if ((u32)got[i] != (u32)v[nin + i]) {
            say("level: FAIL "); say(kind); say(" line "); say_dec((u32)line_no); say(" field ");
            say_dec((u32)i); say(" got"); say_hex((u32)got[i]); say(" want"); say_hex((u32)v[nin + i]); say("\n");
            failures++;
        }
}

int main(int argc, char **argv)
{
    static char data[0x40000];
    static u8 rom[0xA0000];
    int n, i, k, neg, f0, eq, romlen;
    char *p, kind[16];
    if (argc < 4) { say("level: usage harness vectors blob rom [--emit]\n"); return 2; }
    emit = argc > 4 && streq(argv[4], "--emit");
    if (install(4, on_sigill) < 0 || install(10, on_sigsegv) < 0 || install(11, on_sigsegv) < 0) {
        say("level: sigaction failed\n"); return 2;
    }
    blob_len = read_file(argv[2], blob, sizeof(blob));
    if (blob_len <= 0 || !parse_blob()) { say("level: cannot read payload blob\n"); return 2; }
    romlen = read_file(argv[3], rom, sizeof(rom));
    if (romlen < (int)ROMOFF(0x8009B224u)) { say("level: cannot read retail executable\n"); return 2; }
    /* D_8009AFE8 (128 x 4 bytes) and D_8009B1A4 (0x80 bytes), file offset = addr - 0x80010000 + 0x800 */
    for (i = 0; i < 0x200; i++) D_8009AFE8[i] = rom[ROMOFF(0x8009AFE8u) + i];
    for (i = 0; i < 0x80; i++) D_8009B1A4[i] = rom[ROMOFF(0x8009B1A4u) + i];
    for (i = 0; i < 10; i++) D_800A8E80[i] = 0;
    D_8009F808 = block_table;
    /* func_800833A8 data: copy the retail strings behind D_8009C1CC / D_8009F3D8 / D_8009F3E0 */
    {
        int j;
        for (i = 0; i < 5; i++) {
            u32 ptr = *(u32 *)(rom + ROMOFF(0x8009C1CCu) + i * 4);
            for (j = 0; j < 15 && rom[ROMOFF(ptr) + j]; j++) module_names[i][j] = (char)rom[ROMOFF(ptr) + j];
            module_names[i][j] = 0;
            D_8009C1CC[i] = (s32)module_names[i];
        }
        for (j = 0; j < 7 && rom[ROMOFF(*(u32 *)(rom + ROMOFF(0x8009F3D8u))) + j]; j++)
            prefix_cw[j] = (char)rom[ROMOFF(*(u32 *)(rom + ROMOFF(0x8009F3D8u))) + j];
        for (j = 0; j < 7 && rom[ROMOFF(*(u32 *)(rom + ROMOFF(0x8009F3E0u))) + j]; j++)
            prefix_pa[j] = (char)rom[ROMOFF(*(u32 *)(rom + ROMOFF(0x8009F3E0u))) + j];
        D_8009F3D8 = (s32)prefix_cw;
        D_8009F3E0 = (s32)prefix_pa;
    }
    n = read_file(argv[1], (u8 *)data, sizeof(data) - 1);
    if (n <= 0) { say("level: cannot read vectors\n"); return 2; }
    data[n] = 0; p = data; line_no = 1;
    while (*p) {
        if (*p == '\n') { line_no++; p++; continue; }
        if (*p == ' ' || *p == '\t' || *p == '\r') { p++; continue; }
        if (*p == '#') { while (*p && *p != '\n') p++; continue; }
        for (i = 0; i < 15 && *p > ' '; i++) kind[i] = *p++;
        kind[i] = 0;
        if (streq(kind, "MIS")) { while (*p && *p != '\n') p++; continue; }
        k = 0; eq = -1;
        for (;;) {
            u32 x = 0; int digits = 0;
            while (*p == ' ' || *p == '\t' || *p == '\r') p++;
            if (!*p || *p == '\n' || *p == '#') break;
            if (*p == '=') { eq = k; p++; continue; }
            neg = 0;
            if (*p == '-') { neg = 1; p++; }
            if (p[0] == '0' && (p[1] == 'x' || p[1] == 'X')) {
                p += 2;
                for (;;) {
                    char c = *p;
                    if (c >= '0' && c <= '9') f0 = c - '0';
                    else if (c >= 'a' && c <= 'f') f0 = c - 'a' + 10;
                    else if (c >= 'A' && c <= 'F') f0 = c - 'A' + 10;
                    else break;
                    x = (x << 4) | (u32)f0; p++; digits++;
                }
            } else {
                while (*p >= '0' && *p <= '9') { x = x * 10 + (u32)(*p - '0'); p++; digits++; }
            }
            if (!digits || (*p > ' ' && *p != '#') || k >= MAXV) {
                say("level: bad number on line "); say_dec((u32)line_no); say("\n"); return 2;
            }
            v[k++] = neg ? -(s32)x : (s32)x;
        }
        if (streq(kind, "TEXPAGES") && k == 10 && eq < 0) {
            /* D_800A8E80[0..9], the tpage/CLUT words func_80063C5C publishes */
            for (i = 0; i < 10; i++) D_800A8E80[i] = (u32)v[i];
            if (emit) { say(kind); for (i = 0; i < k; i++) say_hex((u32)v[i]); say("\n"); }
            continue;
        }
        if (streq(kind, "RAMP") && k == 64 && eq < 0) {
            /* D_800A2630[0..255] as 64 little-endian words (func_800654F8 output) */
            for (i = 0; i < 64; i++) {
                D_800A2630[i * 4] = (u8)v[i]; D_800A2630[i * 4 + 1] = (u8)(v[i] >> 8);
                D_800A2630[i * 4 + 2] = (u8)(v[i] >> 16); D_800A2630[i * 4 + 3] = (u8)((u32)v[i] >> 24);
            }
            if (emit) { say(kind); for (i = 0; i < k; i++) say_hex((u32)v[i]); say("\n"); }
            continue;
        }
        if (eq < 0 && !emit) { say("level: missing '=' on line "); say_dec((u32)line_no); say("\n"); return 2; }
        nin = eq < 0 ? k : eq;
        nout = k - nin;
        run_case(kind);
    }
    if (emit) return g_trap_error ? 1 : 0;
    for (i = 0; kinds[i]; i++) { say("level: "); say(kinds[i]); say(" "); say_dec((u32)per_kind[i]); say(" cases\n"); }
    if (!ncase || failures || g_trap_error) { say("level: FAIL ("); say_dec((u32)failures); say(" mismatches)\n"); return 1; }
    say("level: all "); say_dec((u32)ncase);
    say(" vectors PASS (retail C + retail GTE asm words, cop2 via gte_ref-model trap)\n");
    return 0;
}
