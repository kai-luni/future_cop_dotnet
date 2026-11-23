/* Executes unchanged func_800330E8 with the shared vector file as input. */
typedef unsigned char u8;
typedef short s16;
typedef int s32;

struct Vec3 { s32 x, y, z; };
struct Entry { struct Vec3 a, b; s16 pad18; u8 mode, pad1b; };
struct Owner { u8 pad[0xdc]; struct Vec3 position; u8 pad2[0x4c]; struct Entry *entry; };
struct Session { u8 pad[0xe0]; s32 field; };
struct Session *gGameSession __asm__("D_8009FA94");
void update(struct Owner *owner) __asm__("func_800330E8");

static int read0(char *buf, int count)
{
    register int v0 __asm__("$2") = 4003;
    register int a0 __asm__("$4") = 0;
    register char *a1 __asm__("$5") = buf;
    register int a2 __asm__("$6") = count;
    __asm__ volatile("syscall" : "+r"(v0) : "r"(a0), "r"(a1), "r"(a2) : "memory", "$7");
    return v0;
}
void __main(void) {}

int main(void)
{
    char text[4096], *p;
    int len = 0, n, i, count = 0;
    s32 v[15];
    struct Entry e;
    struct Owner o;
    struct Session session;
    gGameSession = &session;
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
        for (i = 0; i < 15; i++) {
            int sign = 1;
            while (*p == ' ' || *p == '\t') p++;
            if (i == 11) { if (p[0] != '-' || p[1] != '>') return 4; p += 2; while (*p == ' ') p++; }
            if (*p == '-') { sign = -1; p++; }
            if (*p < '0' || *p > '9') return 5;
            v[i] = 0;
            while (*p >= '0' && *p <= '9') { v[i] = v[i] * 10 + *p - '0'; p++; }
            v[i] *= sign;
        }
        while (*p == ' ' || *p == '\t' || *p == '\r') p++;
        if (*p != '\n' && *p != 0) return 6;
        e.mode = v[0];
        e.a.x = v[1]; e.a.y = v[2]; e.a.z = v[3];
        e.b.x = v[4]; e.b.y = v[5]; e.b.z = v[6];
        o.position.x = v[7]; o.position.y = v[8]; o.position.z = v[9];
        o.entry = &e; session.field = v[10];
        update(&o);
        if (o.position.x != v[11] || o.position.y != v[12] || o.position.z != v[13] || session.field != v[14]) return 10 + count;
        count++;
    }
    return count == 4 ? 0 : 7;
}
