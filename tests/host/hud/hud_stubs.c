/* Host harness: runs the ORIGINAL gDrawInGameHud (src/game/80047F30.c) with recording stubs, so
   the expected draw sequence comes from the retail routine itself and not from the port.
   hud_test.sh copies 80047F30.c and 8001C214.c out of the decomp repo into a temp dir with their
   __asm__("...") attributes stripped and compiles them against this file. Nothing here is a port:
   every drawing primitive is a stub that only prints its arguments.

   The stubs make four assumptions the C code does not settle; each is called out at its site and
   in the header of tests/golden/hud-draw.vectors.

   Input : a state file, one "CASE <name> key=value ..." line per state (blank lines and '#' comments
           ignored).
   Output: for every case a "CASE <name>" line followed by the recorded calls, one per line, two
           spaces indented.

   usage: hud_draw_gen <states-file> >
*/
#include <stdio.h>
#include <stdarg.h>
#include <string.h>
#include <stdlib.h>
#include "types.h"

void gDrawInGameHud(void *game);

/* Globals the routine reads. */
s32 gFrameTime;
s32 gScreenState;
s32 gPlayerCount;
void *gGameSession;
void *gSinglePlayerEntity;
u8 gCounterFormatString[] = "%02ld/%02ld";
u8 gStageLineFormat[] = "L%02d";
u8 gScoreFormatString[] = "%03ld";
u8 *gLabelTable[16];
u8 **gPlayerAvatarSprite = gLabelTable;

/* Recorder. */
#define REC_MAX 256
static int rec_count;
static char rec[REC_MAX][192];

static void put(const char *fmt, ...)
{
    va_list ap;
    if (rec_count >= REC_MAX) return;
    va_start(ap, fmt);
    vsnprintf(rec[rec_count], sizeof(rec[0]), fmt, ap);
    va_end(ap);
    rec_count++;
}

void gDrawTargetMarker(void) { put("MARKER"); }
void gDrawThreeSlotStatusBar(void *g) { (void)g; put("SLOTSBAR"); }
void gEmitHudGouraudBar(s32 x, s32 y, s32 z, s32 p3, s32 p4, s32 p5, s32 flag)
{ put("BAR %d %d %d %d %d %d %d", (int)x, (int)y, (int)z, (int)p3, (int)p4, (int)p5, (int)flag); }
/* The decomp repo renames functions as it identifies them; func_8005AB8C now carries the
   descriptive name gDrawRadarBackgroundAndMarkers. Both spellings are stubbed so the harness
   links against either revision of 80047F30.c. */
void gDrawRadarBackgroundAndMarkers(void *g, void *a, void *b) { (void)g; (void)a; (void)b; put("TARGETPASS"); }
void func_8005AB8C(void *g, void *a, void *b) { gDrawRadarBackgroundAndMarkers(g, a, b); }
void gDrawTargetMarkers(void *g) { (void)g; put("MARKERS"); }
/* func_80049340 sums the per-character advance and returns total * 2 (80049340.c:51).
   The retail advances live in the VRAM glyph atlas and are not decoded. The harness therefore uses
   the SAME 4 pixel advance as the port's InGameHud, so the vectors pin everything except that one
   number. An 8 pixel advance was tried first and produced negative label positions ("Close-Up Side
   Camera" -> x -12), which no font on a 320 pixel display could justify - so 8 is disproved, and the
   true advance is somewhere in 1..7. */
#define GLYPH_ADVANCE 4
s32 func_80049340(u8 *s) { return (s32)strlen((char *)s) * GLYPH_ADVANCE * 2; }
s32 gMeasureTextWidthPx(u8 *s) { return func_80049340(s); }
void gDrawAlignedString(u8 *text, s32 x, s32 y, s32 colour)
{ put("TEXT \"%s\" %d %d %d", (char *)text, (int)x, (int)y, (int)colour); }
/* The rectangle path is the ORIGINAL func_8001C214 (game/8001C214.c): it halves x/y/w/h and
   widens the 15-bit colour. Only the final TILE emitter is a recorder here, so the recorded
   values are the real screen coordinates and 8-bit channels. */
void gEmitSolidTileRect(u8 red, u8 green, u8 blue, void *rect, u8 blend)
{
    s16 *r = (s16 *)rect;
    put("RECT %d %d %d x=%d y=%d w=%d h=%d blend=%d", red, green, blue, r[0], r[1], r[2], r[3], blend);
}
/* The retail func_8008CD30 is a full printf implementation (8008CD3C.c) over 32-bit arguments - on
   the PS1 a long is 32 bits wide. The host libc treats long as 64 bits, so the conversion is rewritten
   to the int form; without this the varargs read past the argument and print garbage. */
static void format_retail(void *buf, void *fmt, va_list ap)
{
    char host[32];
    char *s = (char *)fmt;
    int i = 0;
    while (*s && i < (int)sizeof host - 3) {
        if (*s == 'l') { s++; continue; }   /* PS1 long is 32 bit wide, host long is not */
        host[i++] = *s++;
    }
    host[i] = 0;
    vsnprintf((char *)buf, 32, host, ap);
}
void func_8008CD30(void *buf, void *fmt, ...)
{
    va_list ap;
    va_start(ap, fmt);
    format_retail(buf, fmt, ap);
    va_end(ap);
}
void gSpillVarargsArguments(void *buf, void *fmt, ...)
{
    va_list ap;
    va_start(ap, fmt);
    format_retail(buf, fmt, ap);
    va_end(ap);
}

/* Retail block layouts, taken from the typedefs at the top of 80047F30.c. */
typedef struct {
    u8 pad00[0x58];
    s32 value58, value5c;
    u8 value60[4];
    u8 value64;
    u8 pad65[0x3f];
    u8 valuea4;
    u8 pada5[9];
    s16 valueae;
    u8 padb0[0x11];
    s8 valuec1;
    u8 padc2[0x264];
    s16 value326;
} PlayerBlock;

typedef struct {
    u8 pad00[0xa4];
    s32 a4, a8;
} ExtraBlock;

typedef struct {
    u8 pad00[0x1c];
    s16 health, maxhealth;
    u8 pad20[0x30];
    u8 position[0x50];
    u8 veca0[0xc];
    PlayerBlock *player;
    u8 padb0[0x84];
    ExtraBlock *extra;
} GameBlock;

typedef struct {
    u8 pad00[0x4c];
    s32 stage;
    u8 pad50[0xc];
    s32 mode;
    u8 pad60[4];
    s32 score[2];
    s32 count1, count2, count3, count4;
    u8 pad7c[8];
    s32 total1, total2;
} StateBlock;

static GameBlock gameA, gameB;
static PlayerBlock playerA, playerB;
static ExtraBlock extraB;
static StateBlock state;
static char labels[16][40];

#define LABEL_COUNT 7
static const char *labels_text[LABEL_COUNT] = {
    "Standard Camera", "Close-Up Camera", "Standard Side Camera", "Close-Up Side Camera",
    "Sky Camera", "Crowd Control Camera", "Crowd Control Side Camera",
};
/* The retail table at 0x8009AC58 holds seven pointers and ends with the sentinel 0x0001FFFF, so
   indices past 6 must not resolve to a string here either. */

static void reset(void)
{
    int i;
    memset(&gameA, 0, sizeof gameA);
    memset(&gameB, 0, sizeof gameB);
    memset(&playerA, 0, sizeof playerA);
    memset(&playerB, 0, sizeof playerB);
    memset(&extraB, 0, sizeof extraB);
    memset(&state, 0, sizeof state);
    for (i = 0; i < 16; i++) {
        if (i < LABEL_COUNT) {
            strncpy(labels[i], labels_text[i], sizeof(labels[i]) - 1);
            labels[i][sizeof(labels[i]) - 1] = 0;
        } else {
            labels[i][0] = 0;   /* the retail table ends here, so nothing follows */
        }
        gLabelTable[i] = (u8 *)labels[i];
    }
    gameA.player = &playerA;
    gameB.player = &playerB;
    gameB.extra = &extraB;
    gScreenState = 4;
    gPlayerCount = 1;
    gFrameTime = 0;
    gGameSession = &state;
    gSinglePlayerEntity = 0;
    rec_count = 0;
}

/* Applies one "key=value ..." state description to the retail blocks. */
static void apply(char *line)
{
    char *tok;
    for (tok = strtok(line, " \t\r\n"); tok; tok = strtok(0, " \t\r\n")) {
        char *eq = strchr(tok, '=');
        s32 v;
        if (!eq) continue;
        *eq = 0;
        v = (s32)strtol(eq + 1, 0, 0);
        if (!strcmp(tok, "screen")) gScreenState = v;
        else if (!strcmp(tok, "players")) gPlayerCount = v;
        else if (!strcmp(tok, "frame")) gFrameTime = v;
        else if (!strcmp(tok, "mode")) state.mode = v;
        else if (!strcmp(tok, "health")) gameA.health = (s16)v;
        else if (!strcmp(tok, "maxhealth")) gameA.maxhealth = (s16)v;
        else if (!strcmp(tok, "w58")) playerA.value58 = v;
        else if (!strcmp(tok, "w5c")) playerA.value5c = v;
        else if (!strcmp(tok, "w64")) playerA.value64 = (u8)v;
        else if (!strcmp(tok, "w326")) playerA.value326 = (s16)v;
        else if (!strcmp(tok, "a4")) playerA.valuea4 = (u8)v;
        else if (!strcmp(tok, "ae")) playerA.valueae = (s16)v;
        else if (!strcmp(tok, "c1")) playerA.valuec1 = (s8)v;
        else if (!strcmp(tok, "score0")) state.score[0] = v;
        else if (!strcmp(tok, "score1")) state.score[1] = v;
        else if (!strcmp(tok, "count1")) state.count1 = v;
        else if (!strcmp(tok, "count2")) state.count2 = v;
        else if (!strcmp(tok, "count3")) state.count3 = v;
        else if (!strcmp(tok, "count4")) state.count4 = v;
        else if (!strcmp(tok, "total1")) state.total1 = v;
        else if (!strcmp(tok, "total2")) state.total2 = v;
        else if (!strcmp(tok, "stage")) state.stage = v;
        else if (!strcmp(tok, "second")) {
            gSinglePlayerEntity = &gameB;
            gameB.health = (s16)v;
        }
        else if (!strcmp(tok, "secondmax")) gameB.maxhealth = (s16)v;
        else if (!strcmp(tok, "extraa4")) extraB.a4 = v;
        else if (!strcmp(tok, "extraa8")) extraB.a8 = v;
        else { fprintf(stderr, "unknown key '%s'\n", tok); exit(2); }
    }
}

int main(int argc, char **argv)
{
    FILE *in;
    char line[512];
    int i;

    if (argc != 2) { fprintf(stderr, "usage: %s <states-file>\n", argv[0]); return 2; }
    in = fopen(argv[1], "r");
    if (!in) { perror(argv[1]); return 2; }

    while (fgets(line, sizeof line, in)) {
        char name[32], *rest;
        if (line[0] == '#' || line[0] == '\n' || line[0] == '\r') continue;
        if (strncmp(line, "CASE", 4)) { fprintf(stderr, "expected CASE: %s", line); return 2; }
        rest = line + 4;
        while (*rest == ' ' || *rest == '\t') rest++;
        if (sscanf(rest, "%31s", name) != 1) { fprintf(stderr, "no case name\n"); return 2; }
        reset();
        apply(rest + strlen(name));
        gDrawInGameHud(&gameA);
        printf("CASE %s", name);
        /* Echo the normalised state so the vector file documents its own input. */
        printf(" screen=%d players=%d frame=%d mode=%d", (int)gScreenState, (int)gPlayerCount,
               (int)gFrameTime, (int)state.mode);
        printf(" health=%d maxhealth=%d w58=%d w5c=%d w64=%d w326=%d a4=%d ae=%d c1=%d",
               (int)gameA.health, (int)gameA.maxhealth, (int)playerA.value58, (int)playerA.value5c,
               (int)playerA.value64, (int)playerA.value326, (int)playerA.valuea4,
               (int)playerA.valueae, (int)playerA.valuec1);
        printf(" score0=%d score1=%d count1=%d count2=%d count3=%d count4=%d total1=%d total2=%d stage=%d",
               (int)state.score[0], (int)state.score[1], (int)state.count1, (int)state.count2,
               (int)state.count3, (int)state.count4, (int)state.total1, (int)state.total2,
               (int)state.stage);
        if (gSinglePlayerEntity)
            printf(" second=%d secondmax=%d extraa4=%d extraa8=%d", (int)gameB.health,
                   (int)gameB.maxhealth, (int)extraB.a4, (int)extraB.a8);
        printf("\n");
        for (i = 0; i < rec_count; i++) printf("  %s\n", rec[i]);
    }
    fclose(in);
    return 0;
}
