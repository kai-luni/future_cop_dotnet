/* The solver, distance, normalization, and reciprocal-table builder are original C.
 * Only the platform-dependent LZCR/packed lookup and the table-init environment
 * (pool allocation and unrelated RNG seed) are stand-ins. */
#include "fc_testkit.h"

typedef struct { s32 x, y, z; } Vec3;
extern void func_8005E2A0(Vec3 *target, Vec3 *out, s32 arg2, s32 arg3,
                          Vec3 *start, Vec3 *direction, s32 scale);
extern void func_80019828(void);

static u16 table[1024];
u16 *D_8009F5B0;

void *func_80017EBC(s32 size, s32 tag)
{
    fck_record("func_80017EBC", size, tag, 0);
    return table;
}

void func_800197A8(s32 seed)
{
    fck_record("func_800197A8", seed, 0, 0);
}

s32 func_80095C14(u32 value)
{
    s32 count = 0;
    if (value & 0x80000000u) value = ~value;
    if (!value) return 32;
    while (!(value & 0x80000000u)) {
        value <<= 1;
        count++;
    }
    return count;
}

/* 80019904.c uses COP2 and a PS1 $gp slot, neither available to Linux
 * qemu-user. Translate its shifts and unsigned halfword load, using the
 * table populated by the unchanged 80019828.c. */
s32 func_80019904(s32 value)
{
    s32 shift = 0x16 - (func_80095C14((u32)value) & ~1);
    s32 index;
    u16 entry;
    if (shift < 0) shift = 0;
    index = value >> shift;
    if ((u32)index >= 1024u) {
        fck_write("lead direction: lookup index out of range\n");
        fck_sys3(4001, 2, 0, 0);
    }
    entry = D_8009F5B0[index];
    return (s32)((u32)(entry >> 5) << (entry & 31)) >> (shift >> 1);
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

static Vec3 vec(const s32 *values)
{
    Vec3 result;
    result.x = values[0];
    result.y = values[1];
    result.z = values[2];
    return result;
}

static void write_vec(Vec3 v)
{
    fck_write_int(v.x);
    fck_write(" ");
    fck_write_int(v.y);
    fck_write(" ");
    fck_write_int(v.z);
}

static int run_row(char *line, int emit)
{
    const char *name = token(&line);
    s32 input[12], expected[3];
    Vec3 target, start, direction, aim;
    int i;
    if (!name) return 0;
    for (i = 0; i < 12; i++)
        if (!number(&line, &input[i])) return -1;
    if (!emit) {
        const char *arrow = token(&line);
        if (!arrow || arrow[0] != '-' || arrow[1] != '>' || arrow[2]) return -1;
        for (i = 0; i < 3; i++)
            if (!number(&line, &expected[i])) return -1;
    }
    if (token(&line)) return -1;

    target = vec(input);
    start = vec(input + 5);
    direction = vec(input + 8);
    func_8005E2A0(&target, &aim, input[3], input[4], &start, &direction, input[11]);

    if (emit) {
        fck_write(name);
        for (i = 0; i < 12; i++) {
            fck_write(" ");
            fck_write_int(input[i]);
        }
        fck_write(" -> ");
        write_vec(aim);
        fck_write("\n");
    } else if (aim.x != expected[0] || aim.y != expected[1] || aim.z != expected[2]) {
        fck_write("lead direction: FAIL ");
        fck_write(name);
        fck_write(" got ");
        write_vec(aim);
        fck_write(" expected ");
        write_vec(vec(expected));
        fck_write("\n");
        return -2;
    }
    return 1;
}

int main(int argc, char **argv)
{
    int length, cases = 0, i, start = 0, emit = argc == 3;
    if (argc < 2 || argc > 3) return 2;
    length = fck_read_file(argv[1], data, sizeof(data));
    if (length < 0 || length == sizeof(data) - 1) {
        fck_write("lead direction: cannot read fixture or fixture too large\n");
        return 2;
    }
    func_80019828();
    if (fck_log_count != 2 || fck_log[0].a0 != sizeof(table) ||
        fck_log[0].a1 != 2 || fck_log[1].a0 != 0x457) {
        fck_write("lead direction: unexpected reciprocal-table initialization\n");
        return 2;
    }

    for (i = 0; i <= length; i++) {
        if (data[i] == '\n' || i == length) {
            int status;
            data[i] = 0;
            status = run_row(data + start, emit);
            if (status == -2) return 1;
            if (status == -1) {
                fck_write("lead direction: malformed vector row\n");
                return 2;
            }
            cases += status;
            start = i + 1;
        }
    }
    if (!cases) return 2;
    if (!emit) {
        fck_write("lead direction: all ");
        fck_write_int(cases);
        fck_write(" vectors PASS\n");
    }
    return 0;
}
