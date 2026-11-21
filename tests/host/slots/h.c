
/* Reference harness for func_8006C080 (three slot status bar) with the real
   func_8006BF44 sprite emitter, so the emitted packets are recorded too. */
#include <stdio.h>
#include <stdarg.h>
#include <string.h>
#include <stdlib.h>
#include "types.h"

void gDrawThreeSlotStatusBar(void *arg0);
void gEmitStatusSlotIcon(int a0, int a1, int a2, int a3);

/* globals */
u8  gPlayerInvulnerable[0x40];
u32 gFrameTime;
void *gSinglePlayerEntity;
u8 *gPacketCursor;
u8 *gPacketLimit;
u32 gEnvMapTexpageWord[1];
u8 gFormatStringB[] = "%ld";

#define NB 4096
static u8 pool[NB];

static void put(const char *fmt, ...) {
    va_list ap; va_start(ap, fmt); vprintf(fmt, ap); va_end(ap); putchar('\n');
}
void gDrawAlignedString(u8 *text, s32 x, s32 y, s32 colour) {
    put("TEXT \"%s\" %d %d %d", (char*)text, x, y, colour);
}
/* The retail func_8008CD30 is a full printf over 32-bit arguments - on the PS1 a long is 32
   bits wide. The host libc treats long as 64, so "%ld" would read past the passed int and print
   garbage; the conversion is rewritten to the int form, exactly like tests/host/hud/hud_stubs.c. */
void func_8008CD30(void *buf, void *fmt, ...) {
    va_list ap; char host[32]; char *s = (char*)fmt; int i = 0;
    while (*s && i < (int)sizeof host - 3) {
        if (*s == 'l') { s++; continue; }
        host[i++] = *s++;
    }
    host[i] = 0;
    va_start(ap, fmt);
    vsnprintf((char*)buf, 32, host, ap);
    va_end(ap);
}
void gRegistryFind(u8 id) { put("REGISTRY %d", (int)id); }

typedef struct { u32 addr:24; u32 len:8; u8 r,g,b,code; short x,y; u8 u,v; u16 clut,w,h; } Packet;

static int pending;   /* packets emitted since the last slot marker */
static void dump_packet(const char *tag, Packet *p) {
    put("  %s code=%02X len=%d rgb=%02X%02X%02X xy=%d,%d uv=%d,%d clut=%04X wh=%d,%d addr=%u",
        tag, p->code, p->len, p->r, p->g, p->b, p->x, p->y, p->u, p->v, p->clut, p->w, p->h, p->addr);
}

/* Record every packet in the pool in order. Called by the driver after the routine. */
static void dump_pool(void) {
    u8 *p = pool;
    int n = 0;
    while (p < gPacketCursor) {
        char tag[24];
        snprintf(tag, sizeof tag, "packet%d", n++);
        dump_packet(tag, (Packet *)p);
        p += 0x1C;
    }
}

struct Unk { u8 pad00[0x84]; s16 field84[3]; s16 field8A[3]; u8 field90[3]; u8 pad93[0x2E]; s8 fieldC1; };
struct Arg { u8 pad00[0xAC]; struct Unk *ptr; };

int main(int argc, char **argv) {
    /* states: six numbers per line: f84a f84b f84c f8Aa f8Ab f8Ac f90a f90b f90c c1 frame invul */
    char line[512];
    static struct Unk u;
    struct Arg a;
    while (fgets(line, sizeof line, stdin)) {
        if (line[0] == '#' || line[0] == '\n') continue;
        memset(&u, 0, sizeof u); memset(&a, 0, sizeof a);
        memset(gPlayerInvulnerable, 0, sizeof gPlayerInvulnerable);
        gEnvMapTexpageWord[0] = 0x7C00 | (0x1234 << 16);
        int v[9]; int c1 = 0, frame = 0, inv = 0, second = 0;
        int got = sscanf(line, "%i %i %i %i %i %i %i %i %i %i %i %i %i",
                         &v[0],&v[1],&v[2],&v[3],&v[4],&v[5],&v[6],&v[7],&v[8],&c1,&frame,&inv,&second);
        if (got < 12) { fprintf(stderr, "bad line: %s", line); return 2; }
        for (int i = 0; i < 3; i++) { u.field84[i] = (s16)v[i]; u.field8A[i] = (s16)v[3+i]; u.field90[i] = (u8)v[6+i]; }
        u.fieldC1 = (s8)c1;
        gFrameTime = (u32)frame;
        if (inv) gPlayerInvulnerable[c1 & 0x3F] = 1;
        gSinglePlayerEntity = second ? (void*)1 : 0;
        a.ptr = &u;
        gPacketCursor = pool; gPacketLimit = pool + NB;
        printf("CASE c1=%d frame=0x%X inv=%d second=%d counts84=%d,%d,%d counts8A=%d,%d,%d ids=%d,%d,%d\n",
               c1, frame, inv, second, v[0],v[1],v[2],v[3],v[4],v[5],v[6],v[7],v[8]);
        gDrawThreeSlotStatusBar(&a);
        dump_pool();
    }
    return 0;
}
