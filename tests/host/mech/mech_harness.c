/* Walker locomotion harness (test-only, NOT retail code).
 *
 * Executes the ORIGINAL decomp units (future-cop-lapd-ps1/src/game, unchanged) under qemu-mipsel
 * and checks them against tests/golden/mech-walk.vectors, which the .NET port reads as well:
 *   func_800549F0  on-foot locomotion tick (walker form of the player mech)
 *   and its callee chain: stick decode 800538C8/8001E7B8, delta 80054590, knockback 80052550,
 *   world clamp 8006943C, grid ray 800684AC/80067914, ground 80066F78/80067480/8006320C,
 *   platform 80053514, surface attributes 80053188, footprint walls 80052708/80052410 (+GTE
 *   basis 80051C50/80010974/800188D0/80010750/80010234), fall damage 80052F54, turn snap
 *   80052E28, shadow record 80051AC8, animation track 800448D8/80044B34/800449F8/8002A5B4/
 *   80044AE8, PRNG 800197A8/80019720/80019668, fixed-point math 8001846C 8001A180 8001A314
 *   8001A7AC 8001A030 8001ABE0 8001AB00 8001AC28 8001A6F8 80019FCC 80095900 80095C14 80019904
 *   (table from 80019828/80018818/800187C8), pad path 8001E588/8001E620/8001EF8C/80057864/800576A8.
 *
 * NOT executed (recorders or fail-stubs, see the stub section): sound/stream events func_80047088,
 * the script message dispatcher func_8005908C, the model-frame setter func_800175F8, actor/2P
 * collision func_80052098, 2P camera distance func_8001A074, platform rotation func_8001A578 and
 * the dynamic-object surface helpers of func_8006320C (the object queue is empty).
 *
 * GTE: qemu has no COP2. Every cop2 word raises SIGILL and the handler runs that one instruction
 * against a software register file (MVMVA/RTPS/NCLIP model of level_harness.c plus OP and the
 * LZCS/LZCR pair), then resumes at pc+4. Hard-wired KSEG0 loads (lui 0x800A + lh in func_80095900)
 * fault and are answered from the retail image mapped at address - 0x70000000. A green run is a
 * C-semantics + software-GTE result, not a PS1 hardware measurement.
 *
 * Inputs: argv[1] vectors, argv[2] payload blob (Ctil/Cptc of the MIS line), argv[3] retail
 * SLUS_007.39 (copied whole to its mapped address), optional argv[4] "--emit". */
typedef unsigned int u32;
typedef int s32;
typedef short s16;
typedef unsigned short u16;
typedef unsigned char u8;
typedef signed char s8;

/* ------------------------------------------------------------ units under test (retail names) */
extern s32 func_800549F0(void *self, s32 unused, s32 steps, u32 flags);
extern s32 func_800538C8(u8 *context, u32 *flags, s32 *v, s32 *h);
extern void func_8001846C(u32 angle, s32 *s, s32 *c);
extern s32 func_80095900(s32 v);
extern void func_8001A180(s32 *v);
extern s32 func_8001A314(s32 *v);
extern s32 func_8001A7AC(s32 *v);
extern void func_80054590(void *obj, s32 speed, s32 lane, s32 *out, s32 dir);
extern s32 func_80052550(void *obj, s32 *vec);
extern s32 func_800684AC(s32 *p, s32 *q, s32 *outA, s32 *outB, s32 flag);
extern s32 func_80066F78(s32 *pos, s32 *out, s32 *attr, s32 *surface);
extern void func_80053188(void *obj, u32 attr);
extern void func_80052708(void *obj, s32 arg);
extern u32 func_80044B34(void *script, void *track, s32 speed, s32 dir);
extern void func_80019828(void);
extern void func_800197A8(s32 seed);
extern s32 func_80019668(void);
extern void func_8001E588(u32 port);
extern void func_80057864(void);
extern void func_800576A8(u32 *out, s32 index);
extern void func_80063DC8(void *res);
extern void func_800639D4(u8 *data, u32 size, s32 id);

/* retail globals, linked at (retail address - 0x70000000) */
extern u8 D_RAM[];                 /* 0x10010000 == retail 0x80010000 */
extern s32 D_8009FA0C;             /* gPlayerCount */
extern u8 *D_8009FA94;             /* gGameSession */
extern u8 D_8009FA8C[];            /* gPlayerInvulnerable */
extern u8 **D_8009F808;            /* gTileBlockTable */
extern s32 D_8009F838;             /* gQueueCount (func_8006320C object queue) */
extern u8 D_800A78F0[];            /* gPadBuffers, 0x24 bytes each */
extern u32 D_8009F7C0[];           /* gPadWords */
extern s32 D_800A2950[];           /* gWorldClipRect */
extern u8 D_800A8DC0[];            /* gPlayerCollisionCandidates */

void __main(void) {}

/* -------------------------------------------------------------------------------- syscalls */
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
static void die(const char *m) { say("mech: "); say(m); say("\n"); sys4(4001, 3, 0, 0, 0); }
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
static int streq(const char *a, const char *b) { while (*a && *a == *b) { a++; b++; } return *a == *b; }

/* ------------------------------------------------------------------------------------- GTE */
static u32 gd[32], gc[32];
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
    if (r == 30) { /* LZCS -> LZCR: count of leading bits equal to the sign bit */
        u32 x = (v & 0x80000000u) ? ~v : v; int n = 0;
        while (n < 32 && !(x & 0x80000000u)) { x <<= 1; n++; }
        gd[30] = v; gd[31] = (u32)n; return;
    }
    if (r == 31) return;
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
static int gte_cmd(u32 insn)
{
    int sf = (insn >> 19) & 1, lm = (insn >> 10) & 1, i;
    u32 fl = 0;
    if ((insn & 0x3F) == 0x0C) { /* OP: cross product of the rotation diagonal with IR */
        s32 d1 = mat_el(0, 0, 0), d2 = mat_el(0, 1, 1), d3 = mat_el(0, 2, 2);
        s32 i1 = sx16(gd[9]), i2 = sx16(gd[10]), i3 = sx16(gd[11]);
        long long m[3];
        m[0] = (long long)d2 * i3 - (long long)d3 * i2;
        m[1] = (long long)d3 * i1 - (long long)d1 * i3;
        m[2] = (long long)d1 * i2 - (long long)d2 * i1;
        for (i = 0; i < 3; i++) {
            long long mac = m[i];
            mac_ovf(mac, i, &fl);
            if (sf) mac >>= 12;
            gd[25 + i] = (u32)(s32)mac;
            gd[9 + i] = (u32)sat_ir(mac, lm, &fl, 0x01000000u >> i);
        }
        finish_flag(fl);
        return 0;
    }
    if ((insn & 0x3F) == 0x12) { /* MVMVA */
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
    return -1;
}
static u32 get_reg(unsigned char *uc, int r) { return *(u32 *)(uc + 40 + 8 * r); }
static void set_reg(unsigned char *uc, int r, u32 v)
{
    u32 *p = (u32 *)(uc + 40 + 8 * r);
    if (r == 0) return;
    p[0] = v; p[1] = (v & 0x80000000u) ? 0xFFFFFFFFu : 0;
}
static int g_trap_error;
extern void func_80067914(void);
/* Emulator-only breakpoint on the retail callee's $s1 save. Save the actual host register,
 * then map a walker pointer to its PS1 KSEG0 value while the callee runs. Its unchanged
 * epilogue restores the host value; grid-edge values and standalone RAY cases are untouched. */
static int g_walker_probe;
static void segment_break(int sig, void *info, void *ucv)
{
    unsigned char *uc = ucv;
    u32 pc = *(u32 *)(uc + 32), s1 = get_reg(uc, 17);
    (void)sig; (void)info;
    if (pc != (u32)func_80067914 + 32) sys4(4001, 4, 0, 0, 0);
    *(u32 *)(get_reg(uc, 29) + 36) = s1; /* original sw $17,36($sp) */
    if (g_walker_probe && s1 == 0x0E000000u) set_reg(uc, 17, 0x800E0000u);
    *(u32 *)(uc + 32) = pc + 4;
}
static void trap_fail(u32 pc, u32 insn)
{
    say("mech: unhandled trap at"); say_hex(pc); say_hex(insn); say("\n");
    sys4(4001, 3, 0, 0, 0);
}
static void on_sigill(int sig, void *info, void *ucv)
{
    unsigned char *uc = ucv;
    u32 *pcp = (u32 *)(uc + 32);
    u32 pc = pcp[0], insn = *(u32 *)pc;
    int rt = (insn >> 16) & 31, rd = (insn >> 11) & 31;
    (void)sig; (void)info;
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
/* A load from a hard-wired KSEG0 address (e.g. lui $t5,0x800A; lh $t5,-0x20E0($t5) in
 * func_80095900) is answered from the mapped retail image; anything else is a failure. */
static void on_sigsegv(int sig, void *info, void *ucv)
{
    unsigned char *uc = ucv;
    u32 *pcp = (u32 *)(uc + 32);
    u32 pc = pcp[0], insn = *(u32 *)pc, op = insn >> 26, addr;
    u8 *p;
    (void)sig; (void)info;
    addr = get_reg(uc, (insn >> 21) & 31) + (u32)(s32)(s16)(insn & 0xFFFF);
    if (addr < 0x80010000u || addr >= 0x800B0000u) { g_trap_error = 1; trap_fail(pc, insn); }
    p = (u8 *)(addr - 0x70000000u);
    switch (op) {
    case 0x20: set_reg(uc, (insn >> 16) & 31, (u32)(s32)*(s8 *)p); break;
    case 0x24: set_reg(uc, (insn >> 16) & 31, *p); break;
    case 0x21: set_reg(uc, (insn >> 16) & 31, (u32)(s32)*(s16 *)p); break;
    case 0x25: set_reg(uc, (insn >> 16) & 31, *(u16 *)p); break;
    case 0x23: set_reg(uc, (insn >> 16) & 31, *(u32 *)p); break;
    default: g_trap_error = 1; trap_fail(pc, insn);
    }
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

/* --------------------------------------------------------------- stubs and recorders */
/* Recorder: sound/stream event func_80047088(event, object) - audio is not part of the port. */
static s32 rec_ev[8]; static int rec_evn;
void func_80047088(s32 ev, void *obj) { (void)obj; if (rec_evn < 8) rec_ev[rec_evn] = ev; rec_evn++; }
/* Recorder: script message dispatcher func_8005908C(code, object, record, 0). */
static s32 rec_msg_code, rec_msg_id, rec_msg_val, rec_msg_kind; static int rec_msgn;
void func_8005908C(s32 code, void *obj, u8 *rec, s32 z)
{
    (void)obj; (void)z;
    if (rec_msgn == 0) {
        rec_msg_code = code; rec_msg_id = *(s32 *)rec; rec_msg_val = *(s16 *)(rec + 4); rec_msg_kind = rec[6];
    }
    rec_msgn++;
}
/* Recorder: model-frame setter func_800175F8(instance, frame) - render side, not ported here. */
static s32 rec_frame; static int rec_framen;
void func_800175F8(void *inst, s32 frame) { (void)inst; rec_frame = frame; rec_framen++; }
/* Fail stubs: paths the vectors must not reach (2P / actor collision / moving platforms /
 * dynamic-object surfaces with a non-empty queue). Reaching one aborts the run. */
s32 func_80052098(void) { die("func_80052098 (actor collision) reached"); return 0; }
s32 func_8001A074(void) { die("func_8001A074 (2P distance) reached"); return 0; }
void func_8001A578(void) { die("func_8001A578 (platform rotation) reached"); }
s32 func_8001A750(void) { die("func_8001A750 (object queue) reached"); return 0; }
void func_80018860(void) { die("func_80018860 (object queue) reached"); }
void func_8001A970(void) { die("func_8001A970 (object queue) reached"); }
/* Allocation / loader side calls (as in level_harness.c). */
static u8 alloc_arena[0x4000];
static int alloc_used;
void *func_80017EBC(s32 size, s32 tag)
{
    void *p = alloc_arena + alloc_used;
    (void)tag;
    alloc_used += (size + 15) & ~15;
    if (alloc_used > (int)sizeof(alloc_arena)) die("alloc arena exhausted");
    return p;
}
void func_800496D0(s32 w, s32 h) { (void)w; (void)h; }
void func_800834C4(s32 w, s32 h) { (void)w; (void)h; }
/* Pad hardware: type of the port (func_8001EF34) and the actuator update (func_8001EC94). */
static s32 g_padtype = 7;
s32 func_8001EF34(void) { return g_padtype; }
void func_8001EC94(u32 port) { (void)port; }

/* ------------------------------------------------------------------------ level state */
#define MAXSECT 64
static u8 blob[0x40000];
static int blob_len;
static u8 *sect_ptr[MAXSECT];
static u32 sect_size[MAXSECT];
static u8 *grdb_ptr; static u32 grdb_size;
static u8 *block_table[0x68];

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
static void load_level(void)
{
    struct { u8 *base; u8 pad[0x1C]; u32 size; } res;
    int i;
    res.base = grdb_ptr; res.size = grdb_size;
    func_80063DC8(&res);
    for (i = 0; i < MAXSECT; i++) if (sect_ptr[i]) func_800639D4(sect_ptr[i], sect_size[i], i);
}

/* ----------------------------------------------------------------- entity images */
static u32 self_w[0x140 / 4] __attribute__((section(".mech_entity"))); /* fixed mapped host address */
static u32 sub_w[0x340 / 4];       /* state block self+0xAC */
static u32 clip_w[64 * 4];         /* AnmD clip table: 16-byte records */
static u32 session_w[0x40];        /* gGameSession */
static u32 hud_w[8];               /* record of func_80051AC8 (sub+0x2DC points here) */
#define SELF ((u8 *)self_w)
#define SUB ((u8 *)sub_w)
static int nclips;

enum { T_U8 = 1, T_S8, T_U16, T_S16, T_S32 };
enum { W_SELF, W_SUB, W_SPECIAL };
struct field { const char *name; int where; int off; int type; };
static const struct field fields[] = {
    /* entity */
    { "cmd0", W_SELF, 0x0C, T_U16 }, { "cmd1", W_SELF, 0x0E, T_U16 }, { "cmd2", W_SELF, 0x10, T_U16 },
    { "cmd3", W_SELF, 0x12, T_U16 }, { "health", W_SELF, 0x1E, T_S16 },
    { "m0", W_SELF, 0x2C, T_S32 }, { "m1", W_SELF, 0x30, T_S32 }, { "m2", W_SELF, 0x34, T_S32 },
    { "m3", W_SELF, 0x38, T_S32 }, { "m4", W_SELF, 0x3C, T_S32 }, { "m5", W_SELF, 0x40, T_S32 },
    { "m6", W_SELF, 0x44, T_S32 }, { "m7", W_SELF, 0x48, T_S32 }, { "m8", W_SELF, 0x4C, T_S32 },
    { "x", W_SELF, 0x50, T_S32 }, { "y", W_SELF, 0x54, T_S32 }, { "z", W_SELF, 0x58, T_S32 },
    { "vx", W_SELF, 0x78, T_S32 }, { "vy", W_SELF, 0x7C, T_S32 }, { "vz", W_SELF, 0x80, T_S32 },
    { "speed", W_SELF, 0x84, T_S32 },
    { "ax", W_SELF, 0xA0, T_S32 }, { "ay", W_SELF, 0xA4, T_S32 }, { "az", W_SELF, 0xA8, T_S32 },
    { "angle", W_SELF, 0xB0, T_U16 }, { "wall", W_SELF, 0xB8, T_U8 }, { "bits", W_SELF, 0xB9, T_U8 },
    { "clip", W_SELF, 0xBA, T_U8 }, { "state", W_SELF, 0xBB, T_U8 }, { "timer", W_SELF, 0xC2, T_U8 },
    { "idle", W_SELF, 0xC3, T_U8 }, { "platform", W_SELF, 0xC4, T_S32 },
    { "telapsed", W_SELF, 0xD0, T_S32 }, { "tframe", W_SELF, 0xD4, T_S16 },
    { "ttarget", W_SELF, 0xD6, T_S16 }, { "tstep", W_SELF, 0xD8, T_S8 }, { "tindex", W_SELF, 0xD9, T_U8 },
    { "tcount", W_SELF, 0xDA, T_U8 }, { "tflags", W_SELF, 0xDB, T_U8 },
    { "lift", W_SELF, 0xF0, T_S16 },
    /* state block */
    { "form", W_SUB, 0xAD, T_S8 }, { "lock", W_SUB, 0xC0, T_S8 }, { "port", W_SUB, 0xC1, T_S8 },
    { "grab", W_SUB, 0xDE, T_U8 }, { "boost", W_SUB, 0xDF, T_U8 },
    { "f0x", W_SUB, 0xEC, T_S16 }, { "f0y", W_SUB, 0xEE, T_S16 }, { "f0z", W_SUB, 0xF0, T_S16 },
    { "f1x", W_SUB, 0xF4, T_S16 }, { "f1y", W_SUB, 0xF6, T_S16 }, { "f1z", W_SUB, 0xF8, T_S16 },
    { "f2x", W_SUB, 0xFC, T_S16 }, { "f2y", W_SUB, 0xFE, T_S16 }, { "f2z", W_SUB, 0x100, T_S16 },
    { "f3x", W_SUB, 0x104, T_S16 }, { "f3y", W_SUB, 0x106, T_S16 }, { "f3z", W_SUB, 0x108, T_S16 },
    { "p0x", W_SUB, 0x10C, T_S32 }, { "p0y", W_SUB, 0x110, T_S32 }, { "p0z", W_SUB, 0x114, T_S32 },
    { "p1x", W_SUB, 0x118, T_S32 }, { "p1y", W_SUB, 0x11C, T_S32 }, { "p1z", W_SUB, 0x120, T_S32 },
    { "p2x", W_SUB, 0x124, T_S32 }, { "p2y", W_SUB, 0x128, T_S32 }, { "p2z", W_SUB, 0x12C, T_S32 },
    { "p3x", W_SUB, 0x130, T_S32 }, { "p3y", W_SUB, 0x134, T_S32 }, { "p3z", W_SUB, 0x138, T_S32 },
    { "hazard", W_SUB, 0x146, T_U8 }, { "slow", W_SUB, 0x147, T_U8 },
    { "ix", W_SUB, 0x150, T_S32 }, { "iy", W_SUB, 0x154, T_S32 }, { "iz", W_SUB, 0x158, T_S32 },
    { "sx", W_SUB, 0x15C, T_S32 }, { "sy", W_SUB, 0x160, T_S32 }, { "sz", W_SUB, 0x164, T_S32 },
    { "nx", W_SUB, 0x168, T_S32 }, { "ny", W_SUB, 0x16C, T_S32 }, { "nz", W_SUB, 0x170, T_S32 },
    { "request", W_SUB, 0x2FD, T_U8 }, { "frozen", W_SUB, 0x310, T_S8 }, { "hit", W_SUB, 0x326, T_S16 },
    /* specials */
    { "tclip", W_SPECIAL, 0, 0 }, { "flags", W_SPECIAL, 1, 0 }, { "steps", W_SPECIAL, 2, 0 },
    { "seed", W_SPECIAL, 3, 0 }, { "padtype", W_SPECIAL, 4, 0 }, { "padx", W_SPECIAL, 5, 0 },
    { "pady", W_SPECIAL, 6, 0 }, { "mode0", W_SPECIAL, 7, 0 }, { "mode1", W_SPECIAL, 8, 0 },
    { "inv0", W_SPECIAL, 9, 0 }, { "inv1", W_SPECIAL, 10, 0 }, { "ret", W_SPECIAL, 11, 0 },
    { "evn", W_SPECIAL, 12, 0 }, { "ev0", W_SPECIAL, 13, 0 }, { "ev1", W_SPECIAL, 14, 0 },
    { "ev2", W_SPECIAL, 15, 0 }, { "ev3", W_SPECIAL, 16, 0 }, { "msgn", W_SPECIAL, 17, 0 },
    { "msgcode", W_SPECIAL, 18, 0 }, { "msgid", W_SPECIAL, 19, 0 }, { "msgval", W_SPECIAL, 20, 0 },
    { "msgkind", W_SPECIAL, 21, 0 }, { "framen", W_SPECIAL, 22, 0 }, { "frame", W_SPECIAL, 23, 0 },
    { "hudr", W_SPECIAL, 24, 0 }, { "hudl", W_SPECIAL, 25, 0 }, { "a0", W_SPECIAL, 26, 0 },
    { "a1", W_SPECIAL, 27, 0 }, { "a2", W_SPECIAL, 28, 0 }, { "a3", W_SPECIAL, 29, 0 },
    { "o0", W_SPECIAL, 30, 0 }, { "o1", W_SPECIAL, 31, 0 }, { "o2", W_SPECIAL, 32, 0 },
    { "selfaddr", W_SPECIAL, 33, 0 },
    { 0, 0, 0, 0 }
};
#define NSPECIAL 34
static s32 special[NSPECIAL];

static s32 get_field(const struct field *f)
{
    u8 *b;
    if (f->where == W_SPECIAL) {
        if (f->off == 0) {
            u32 clip = self_w[0xCC / 4];
            return clip ? (s32)((clip - (u32)clip_w) / 16) : -1;
        }
        return special[f->off];
    }
    b = (f->where == W_SELF ? SELF : SUB) + f->off;
    switch (f->type) {
    case T_U8: return *b;
    case T_S8: return *(s8 *)b;
    case T_U16: return *(u16 *)b;
    case T_S16: return *(s16 *)b;
    default: return *(s32 *)b;
    }
}
static void set_field(const struct field *f, s32 v)
{
    u8 *b;
    if (f->where == W_SPECIAL) {
        if (f->off == 0) { self_w[0xCC / 4] = v < 0 ? 0 : (u32)clip_w + (u32)v * 16; return; }
        /* Bounds are live input/output state, not just recorded output. */
        if (f->off == 24 || f->off == 25)
            *(s16 *)((u8 *)hud_w + (f->off == 24 ? 0x0A : 0x10)) = (s16)v;
        special[f->off] = v;
        return;
    }
    b = (f->where == W_SELF ? SELF : SUB) + f->off;
    switch (f->type) {
    case T_U8: case T_S8: *b = (u8)v; break;
    case T_U16: case T_S16: *(u16 *)b = (u16)v; break;
    default: *(s32 *)b = v;
    }
}
static const struct field *find_field(const char *name)
{
    int i;
    for (i = 0; fields[i].name; i++) if (streq(fields[i].name, name)) return &fields[i];
    return 0;
}

/* Fresh world for one named case. */
static void reset_entity(void)
{
    int i;
    for (i = 0; i < (int)(sizeof(self_w) / 4); i++) self_w[i] = 0;
    for (i = 0; i < (int)(sizeof(sub_w) / 4); i++) sub_w[i] = 0;
    for (i = 0; i < (int)(sizeof(session_w) / 4); i++) session_w[i] = 0;
    for (i = 0; i < 8; i++) hud_w[i] = 0;
    for (i = 0; i < NSPECIAL; i++) special[i] = 0;
    special[3] = 0x457;      /* seed: the value gInitReciprocalTable passes to gSeedPrng */
    special[4] = 7;          /* padtype 7 = analog */
    special[5] = 0x80; special[6] = 0x80;
    self_w[0xAC / 4] = (u32)sub_w;
    self_w[0xC8 / 4] = nclips ? (u32)clip_w : 0;   /* track.clips */
    self_w[0xCC / 4] = nclips ? (u32)clip_w : 0;   /* track.clip = clip 0 */
    *(u32 **)(SUB + 0x2DC) = hud_w;
}
static void prepare_world(void)
{
    int i;
    D_8009FA94 = (u8 *)session_w;
    session_w[0x08 / 4] = (u32)special[7];
    session_w[0x98 / 4] = (u32)special[8];
    D_8009FA8C[0] = (u8)special[9]; D_8009FA8C[1] = (u8)special[10];
    D_8009FA0C = 1;
    D_8009F838 = 0;
    for (i = 0; i < 0x48; i++) D_800A8DC0[i] = 0;
    g_padtype = special[4];
    for (i = 0; i < 2; i++) { D_800A78F0[i * 0x24 + 6] = (u8)special[5]; D_800A78F0[i * 0x24 + 7] = (u8)special[6]; }
    func_800197A8(special[3]);
}
static void reset_records(void)
{
    int i;
    rec_evn = 0; rec_msgn = 0; rec_msg_code = rec_msg_id = rec_msg_val = rec_msg_kind = 0;
    rec_framen = 0; rec_frame = 0;
    for (i = 0; i < 8; i++) rec_ev[i] = 0;
}
static void collect_records(void)
{
    int i;
    special[12] = rec_evn;
    for (i = 0; i < 4; i++) special[13 + i] = rec_ev[i];
    special[17] = rec_msgn; special[18] = rec_msg_code; special[19] = rec_msg_id;
    special[20] = rec_msg_val; special[21] = rec_msg_kind;
    special[22] = rec_framen; special[23] = rec_frame;
    special[24] = *(s16 *)((u8 *)hud_w + 0x0A); special[25] = *(s16 *)((u8 *)hud_w + 0x10);
    /* the entity address: func_800549F0 keeps it in $s1, which func_80067914 can read as a stale
     * best distance (PS1 KSEG0 addresses are negative, this qemu address is positive) */
    special[33] = (s32)self_w;
}

/* 800684AC with a chosen $s1 (the value func_80067914 may read as its stale bestDist). */
static s32 ray_with_s1(s32 *p, s32 *q, s32 *outA, s32 *outB, s32 flag, s32 s1)
{
    static s32 args[6];
    register s32 r __asm__("$2");
    args[0] = (s32)p; args[1] = (s32)q; args[2] = (s32)outA; args[3] = (s32)outB; args[4] = flag; args[5] = s1;
    __asm__ volatile(
        "addiu $sp,$sp,-32\n\t"
        "sw $17,24($sp)\n\t"
        "sw $31,28($sp)\n\t"
        "move $8,%1\n\t"
        "lw $4,0($8)\n\t"
        "lw $5,4($8)\n\t"
        "lw $6,8($8)\n\t"
        "lw $7,12($8)\n\t"
        "lw $9,16($8)\n\t"
        "lw $17,20($8)\n\t"
        "sw $9,16($sp)\n\t"
        "jal func_800684AC\n\t"
        "nop\n\t"
        "lw $17,24($sp)\n\t"
        "lw $31,28($sp)\n\t"
        "addiu $sp,$sp,32\n\t"
        : "=r"(r) : "r"(args)
        : "memory", "$3", "$4", "$5", "$6", "$7", "$8", "$9", "$10", "$11", "$12", "$13", "$14", "$15",
          "$24", "$25");
    return r;
}

/* ------------------------------------------------------------------------- vectors */
#define MAXV 800
static s32 v[MAXV];
static char names[MAXV][12];
static int nin, nout, line_no, failures, ncase, emit, named;
static s32 got[MAXV];
static char gotname[MAXV][12];
static int ngot;
static const char *kinds[] = { "SQRT", "NORM", "NLEN", "LEN2", "SINCOS", "STICK", "PAD", "RAY",
                               "GROUND", "DELTA", "IMPULSE", "SURF", "FOOT", "ANIM", "WALK",
                               "JUMP_INIT", "JUMP_STEP", "JUMP_RANDOM", 0 };
static int per_kind[sizeof(kinds) / sizeof(kinds[0])];
static int jump_active;

static void put(s32 x) { gotname[ngot][0] = 0; got[ngot++] = x; }
static void put_all_fields(void)
{
    int i, j;
    collect_records();
    for (i = 0; fields[i].name; i++) {
        for (j = 0; fields[i].name[j]; j++) gotname[ngot][j] = fields[i].name[j];
        gotname[ngot][j] = 0;
        got[ngot++] = get_field(&fields[i]);
    }
}
static void apply_named_inputs(void)
{
    int i;
    reset_entity();
    for (i = 0; i < nin; i++) {
        const struct field *f = find_field(names[i]);
        if (!f) { say("mech: unknown field "); say(names[i]); say(" on line "); say_dec((u32)line_no); say("\n"); sys4(4001, 2, 0, 0, 0); }
        set_field(f, v[i]);
    }
    prepare_world();
    reset_records();
}

static void set_gp(void) { __asm__ volatile("lui $28,0x1009\n\tori $28,$28,0xF214"); }

static void run_case(const char *kind)
{
    int i;
    ngot = 0;
    set_gp();
    if (streq(kind, "SQRT") && !named && nin == 1) {
        put(func_80095900(v[0]));
    } else if (streq(kind, "NORM") && !named && nin == 3) {
        s32 t[3]; t[0] = v[0]; t[1] = v[1]; t[2] = v[2];
        func_8001A180(t); put(t[0]); put(t[1]); put(t[2]);
    } else if (streq(kind, "NLEN") && !named && nin == 3) {
        s32 t[3], r; t[0] = v[0]; t[1] = v[1]; t[2] = v[2];
        r = func_8001A314(t); put(r); put(t[0]); put(t[1]); put(t[2]);
    } else if (streq(kind, "LEN2") && !named && nin == 2) {
        s32 t[2], r; t[0] = v[0]; t[1] = v[1];
        r = func_8001A7AC(t); put(r); put(t[0]); put(t[1]);
    } else if (streq(kind, "SINCOS") && !named && nin == 1) {
        s32 s = 0x5A5A, c = 0x5A5A;
        func_8001846C((u32)v[0], &s, &c); put(s); put(c);
    } else if (streq(kind, "STICK") && !named && nin == 7) {
        /* padtype x y lock(+0xC0) freeze(+0x310) port flags -> status flags magV magH */
        static u8 ctx[0x320];
        u32 fl = (u32)v[6];
        s32 mv = 0x5A5A, mh = 0x5A5A, st;
        for (i = 0; i < (int)sizeof(ctx); i++) ctx[i] = 0;
        g_padtype = v[0];
        ctx[0xC0] = (u8)v[3]; ctx[0x310] = (u8)v[4]; ctx[0xC1] = (u8)v[5];
        for (i = 0; i < 2; i++) { D_800A78F0[i * 0x24 + 6] = 0; D_800A78F0[i * 0x24 + 7] = 0; }
        D_800A78F0[(v[5] & 1) * 0x24 + 6] = (u8)v[1]; D_800A78F0[(v[5] & 1) * 0x24 + 7] = (u8)v[2];
        st = func_800538C8(ctx, &fl, &mv, &mh);
        put(st); put((s32)fl); put(mv); put(mh);
    } else if (streq(kind, "PAD") && !named && nin == 3) {
        /* buttons0 buttons1 players -> mapped0 mapped1 translated0 translated1
         * buttons = the 16 PS1 digital buttons, 1 = pressed (bit 4 up, 5 right, 6 down, 7 left,
         * 8 L2, 9 R2, 10 L1, 11 R1, 12 triangle, 13 circle, 14 cross, 15 square, 0 select, 3 start) */
        u32 t0 = 0x5A5A, t1 = 0x5A5A;
        g_padtype = 7;
        func_8001E588(0); func_8001E588(1);
        *(u32 *)(D_800A78F0 + 0) = ~((u32)v[0] << 16);
        *(u32 *)(D_800A78F0 + 0x24) = ~((u32)v[1] << 16);
        D_8009FA0C = v[2];
        func_80057864();
        put((s32)D_8009F7C0[0]); put((s32)D_8009F7C0[1]);
        func_800576A8(&t0, 0); func_800576A8(&t1, 1);
        put((s32)t0); put((s32)t1);
        D_8009FA0C = 1;
    } else if (streq(kind, "RAY") && !named && nin == 8) {
        /* px py pz qx qy qz flag s1 -> result outA0 outA1 outA2 outB */
        s32 p[3], q[3], oa[3], ob = 0x5A5A5A5A, r;
        p[0] = v[0]; p[1] = v[1]; p[2] = v[2]; q[0] = v[3]; q[1] = v[4]; q[2] = v[5];
        oa[0] = oa[1] = oa[2] = 0x5A5A5A5A;
        r = ray_with_s1(p, q, oa, &ob, v[6], v[7]);
        put(r); put(oa[0]); put(oa[1]); put(oa[2]); put(ob);
    } else if (streq(kind, "GROUND") && !named && nin == 3) {
        /* x y z -> height nx ny nz attr surface (func_80066F78 with every output) */
        s32 pos[3], n[3], attr = 0x5A5A, surf = 0x5A5A, h;
        pos[0] = v[0]; pos[1] = v[1]; pos[2] = v[2];
        n[0] = n[1] = n[2] = 0x5A5A5A5A;
        D_8009F838 = 0;
        h = func_80066F78(pos, n, &attr, &surf);
        put(h); put(n[0]); put(n[1]); put(n[2]); put(attr); put(surf);
    } else if (named && streq(kind, "DELTA")) {
        /* a0 speed, a1 lane, a2 direction -> o0..o2 the delta */
        s32 out[3];
        apply_named_inputs();
        out[0] = out[1] = out[2] = 0x5A5A5A5A;
        func_80054590(SELF, special[26], special[27], out, special[28]);
        special[30] = out[0]; special[31] = out[1]; special[32] = out[2];
        put_all_fields();
    } else if (named && streq(kind, "IMPULSE")) {
        /* a0..a2 the vector advanced in place -> o0..o2, ret the return value */
        s32 vec[3];
        apply_named_inputs();
        vec[0] = special[26]; vec[1] = special[27]; vec[2] = special[28];
        special[11] = func_80052550(SELF, vec);
        special[30] = vec[0]; special[31] = vec[1]; special[32] = vec[2];
        put_all_fields();
    } else if (named && streq(kind, "SURF")) {
        apply_named_inputs();
        func_80053188(SELF, (u32)special[26]);
        put_all_fields();
    } else if (named && streq(kind, "FOOT")) {
        apply_named_inputs();
        func_80052708(SELF, special[26]);
        put_all_fields();
    } else if (named && streq(kind, "ANIM")) {
        /* func_80044B34(self as script, &track, a0 speed, a1 direction) -> ret */
        apply_named_inputs();
        special[11] = (s32)func_80044B34(SELF, SELF + 0xC8, special[26], special[27]);
        put_all_fields();
    } else if (named && streq(kind, "WALK")) {
        apply_named_inputs();
        g_walker_probe = 1;
        special[11] = func_800549F0(SELF, 0, special[2], (u32)special[1]);
        g_walker_probe = 0;
        put_all_fields();
    } else if (named && streq(kind, "JUMP_INIT")) {
        if (jump_active) die("missing JUMP_RANDOM before next JUMP_INIT");
        apply_named_inputs();
        func_8001E588(0); func_8001E588(1);
        jump_active = 1;
        put_all_fields();
    } else if (jump_active && streq(kind, "JUMP_STEP") && !named && nin == 2) {
        static const char *observed[] = {
            "x", "y", "z", "sy", "state", "bits", "timer", "tclip", "tframe", "telapsed",
            "ret", "evn", "ev0", "hudr", "hudl"
        };
        u32 flags = 0;
        reset_records();
        *(u32 *)(D_800A78F0 + 0) = ~((u32)v[0] << 16);
        *(u32 *)(D_800A78F0 + 0x24) = ~0u;
        func_80057864();
        func_800576A8(&flags, 0);
        g_walker_probe = 1;
        special[11] = func_800549F0(SELF, 0, v[1], flags);
        g_walker_probe = 0;
        collect_records();
        put((s32)flags);
        for (i = 0; i < (int)(sizeof(observed) / sizeof(observed[0])); i++)
            put(get_field(find_field(observed[i])));
    } else if (jump_active && streq(kind, "JUMP_RANDOM") && !named && nin == 1 && v[0] == 3) {
        for (i = 0; i < v[0]; i++) put(func_80019668());
        jump_active = 0;
    } else {
        say("mech: malformed vector line "); say_dec((u32)line_no); say("\n");
        sys4(4001, 2, 0, 0, 0);
    }
    for (i = 0; kinds[i]; i++) if (streq(kind, kinds[i])) per_kind[i]++;
    ncase++;
    if (emit) {
        say(kind);
        for (i = 0; i < nin; i++) {
            say(" ");
            if (named) { say(names[i]); say("="); }
            say_sdec(v[i]);
        }
        say(" ->");
        for (i = 0; i < ngot; i++) {
            if (named) { say(" "); say(gotname[i]); say("="); say_sdec(got[i]); }
            else say_hex((u32)got[i]);
        }
        say("\n");
        return;
    }
    if (nout != ngot) {
        say("mech: FAIL line "); say_dec((u32)line_no); say(" output count "); say_dec((u32)nout);
        say(" want "); say_dec((u32)ngot); say("\n");
        failures++;
        return;
    }
    for (i = 0; i < ngot; i++) {
        if (named && !streq(names[nin + i], gotname[i])) {
            say("mech: FAIL line "); say_dec((u32)line_no); say(" field order "); say(names[nin + i]);
            say(" vs "); say(gotname[i]); say("\n");
            failures++;
            return;
        }
        if ((u32)got[i] != (u32)v[nin + i]) {
            say("mech: FAIL "); say(kind); say(" line "); say_dec((u32)line_no); say(" field ");
            if (named) say(gotname[i]); else say_dec((u32)i);
            say(" got"); say_hex((u32)got[i]); say(" want"); say_hex((u32)v[nin + i]); say("\n");
            failures++;
        }
    }
}

int main(int argc, char **argv)
{
    static char data[0x800000];
    static u8 rom[0x90100];
    int n, i, k, neg, f0, sep, romlen;
    char *p, kind[16];
    if (argc < 4) { say("mech: usage harness vectors blob rom [--emit]\n"); return 2; }
    emit = argc > 4 && streq(argv[4], "--emit");
    if (install(4, on_sigill) < 0 || install(10, on_sigsegv) < 0 || install(11, on_sigsegv) < 0) {
        say("mech: sigaction failed\n"); return 2;
    }
    {
        u32 address = (u32)func_80067914 + 32;
        if (install(5, segment_break) < 0) die("SIGTRAP setup failed");
        if (*(u32 *)address != 0xAFB10024u) die("unexpected segment s1 save");
        if (sys4(4125, address & ~4095u, 4096, 7, 0) < 0) die("mprotect failed");
        *(u32 *)address = 0x0000000Du; /* MIPS break; handler executes original store */
        sys4(4147, address, 4, 3, 0); /* cacheflush */
    }
    /* the whole retail executable image at its mapped address (text, data, ROM tables) */
    romlen = read_file(argv[3], rom, sizeof(rom));
    if (romlen != 0x800 + 0x8F800) { say("mech: cannot read retail executable\n"); return 2; }
    for (i = 0; i < 0x8F800; i++) D_RAM[i] = rom[0x800 + i];
    blob_len = read_file(argv[2], blob, sizeof(blob));
    if (blob_len <= 0 || !parse_blob()) { say("mech: cannot read payload blob\n"); return 2; }
    set_gp();
    D_8009F808 = block_table;
    func_80019828();               /* gInitReciprocalTable: D_8009F5B0 (needs $gp) */
    load_level();
    n = read_file(argv[1], (u8 *)data, sizeof(data) - 1);
    if (n <= 0) { say("mech: cannot read vectors\n"); return 2; }
    data[n] = 0; p = data; line_no = 1;
    while (*p) {
        if (*p == '\n') { line_no++; p++; continue; }
        if (*p == ' ' || *p == '\t' || *p == '\r') { p++; continue; }
        if (*p == '#') { while (*p && *p != '\n') p++; continue; }
        for (i = 0; i < 15 && *p > ' '; i++) kind[i] = *p++;
        kind[i] = 0;
        if (streq(kind, "MIS")) {
            char *q = p;
            while (*p && *p != '\n') p++;
            if (emit) { char c = *p; *p = 0; say("MIS"); say(q); say("\n"); *p = c; }
            continue;
        }
        k = 0; sep = -1; named = 0;
        for (;;) {
            u32 x = 0; int digits = 0;
            while (*p == ' ' || *p == '\t' || *p == '\r') p++;
            if (!*p || *p == '\n' || *p == '#') break;
            if (p[0] == '-' && p[1] == '>') { sep = k; p += 2; continue; }
            names[k][0] = 0;
            if ((*p >= 'a' && *p <= 'z') || (*p >= 'A' && *p <= 'Z')) {
                int j = 0;
                while (*p && *p != '=' && *p > ' ' && j < 11) names[k][j++] = *p++;
                names[k][j] = 0;
                if (*p != '=') { say("mech: bad name on line "); say_dec((u32)line_no); say("\n"); return 2; }
                p++;
                named = 1;
            }
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
                say("mech: bad number on line "); say_dec((u32)line_no); say("\n"); return 2;
            }
            v[k++] = neg ? -(s32)x : (s32)x;
        }
        if (streq(kind, "CLIPS")) {
            /* CLIPS next0 type mode flags start end next1 next2 code duration (x count) */
            if (k % 10) { say("mech: CLIPS needs 10 values per clip\n"); return 2; }
            nclips = k / 10;
            for (i = 0; i < nclips; i++) {
                u8 *c = (u8 *)clip_w + i * 16;
                c[0] = (u8)v[i * 10]; c[1] = (u8)v[i * 10 + 1]; c[2] = (u8)v[i * 10 + 2]; c[3] = (u8)v[i * 10 + 3];
                *(s16 *)(c + 4) = (s16)v[i * 10 + 4]; *(s16 *)(c + 6) = (s16)v[i * 10 + 5];
                c[8] = (u8)v[i * 10 + 6]; c[9] = (u8)v[i * 10 + 7];
                *(u16 *)(c + 10) = (u16)v[i * 10 + 8]; *(s32 *)(c + 12) = v[i * 10 + 9];
            }
            if (emit) { say("CLIPS"); for (i = 0; i < k; i++) { say(" "); say_sdec(v[i]); } say("\n"); }
            continue;
        }
        if (sep < 0 && !emit) { say("mech: missing '->' on line "); say_dec((u32)line_no); say("\n"); return 2; }
        nin = sep < 0 ? k : sep;
        nout = k - nin;
        run_case(kind);
    }
    if (jump_active) die("missing JUMP_RANDOM at end of sequence");
    if (emit) return g_trap_error ? 1 : 0;
    for (i = 0; kinds[i]; i++) { say("mech: "); say(kinds[i]); say(" "); say_dec((u32)per_kind[i]); say(" cases\n"); }
    if (!ncase || failures || g_trap_error) { say("mech: FAIL ("); say_dec((u32)failures); say(" mismatches)\n"); return 1; }
    say("mech: all "); say_dec((u32)ncase);
    say(" vectors PASS (unchanged decomp C + retail GTE asm words, cop2 via software-GTE trap)\n");
    return 0;
}
