#include "fc_testkit.h"

/* 32-bit MIPS fixture blocks. Only fields touched by the four linked retail
 * functions are named, including the extra mirror banks beyond Ai +0x4C. */
struct Vec3 { s32 x, y, z; };
struct Ai {
    struct Vec3 posA, posB;
    u8 pad18[2], mode, b1B, c1C, pad1D[0x13];
    u16 u30, u32;
    u8 pad34[0x18];
    s32 s4C;
    u8 mirrors[0x48];
};
struct Unit {
    struct Unit *next;
    u8 pad04[4];
    s32 id;
    u8 pad0C[0xA], type, pad17, team, sub, pad1A[2];
    s16 s1C;
    u16 u1E;
    u8 pad20[0x30];
    struct Vec3 pos;
    u8 pad5C[0x4A], aq6, padA7;
    u16 a8;
    s16 aa;
    void *aux;
    u8 padB0[4];
    u16 b4, b6;
    struct Unit *b8;
    s32 bc;
    u8 padC0[8];
    struct Unit *c8;
    s32 cc;
    u8 padD0[0xC];
    struct Vec3 dc;
    u8 padE8[0x12], fa, padFB[0x39];
    struct Ai *ai;
};
struct Glob {
    u8 pad00[0x4C];
    s32 s4C;
    u8 pad50[0x18];
    s32 f68;
    u8 pad6C[8];
    s32 f74, f78;
    u8 pad7C[8];
    s32 f84, f88;
    u8 pad8C[0x10];
    s32 f9C, a0[4], fB0, fB4, fB8, fBC, c[4][2], fE0;
};
#define OFFSET(type, name) ((unsigned)&((struct type *)0)->name)
typedef char layout_check[
    OFFSET(Unit, ai) == 0x134 && OFFSET(Unit, b8) == 0xB8 &&
    OFFSET(Unit, c8) == 0xC8 && OFFSET(Unit, dc) == 0xDC &&
    OFFSET(Ai, s4C) == 0x4C && OFFSET(Glob, fE0) == 0xE0 ? 1 : -1];

static struct Glob globe;
static struct Unit drone, player, units[12];
static struct Ai ai;
struct Unit *D_8009FA40;
struct Glob *D_8009FA94 = &globe;
struct Unit *D_8009F9EC = &drone;
s32 D_8009FAA4 = 1234;
void *D_8009FA58 = &player;
u8 D_8009AC50[] = {3, 2, 1, 0};
u8 D_8009AC54[] = {1, 0, 3, 2};
extern void func_800336DC(struct Unit *self);

/* Controlled leaves: RNG is fixed per row; Linux/MIPS cannot execute the
 * PS1 COP2 LZCS/LZCR of SquareRoot12. Capture its exact signed input and
 * return the value specified by the row to check the downstream comparison. */
static s32 rng, rng_calls, sqrt_result, sqrt_calls, sqrt_input;
s32 func_80019668(void) { rng_calls++; return rng; }
s32 func_80095900(s32 value)
{
    sqrt_calls++;
    sqrt_input = value;
    return sqrt_result;
}

enum Input {
    Shape, Mode, Level, Credits, F74, F78, F84, F88, Alternate,
    A0, A1, A2, A3, PosBX, TargetX, TargetZ, Range, Random, Root, Inputs
};
static const char *fields[] = {
    "mode", "f9C", "f68", "f74", "f78", "fB0", "fB4", "fB8", "fBC", "fE0",
    "a0[0]", "a0[1]", "a0[2]", "a0[3]", "c[0][0]", "c[0][1]", "c[1][0]", "c[1][1]",
    "c[2][1]", "c[3][1]", "s4C", "fa", "aq6", "attack", "pursuit", "dc.x", "dc.z",
    "rngCalls", "sqrtCalls", "sqrtInput"
};
#define OUTPUTS (sizeof(fields) / sizeof(fields[0]))

static void tick(const s32 *in, s32 *out)
{
    s32 i, count;
    memset(&globe, 0, sizeof(globe));
    memset(&drone, 0, sizeof(drone));
    memset(&ai, 0, sizeof(ai));
    memset(units, 0, sizeof(units));
    player.id = 2;
    globe.s4C = in[Level];
    globe.f9C = ai.mode = in[Mode];
    globe.f68 = in[Credits];
    globe.f74 = in[F74];
    globe.f78 = in[F78];
    globe.f84 = in[F84];
    globe.f88 = in[F88];
    for (i = 0; i < 4; i++) globe.a0[i] = in[A0 + i];
    drone.ai = &ai;
    drone.team = 2;
    drone.aa = in[Range];
    ai.posA.x = 32769;
    ai.posA.z = -65537;
    ai.posB.x = in[PosBX];
    ai.b1B = 1;
    ai.c1C = in[Alternate];
    rng = in[Random];
    sqrt_result = in[Root];
    rng_calls = sqrt_calls = sqrt_input = 0;

    /* 0 = no candidates; 1 = ten nearby own type-8s; 2 = own 0x1c by posA;
     * 3 = opposing type-5; 4 = own high-subtype type-9 (prevents upkeep);
     * 5 = ten nearby own type-8s followed by an own 0x1c;
     * 6 = ten nearby own type-8s followed by two own type-5s at decreasing distance from posA. */
    count = in[Shape] == 1 ? 10 : in[Shape] == 5 ? 11 : in[Shape] == 6 ? 12 : (in[Shape] != 0);
    D_8009FA40 = count ? units : 0;
    for (i = 0; i < count; i++) {
        units[i].next = i + 1 < count ? &units[i + 1] : 0;
        units[i].id = i + 3;
        units[i].team = in[Shape] == 3 ? 1 : 2;
        switch (in[Shape]) {
        case 1: units[i].type = 8; break;
        case 2: units[i].type = 0x1c; break;
        case 3: units[i].type = 5; break;
        case 4: units[i].type = 9; break;
        case 5: units[i].type = i < 10 ? 8 : 0x1c; break;
        case 6: units[i].type = i < 10 ? 8 : 5; break;
        }
        units[i].sub = 10;
        if (in[Shape] == 6 && i >= 10) {
            units[i].pos.x = ai.posA.x + (i == 10 ? 0x20000 : 0x10000);
            units[i].pos.z = ai.posA.z;
        } else {
            units[i].pos.x = in[TargetX];
            units[i].pos.z = in[TargetZ];
        }
    }
    func_800336DC(&drone);

    *out++ = ai.mode; *out++ = globe.f9C;
    *out++ = globe.f68; *out++ = globe.f74; *out++ = globe.f78;
    *out++ = globe.fB0; *out++ = globe.fB4; *out++ = globe.fB8;
    *out++ = globe.fBC; *out++ = globe.fE0;
    for (i = 0; i < 4; i++) *out++ = globe.a0[i];
    for (i = 0; i < 2; i++) {
        *out++ = globe.c[i][0]; *out++ = globe.c[i][1];
    }
    *out++ = globe.c[2][1]; *out++ = globe.c[3][1];
    *out++ = ai.s4C; *out++ = drone.fa; *out++ = drone.aq6;
    *out++ = drone.b8 ? drone.bc : 0; *out++ = drone.c8 ? drone.cc : 0;
    *out++ = drone.dc.x; *out++ = drone.dc.z;
    *out++ = rng_calls; *out++ = sqrt_calls; *out = sqrt_input;
}

static char data[16384];

static char *token(char **p)
{
    char *start;
    while (**p == ' ' || **p == '\t' || **p == '\r') (*p)++;
    if (!**p || **p == '#') return 0;
    start = *p;
    while (**p && **p != ' ' && **p != '\t' && **p != '\r') (*p)++;
    if (**p) *(*p)++ = 0;
    return start;
}

static int number(char **p, s32 *out)
{
    char *word = token(p);
    u32 v = 0, max, digit;
    int neg;
    if (!word) return 0;
    neg = (*word == '-');
    if (neg) word++;
    if (!*word) return 0;
    max = neg ? 0x80000000u : 0x7fffffffu;
    for (; *word; word++) {
        digit = (u32)(*word - '0');
        if (digit > 9 || v > (max - digit) / 10) return 0;
        v = v * 10 + digit;
    }
    *out = (s32)(neg ? 0u - v : v);
    return 1;
}

static void write_words(const s32 *words, int count)
{
    int i;
    for (i = 0; i < count; i++) {
        fck_write(" ");
        fck_write_int(words[i]);
    }
}

static int run_row(char *line, int emit)
{
    const char *name = token(&line);
    s32 input[Inputs], expected[OUTPUTS], actual[OUTPUTS];
    int i, failed = 0;
    if (!name) return 0;
    for (i = 0; i < Inputs; i++)
        if (!number(&line, &input[i])) return -1;
    if (input[Shape] < 0 || input[Shape] > 6) return -1;
    if (!emit) {
        const char *arrow = token(&line);
        if (!arrow || arrow[0] != '-' || arrow[1] != '>' || arrow[2]) return -1;
        for (i = 0; i < OUTPUTS; i++)
            if (!number(&line, &expected[i])) return -1;
    }
    if (token(&line)) return -1;

    tick(input, actual);
    if (emit) {
        fck_write(name);
        write_words(input, Inputs);
        fck_write(" ->");
        write_words(actual, OUTPUTS);
        fck_write("\n");
    } else {
        for (i = 0; i < OUTPUTS; i++) {
            if (actual[i] != expected[i]) {
                fck_write("drone strategy: FAIL ");
                fck_write(name);
                fck_write(" ");
                fck_write(fields[i]);
                fck_write(" got ");
                fck_write_int(actual[i]);
                fck_write(" expected ");
                fck_write_int(expected[i]);
                fck_write("\n");
                failed = 1;
            }
        }
    }
    return failed ? -2 : 1;
}

int main(int argc, char **argv)
{
    int length, cases = 0, failed = 0, i, start = 0, emit = argc == 3;
    if (argc < 2 || argc > 3) return 2;
    length = fck_read_file(argv[1], data, sizeof(data));
    if (length < 0 || length == sizeof(data) - 1) {
        fck_write("drone strategy: cannot read fixture or fixture too large\n");
        return 2;
    }
    for (i = 0; i <= length; i++) {
        if (data[i] == '\n' || i == length) {
            int status;
            data[i] = 0;
            status = run_row(data + start, emit);
            if (status == -1) {
                fck_write("drone strategy: malformed vector row\n");
                return 2;
            }
            if (status == -2) failed++;
            if (status) cases++;
            start = i + 1;
        }
    }
    if (!cases) return 2;
    if (failed) return 1;
    if (!emit) {
        fck_write("drone strategy: all ");
        fck_write_int(cases);
        fck_write(" vectors PASS\n");
    }
    return 0;
}
