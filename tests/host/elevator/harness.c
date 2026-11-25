/* Trace the unchanged lift init, motion and command removal. Only external services are stubbed. */
#include "fc_testkit.h"

struct Record {
    u8 pad[0x24];
    s8 count;
    u8 start;
    s16 stops[3], dwell[3], speed[2], angle;
    u8 flags, kind, sound;
};
struct InitArg { u8 pad[8]; struct Record *record; };
struct Model { u8 pad[0x34]; u8 *frames; };
struct Lift {
    u8 pad00[8];
    s32 id;
    u8 commands[8], pad14[0xD];
    u8 old, channel, pad23;
    u32 flags;
    struct Model *model;
    u8 basis[0x24];
    s32 x, y, z;
    u8 vertex[0x14];
    s16 frame;
    u8 pad72[0x2E];
    s32 stops[3], dwell[3], speed[2], timer;
    s16 hold;
    u8 modeFlags, kind;
    s8 count, last, next, direction;
    u8 previous, sound;
};
struct PlayerState { u8 pad[0x40]; s32 form; };
struct Player {
    u8 pad00[0xAC];
    struct PlayerState *state;
    u8 padB0[0x14];
    struct Lift *platform;
};

extern struct Player *players[2] __asm__("D_8009FA58");
extern void init_lift(struct Lift *, struct InitArg *, s32) __asm__("func_80041488");
extern s32 step_lift(struct Lift *, s32) __asm__("func_80041880");

static struct Lift lift, other;
static struct Player p1, p2;
static struct PlayerState hover, walker;
static u8 frames[64];
static struct Model model;
static s32 message, ground_kind, yaw, loads, transforms, frame_offset, refresh_y[2];
static s32 stopped, played;

void base_handler(s32 msg, struct Lift *self, struct InitArg *arg, s32 data) __asm__("func_8002AD58");
void base_handler(s32 msg, struct Lift *self, struct InitArg *arg, s32 data) { message = msg; }
void register_ground(struct Lift *self, u8 kind) __asm__("func_8006314C");
void register_ground(struct Lift *self, u8 kind) { ground_kind = kind; }
void rotation(s32 y, s32 pitch, s32 roll, void *out) __asm__("func_800188D0");
void rotation(s32 y, s32 pitch, s32 roll, void *out) { yaw = y; }
s32 ground_height(void *position, s32 a, s32 b, s32 c) __asm__("func_80066F78");
s32 ground_height(void *position, s32 a, s32 b, s32 c) { return 100; }
void load_matrix(void *basis) __asm__("func_80010750");
void load_matrix(void *basis) { loads++; }
void transform(u8 *frame, void *out, s32 count) __asm__("func_80010234");
void transform(u8 *frame, void *out, s32 count)
{
    frame_offset = frame - frames;
    if (transforms < 2) refresh_y[transforms] = lift.y;
    transforms++;
}
void stop_sound(s32 id, s32 channel) __asm__("func_800457B8");
void stop_sound(s32 id, s32 channel) { stopped = id; }
void positional_sound(s32 id, s32 x, s32 z, s32 flags) __asm__("func_80047284");
void positional_sound(s32 id, s32 x, s32 z, s32 flags) { played = id; }

static void value(s32 n)
{
    fck_write(" ");
    fck_write_int(n);
}

static void command(int index, u8 op, u8 arg)
{
    lift.commands[index * 2] = op;
    lift.commands[index * 2 + 1] = arg;
}

static void reset(s8 count, u8 start, u8 flags)
{
    struct Record record = {{0}, 2, 1, {81, 921, 1228}, {0, 384, 768}, {204, 409}, 1024, 0, 14, 71};
    struct InitArg arg;
    int i;

    for (i = 0; i < sizeof(lift); i++) ((u8 *)&lift)[i] = 0;
    record.count = count;
    record.start = start;
    record.flags = flags;
    arg.record = &record;
    model.frames = frames;
    lift.model = &model;
    lift.frame = 1;
    lift.old = 1;
    lift.id = 42;
    players[0] = 0;
    players[1] = 0;
    loads = transforms = stopped = played = 0;
    init_lift(&lift, &arg, 0);
}

/* Rows: Y, timer, last, next, direction, hold, mode flags, eight command bytes,
 * GTE load/transform counts, first two refresh heights, stop/play sound IDs. */
static void tick(const char *name, s32 dt)
{
    int i;
    loads = transforms = stopped = played = 0;
    refresh_y[0] = refresh_y[1] = 0;
    step_lift(&lift, dt);

    fck_write(name);
    value(lift.y); value(lift.timer); value(lift.last); value(lift.next); value(lift.direction);
    value(lift.hold); value(lift.modeFlags);
    for (i = 0; i < sizeof(lift.commands); i++) value(lift.commands[i]);
    value(loads); value(transforms); value(refresh_y[0]); value(refresh_y[1]);
    value(stopped); value(played);
    fck_write("\n");
}

int main(void)
{
    int i;

    reset(2, 1, 0);
    fck_write("init");
    for (i = 0; i < 3; i++) value(lift.stops[i]);
    for (i = 0; i < 3; i++) value(lift.dwell[i]);
    value(lift.speed[0]); value(lift.speed[1]);
    value(lift.y); value(lift.timer); value(lift.last); value(lift.next); value(lift.direction);
    value(lift.previous); value(message); value(ground_kind); value(yaw);
    value(loads); value(transforms); value(frame_offset);
    fck_write("\n");

    tick("no-rider", 10);
    hover.form = 1;
    p1.state = &hover;
    p1.platform = &lift;
    players[0] = &p1;
    tick("hover-rider", 10);
    p1.state = &walker;
    p1.platform = &other;
    tick("wrong-lift", 10);
    p2.state = &walker;
    p2.platform = &lift;
    players[1] = &p2;
    tick("second-rider", 10);
    players[0] = players[1] = 0;
    lift.flags = 0x400000;
    tick("top-snap", 2000);
    tick("dwell", 100);
    tick("dwell-expiry", 350);
    tick("bottom-snap", 1000);
    tick("wait-at-bottom", 1);
    players[1] = &p2;
    tick("second-rider-at-bottom", 1);

    reset(2, 1, 4);
    players[1] = &p2;
    tick("script-only", 10);
    command(0, 0x52, 0);
    tick("arm", 10);

    reset(3, 2, 0);
    lift.hold = 2;
    command(0, 0x53, 0);
    tick("freeze", 50);
    command(0, 0x53, 1);
    tick("unfreeze", 50);
    tick("held", 50);
    command(0, 0x99, 42);
    command(1, 0x52, 1);
    tick("seek", 1);
    command(1, 0x52, 13);
    command(2, 0x53, 0);
    tick("command-snap-and-freeze", 0);
    return 0;
}
