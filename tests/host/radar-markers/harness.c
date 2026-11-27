/* Executes unchanged PS1 C radar marker units against tests/golden/radar-markers.vectors:
 *   func_8004CAE4 (gInitMarkerColorsAndPools, 8004CAE4.c) -> D_800A6F60[0..31]
 *   func_8005A740 (gEmitAngledLine,          8005A740.c) -> LINE_F2 packets
 *   func_8005A828 (gEmitRadarTriangle,       8005A828.c) -> POLY_F3 packets (using D_8009AFB4)
 *   func_8005A934 (gCheckRadarTargetBearing, 8005A934.c) -> bearing + dead-zone gate
 *   func_8005A9C4 (gDrawRadarMarkers,        8005A9C4.c) -> full radar marker pass
 *   func_8004CC68 (gUpdateMarkerPulseColors, 8004CC68.c) -> D_800A6F60[17..19] per frame
 *   func_8001846C (gSinCos,                  8001846C.c) -> ROM table D_8009A234
 *   func_80095C30 (gAtan2,                   80095C30.c) -> ROM table D_8009E0A0
 *
 * All D_800xxxxx symbols are relocated by -0x70000000 into .retailram at 0x10010000,
 * seeded with the retail SLUS_007.39 payload (0x80010000..0x8009F7FF) + zeroed BSS.
 */
#include "game/types.h"

#define EXPECTED_CASES 183
#define PKT_STRIDE 0x1C
#define MAX_PKTS 16

/* Struct layouts matching 8005A934.c and 8005A9C4.c */
struct Node5A9C4 {
    u8 unk00[0xA4];
    u32 unkA4;
    u8 flags[8];
    struct {
        s16 a;
        s16 b;
    } pos[8];
};

struct Ctl5A9C4 {
    u8 unk00[0xC1];
    s8 unkC1;
    u8 unkC2[0x300 - 0xC2];
    struct Node5A9C4 *unk300;
};

struct Obj5A9C4 {
    u8 unk00[0x50];
    s32 x;
    s32 y;
    s32 z;
    u8 unk5C[0x50];
    struct Ctl5A9C4 *unkAC;
    s16 heading;
    s16 unkB2;
};

struct Other5A9C4 {
    u8 unk00[0x50];
    s32 x;
    u8 unk54[4];
    s32 z;
};

struct Session5A9C4 {
    u8 unk00[0x0C];
    s32 mode0C;
    u8 unk10[0x4C];
    s32 mode5C;
};

struct LineF2 {
    u32 tag;
    u32 code;
    s16 x0;
    s16 y0;
    s16 x1;
    s16 y1;
};

struct LineF3 {
    u32 tag;
    u32 code;
    s16 xy[6];
};

extern u8 *gPacketCursor __asm__("D_8009FA90");
extern u8 *gPacketLimit __asm__("D_8009F9C0");
extern s32 gPlayerCount __asm__("D_8009FA0C");
extern struct Session5A9C4 *gGameSession __asm__("D_8009FA94");
extern struct Other5A9C4 *gPlayerEntities[2] __asm__("D_8009FA58");
extern s32 gFrameTime __asm__("D_8009FAA4");
extern u32 gMarkerColorTable[32] __asm__("D_800A6F60");

extern void gInitMarkerColorsAndPools(void) __asm__("func_8004CAE4");
extern void gEmitAngledLine(s32 origin, s32 angle) __asm__("func_8005A740");
extern void gEmitRadarTriangle(s32 origin, u32 angle, s32 code) __asm__("func_8005A828");
extern s32 gCheckRadarTargetBearing(void *object, s32 z, s32 x, s32 *angle) __asm__("func_8005A934");
extern void gDrawRadarMarkers(void *object) __asm__("func_8005A9C4");
extern void gUpdateMarkerPulseColors(void) __asm__("func_8004CC68");

/* Linux MIPS o32 syscalls */
static s32 sys_read(s32 fd, void *buf, s32 len)
{
    register s32 a0 __asm__("$4") = fd;
    register s32 a1 __asm__("$5") = (s32)buf;
    register s32 a2 __asm__("$6") = len;
    register s32 v0 __asm__("$2") = 4003;
    register s32 a3 __asm__("$7");
    __asm__ volatile("syscall" : "+r"(v0), "=r"(a3) : "r"(a0), "r"(a1), "r"(a2) : "memory");
    return a3 ? -v0 : v0;
}

static s32 sys_write(s32 fd, const void *buf, s32 len)
{
    register s32 a0 __asm__("$4") = fd;
    register s32 a1 __asm__("$5") = (s32)buf;
    register s32 a2 __asm__("$6") = len;
    register s32 v0 __asm__("$2") = 4004;
    register s32 a3 __asm__("$7");
    __asm__ volatile("syscall" : "+r"(v0), "=r"(a3) : "r"(a0), "r"(a1), "r"(a2) : "memory");
    return a3 ? -v0 : v0;
}

static void put_fd(s32 fd, const char *s)
{
    s32 n = 0;
    while (s[n]) n++;
    sys_write(fd, s, n);
}

static void put_n(s32 fd, const char *s, s32 n)
{
    sys_write(fd, s, n);
}

static void put_int(s32 fd, s32 v)
{
    char b[16];
    s32 i = 15;
    u32 u;
    b[i] = 0;
    if (v < 0) {
        put_fd(fd, "-");
        u = (u32)(-(v + 1)) + 1u;
    } else {
        u = (u32)v;
    }
    if (u == 0) {
        b[--i] = '0';
    }
    while (u > 0) {
        b[--i] = (char)('0' + (u % 10));
        u /= 10;
    }
    put_fd(fd, &b[i]);
}

static void put_hex8(s32 fd, u32 v)
{
    static const char hex[] = "0123456789ABCDEF";
    char b[9];
    s32 i;
    for (i = 7; i >= 0; i--) {
        b[i] = hex[v & 0xFu];
        v >>= 4;
    }
    b[8] = 0;
    put_fd(fd, b);
}

static void skip_ws(const char **pp)
{
    const char *p = *pp;
    while (*p == ' ' || *p == '\t') p++;
    *pp = p;
}

static s32 parse_int(const char **pp)
{
    const char *p;
    s32 neg = 0;
    u32 v = 0;
    skip_ws(pp);
    p = *pp;
    if (*p == '-') { neg = 1; p++; }
    if (p[0] == '0' && (p[1] == 'x' || p[1] == 'X')) {
        p += 2;
        while (1) {
            if (*p >= '0' && *p <= '9') v = (v << 4) | (u32)(*p - '0');
            else if (*p >= 'a' && *p <= 'f') v = (v << 4) | (u32)(*p - 'a' + 10);
            else if (*p >= 'A' && *p <= 'F') v = (v << 4) | (u32)(*p - 'A' + 10);
            else break;
            p++;
        }
    } else {
        while (*p >= '0' && *p <= '9') {
            v = v * 10u + (u32)(*p - '0');
            p++;
        }
    }
    *pp = p;
    return neg ? (s32)(0u - v) : (s32)v;
}

static u32 parse_hex(const char **pp)
{
    const char *p;
    u32 v = 0;
    skip_ws(pp);
    p = *pp;
    if (p[0] == '0' && (p[1] == 'x' || p[1] == 'X')) p += 2;
    while (1) {
        if (*p >= '0' && *p <= '9') v = (v << 4) | (u32)(*p - '0');
        else if (*p >= 'a' && *p <= 'f') v = (v << 4) | (u32)(*p - 'a' + 10);
        else if (*p >= 'A' && *p <= 'F') v = (v << 4) | (u32)(*p - 'A' + 10);
        else break;
        p++;
    }
    *pp = p;
    return v;
}

static s32 read_token(const char **pp, const char **out_start)
{
    const char *p;
    s32 len = 0;
    skip_ws(pp);
    p = *pp;
    *out_start = p;
    while (*p && *p != ' ' && *p != '\t' && *p != '\r' && *p != '\n') {
        p++;
        len++;
    }
    *pp = p;
    return len;
}

static s32 tok_eq(const char *s, s32 len, const char *lit)
{
    s32 i = 0;
    while (i < len && lit[i]) {
        if (s[i] != lit[i]) return 0;
        i++;
    }
    return i == len && lit[i] == 0;
}

static void mem_zero(void *dst, s32 n)
{
    u8 *p = (u8 *)dst;
    s32 i;
    for (i = 0; i < n; i++) p[i] = 0;
}

static char s_inbuf[131072];
static u8 s_packets[MAX_PKTS * PKT_STRIDE];
static struct Obj5A9C4 s_obj;
static struct Ctl5A9C4 s_ctl;
static struct Node5A9C4 s_node;
static struct Other5A9C4 s_other0;
static struct Other5A9C4 s_other1;
static struct Session5A9C4 s_session;

static void reset_packets(void)
{
    mem_zero(s_packets, sizeof(s_packets));
    gPacketCursor = s_packets;
    gPacketLimit = s_packets + sizeof(s_packets);
}

static void init_colors(void)
{
    mem_zero(&s_session, sizeof(s_session));
    gGameSession = &s_session;
    gInitMarkerColorsAndPools();
}

/* Seed D_8009FAA4 the way the retail frame loop does (8002703C.c:69,341-352) and let the
 * unmodified func_8004CC68 (8004CC68.c:32-48) pulse D_800A6F60[17..19]. */
static void run_marker_pulse(s32 frame_time)
{
    gFrameTime = frame_time;
    gUpdateMarkerPulseColors();
}

void __main(void) {}

s32 main(s32 argc, char **argv)
{
    s32 print_mode = 0;
    s32 n = 0, r;
    const char *p;
    s32 cases = 0;

    if (argc > 1 && argv[1][0] == 'p' && argv[1][1] == 'r' && argv[1][2] == 'i'
        && argv[1][3] == 'n' && argv[1][4] == 't' && argv[1][5] == 0) {
        print_mode = 1;
    }

    while (n < (s32)sizeof(s_inbuf) - 1) {
        r = sys_read(0, s_inbuf + n, (s32)sizeof(s_inbuf) - 1 - n);
        if (r <= 0) break;
        n += r;
    }
    s_inbuf[n] = 0;

    /* Run unmodified func_8004CAE4 once to populate D_800A6F60[0..31]. */
    init_colors();

    p = s_inbuf;
    while (*p) {
        const char *line_start = p;
        const char *kind;
        s32 kind_len;

        while (*p == ' ' || *p == '\t' || *p == '\r' || *p == '\n') p++;
        if (!*p) break;
        if (*p == '#') {
            if (print_mode) {
                const char *eol = p;
                while (*eol && *eol != '\n') eol++;
                if (*eol == '\n') eol++;
                put_n(1, p, (s32)(eol - p));
            }
            while (*p && *p != '\n') p++;
            continue;
        }
        line_start = p;
        kind_len = read_token(&p, &kind);

        if (tok_eq(kind, kind_len, "PULSE")) {
            /* PULSE name frameTime(dec) -> unk44(hex) unk48(hex) unk4C(hex): D_800A6F60[17..19]
             * after the unmodified func_8004CC68 (8004CC68.c:45-47) ran with D_8009FAA4 = frameTime. */
            const char *name;
            s32 name_len = read_token(&p, &name);
            s32 frame_time = parse_int(&p);
            const char *input_end = p;
            u32 got[3];
            s32 i;

            run_marker_pulse(frame_time);
            got[0] = gMarkerColorTable[17];
            got[1] = gMarkerColorTable[18];
            got[2] = gMarkerColorTable[19];

            if (print_mode) {
                put_n(1, line_start, (s32)(input_end - line_start));
                put_fd(1, " -> ");
                for (i = 0; i < 3; i++) {
                    if (i) put_fd(1, " ");
                    put_hex8(1, got[i]);
                }
                put_fd(1, "\n");
            } else {
                u32 exp[3];
                skip_ws(&p);
                if (p[0] == '-' && p[1] == '>') p += 2;
                for (i = 0; i < 3; i++) exp[i] = parse_hex(&p);
                if (got[0] != exp[0] || got[1] != exp[1] || got[2] != exp[2]) {
                    put_fd(2, "PULSE ");
                    put_n(2, name, name_len);
                    put_fd(2, " (D_8009FAA4="); put_int(2, frame_time);
                    put_fd(2, "): expected "); put_hex8(2, exp[0]);
                    put_fd(2, " "); put_hex8(2, exp[1]);
                    put_fd(2, " "); put_hex8(2, exp[2]);
                    put_fd(2, " got "); put_hex8(2, got[0]);
                    put_fd(2, " "); put_hex8(2, got[1]);
                    put_fd(2, " "); put_hex8(2, got[2]);
                    put_fd(2, " (D_800A6F60[17..19])\n");
                    return 1;
                }
            }
            cases++;
        } else if (tok_eq(kind, kind_len, "COLOR")) {
            s32 idx = parse_int(&p);
            const char *arrow_pos = p;
            u32 got, exp;
            if (idx < 0 || idx >= 32) { put_fd(2, "COLOR idx out of range\n"); return 1; }
            got = gMarkerColorTable[idx];
            if (print_mode) {
                put_n(1, line_start, (s32)(arrow_pos - line_start));
                put_fd(1, " -> ");
                put_hex8(1, got);
                put_fd(1, "\n");
            } else {
                skip_ws(&p);
                if (p[0] == '-' && p[1] == '>') p += 2;
                exp = parse_hex(&p);
                if (got != exp) {
                    put_fd(2, "COLOR "); put_int(2, idx);
                    put_fd(2, ": expected "); put_hex8(2, exp);
                    put_fd(2, " got "); put_hex8(2, got);
                    put_fd(2, "\n");
                    return 1;
                }
            }
            cases++;
        } else if (tok_eq(kind, kind_len, "LINE")) {
            const char *name;
            s32 name_len = read_token(&p, &name);
            s32 origin = (s32)parse_hex(&p);
            s32 angle = parse_int(&p);
            const char *input_end = p;
            struct LineF2 *pkt;

            reset_packets();
            gEmitAngledLine(origin, angle);
            pkt = (struct LineF2 *)s_packets;

            if (print_mode) {
                put_n(1, line_start, (s32)(input_end - line_start));
                put_fd(1, " -> ");
                put_hex8(1, pkt->tag); put_fd(1, " ");
                put_hex8(1, pkt->code); put_fd(1, " ");
                put_int(1, pkt->x0); put_fd(1, " ");
                put_int(1, pkt->y0); put_fd(1, " ");
                put_int(1, pkt->x1); put_fd(1, " ");
                put_int(1, pkt->y1); put_fd(1, "\n");
            } else {
                u32 etag, ecode;
                s32 ex0, ey0, ex1, ey1;
                skip_ws(&p);
                if (p[0] == '-' && p[1] == '>') p += 2;
                etag = parse_hex(&p);
                ecode = parse_hex(&p);
                ex0 = parse_int(&p); ey0 = parse_int(&p);
                ex1 = parse_int(&p); ey1 = parse_int(&p);
                if (gPacketCursor != s_packets + PKT_STRIDE
                    || pkt->tag != etag || pkt->code != ecode
                    || pkt->x0 != ex0 || pkt->y0 != ey0
                    || pkt->x1 != ex1 || pkt->y1 != ey1) {
                    put_fd(2, "LINE "); put_n(2, name, name_len);
                    put_fd(2, " mismatch: got ");
                    put_hex8(2, pkt->tag); put_fd(2, " ");
                    put_hex8(2, pkt->code); put_fd(2, " ");
                    put_int(2, pkt->x0); put_fd(2, " ");
                    put_int(2, pkt->y0); put_fd(2, " ");
                    put_int(2, pkt->x1); put_fd(2, " ");
                    put_int(2, pkt->y1); put_fd(2, "\n");
                    return 1;
                }
            }
            cases++;
        } else if (tok_eq(kind, kind_len, "TRI")) {
            const char *name;
            s32 name_len = read_token(&p, &name);
            s32 origin = (s32)parse_hex(&p);
            u32 angle = (u32)parse_int(&p);
            s32 code = (s32)parse_hex(&p);
            const char *input_end = p;
            struct LineF3 *pkt;

            reset_packets();
            gEmitRadarTriangle(origin, angle, code);
            pkt = (struct LineF3 *)s_packets;

            if (print_mode) {
                put_n(1, line_start, (s32)(input_end - line_start));
                put_fd(1, " -> ");
                put_hex8(1, pkt->tag); put_fd(1, " ");
                put_hex8(1, pkt->code); put_fd(1, " ");
                put_int(1, pkt->xy[0]); put_fd(1, " ");
                put_int(1, pkt->xy[1]); put_fd(1, " ");
                put_int(1, pkt->xy[2]); put_fd(1, " ");
                put_int(1, pkt->xy[3]); put_fd(1, " ");
                put_int(1, pkt->xy[4]); put_fd(1, " ");
                put_int(1, pkt->xy[5]); put_fd(1, "\n");
            } else {
                u32 etag, ecode;
                s32 exy[6], i;
                skip_ws(&p);
                if (p[0] == '-' && p[1] == '>') p += 2;
                etag = parse_hex(&p);
                ecode = parse_hex(&p);
                for (i = 0; i < 6; i++) exy[i] = parse_int(&p);
                if (gPacketCursor != s_packets + PKT_STRIDE
                    || pkt->tag != etag || pkt->code != ecode
                    || pkt->xy[0] != exy[0] || pkt->xy[1] != exy[1]
                    || pkt->xy[2] != exy[2] || pkt->xy[3] != exy[3]
                    || pkt->xy[4] != exy[4] || pkt->xy[5] != exy[5]) {
                    put_fd(2, "TRI "); put_n(2, name, name_len);
                    put_fd(2, " mismatch\n");
                    return 1;
                }
            }
            cases++;
        } else if (tok_eq(kind, kind_len, "BEARING")) {
            const char *name;
            s32 name_len = read_token(&p, &name);
            s32 refX = parse_int(&p);
            s32 refZ = parse_int(&p);
            s32 heading = parse_int(&p);
            s32 targetZ = parse_int(&p);
            s32 targetX = parse_int(&p);
            s32 initAngle = parse_int(&p);
            const char *input_end = p;
            s32 angle = initAngle;
            s32 shown;

            mem_zero(&s_obj, sizeof(s_obj));
            s_obj.x = refX;
            s_obj.z = refZ;
            s_obj.heading = (s16)heading;
            shown = gCheckRadarTargetBearing(&s_obj, targetZ, targetX, &angle);

            if (print_mode) {
                put_n(1, line_start, (s32)(input_end - line_start));
                put_fd(1, " -> ");
                put_int(1, shown); put_fd(1, " ");
                put_int(1, angle); put_fd(1, "\n");
            } else {
                s32 eshown, eangle;
                skip_ws(&p);
                if (p[0] == '-' && p[1] == '>') p += 2;
                eshown = parse_int(&p);
                eangle = parse_int(&p);
                if (shown != eshown || angle != eangle) {
                    put_fd(2, "BEARING "); put_n(2, name, name_len);
                    put_fd(2, ": expected "); put_int(2, eshown); put_fd(2, " "); put_int(2, eangle);
                    put_fd(2, " got "); put_int(2, shown); put_fd(2, " "); put_int(2, angle);
                    put_fd(2, "\n");
                    return 1;
                }
            }
            cases++;
        } else if (tok_eq(kind, kind_len, "DRAW")) {
            const char *name;
            s32 name_len = read_token(&p, &name);
            s32 refX = parse_int(&p);
            s32 refZ = parse_int(&p);
            s32 heading = parse_int(&p);
            s32 side = parse_int(&p);
            s32 frameTime = parse_int(&p);
            s32 playerCount = parse_int(&p);
            s32 sessionMode = parse_int(&p);
            s32 otherX = parse_int(&p);
            s32 otherZ = parse_int(&p);
            s32 hasNode = parse_int(&p);
            u32 mask = parse_hex(&p);
            s32 slotCount = parse_int(&p);
            s32 i, count;
            const char *input_end;
            s32 saved_frame_time = gFrameTime;

            mem_zero(&s_obj, sizeof(s_obj));
            mem_zero(&s_ctl, sizeof(s_ctl));
            mem_zero(&s_node, sizeof(s_node));
            mem_zero(&s_other0, sizeof(s_other0));
            mem_zero(&s_other1, sizeof(s_other1));
            mem_zero(&s_session, sizeof(s_session));

            s_node.unkA4 = mask;
            for (i = 0; i < slotCount; i++) {
                u32 fl = parse_hex(&p);
                s32 pa = parse_int(&p);
                s32 pb = parse_int(&p);
                if (i < 8) {
                    s_node.flags[i] = (u8)fl;
                    s_node.pos[i].a = (s16)pa;
                    s_node.pos[i].b = (s16)pb;
                }
            }
            input_end = p;

            s_ctl.unkC1 = (s8)side;
            s_ctl.unk300 = hasNode ? &s_node : (struct Node5A9C4 *)0;
            s_obj.x = refX;
            s_obj.z = refZ;
            s_obj.unkAC = &s_ctl;
            s_obj.heading = (s16)heading;

            s_other0.x = otherX;
            s_other0.z = otherZ;
            s_other1.x = otherX;
            s_other1.z = otherZ;
            gPlayerEntities[0] = &s_other0;
            gPlayerEntities[1] = &s_other1;
            s_session.mode5C = sessionMode;
            gGameSession = &s_session;
            gPlayerCount = playerCount;
            gFrameTime = frameTime;

            reset_packets();
            gDrawRadarMarkers(&s_obj);
            count = (s32)((gPacketCursor - s_packets) / PKT_STRIDE);

            if (print_mode) {
                put_n(1, line_start, (s32)(input_end - line_start));
                put_fd(1, " -> ");
                put_int(1, count);
                for (i = 0; i < count; i++) {
                    u8 *slot = s_packets + i * PKT_STRIDE;
                    u32 tag = *(u32 *)(slot + 0);
                    u32 code = *(u32 *)(slot + 4);
                    if ((code >> 24) == 0x40u) {
                        struct LineF2 *ln = (struct LineF2 *)slot;
                        put_fd(1, " L ");
                        put_hex8(1, tag); put_fd(1, " ");
                        put_hex8(1, code); put_fd(1, " ");
                        put_int(1, ln->x0); put_fd(1, " ");
                        put_int(1, ln->y0); put_fd(1, " ");
                        put_int(1, ln->x1); put_fd(1, " ");
                        put_int(1, ln->y1);
                    } else {
                        struct LineF3 *tr = (struct LineF3 *)slot;
                        put_fd(1, " T ");
                        put_hex8(1, tag); put_fd(1, " ");
                        put_hex8(1, code); put_fd(1, " ");
                        put_int(1, tr->xy[0]); put_fd(1, " ");
                        put_int(1, tr->xy[1]); put_fd(1, " ");
                        put_int(1, tr->xy[2]); put_fd(1, " ");
                        put_int(1, tr->xy[3]); put_fd(1, " ");
                        put_int(1, tr->xy[4]); put_fd(1, " ");
                        put_int(1, tr->xy[5]);
                    }
                }
                put_fd(1, "\n");
            } else {
                s32 ecount;
                skip_ws(&p);
                if (p[0] == '-' && p[1] == '>') p += 2;
                ecount = parse_int(&p);
                if (count != ecount) {
                    put_fd(2, "DRAW "); put_n(2, name, name_len);
                    put_fd(2, ": expected pkt count "); put_int(2, ecount);
                    put_fd(2, " got "); put_int(2, count);
                    put_fd(2, "\n");
                    return 1;
                }
                for (i = 0; i < count; i++) {
                    const char *ptok;
                    s32 plen = read_token(&p, &ptok);
                    u32 etag = parse_hex(&p);
                    u32 ecode = parse_hex(&p);
                    u8 *slot = s_packets + i * PKT_STRIDE;
                    if (tok_eq(ptok, plen, "L")) {
                        struct LineF2 *ln = (struct LineF2 *)slot;
                        s32 ex0 = parse_int(&p), ey0 = parse_int(&p);
                        s32 ex1 = parse_int(&p), ey1 = parse_int(&p);
                        if (ln->tag != etag || ln->code != ecode
                            || ln->x0 != ex0 || ln->y0 != ey0
                            || ln->x1 != ex1 || ln->y1 != ey1) {
                            put_fd(2, "DRAW "); put_n(2, name, name_len);
                            put_fd(2, " line pkt "); put_int(2, i); put_fd(2, " mismatch\n");
                            return 1;
                        }
                    } else if (tok_eq(ptok, plen, "T")) {
                        struct LineF3 *tr = (struct LineF3 *)slot;
                        s32 exy[6], j;
                        for (j = 0; j < 6; j++) exy[j] = parse_int(&p);
                        if (tr->tag != etag || tr->code != ecode
                            || tr->xy[0] != exy[0] || tr->xy[1] != exy[1]
                            || tr->xy[2] != exy[2] || tr->xy[3] != exy[3]
                            || tr->xy[4] != exy[4] || tr->xy[5] != exy[5]) {
                            put_fd(2, "DRAW "); put_n(2, name, name_len);
                            put_fd(2, " tri pkt "); put_int(2, i); put_fd(2, " mismatch\n");
                            return 1;
                        }
                    } else {
                        put_fd(2, "DRAW unknown packet kind\n");
                        return 1;
                    }
                }
            }

            /* gDrawRadarMarkers (8005A9C4.c) emits the lines/triangles but does not touch
             * gFrameTime; restore the pre-case value so one DRAW cannot leak its frame stamp into
             * the PULSE cases that follow (8002703C.c:69 owns D_8009FAA4 in retail). */
            gFrameTime = saved_frame_time;
            cases++;
        } else {
            put_fd(2, "Unknown directive: ");
            put_n(2, kind, kind_len);
            put_fd(2, "\n");
            return 2;
        }

        while (*p && *p != '\n') p++;
    }

    if (cases != EXPECTED_CASES) {
        put_fd(2, "case count mismatch: expected ");
        put_int(2, EXPECTED_CASES);
        put_fd(2, " got ");
        put_int(2, cases);
        put_fd(2, "\n");
        return 2;
    }
    return 0;
}
