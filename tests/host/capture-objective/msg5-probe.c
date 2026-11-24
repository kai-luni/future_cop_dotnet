/* Scratch probe: can the maspsx+gcc2.7.2 pipeline compile 8002D664.c standalone? */
#include "game/types.h"

extern s32 gEntityBaseHandler(s32 message, void *object, void *data, s32 arg3) __asm__("func_8002AD58");
extern void gInitObjectFromDef(void *obj, void *data, s32 arg2) __asm__("func_8002C584");
extern s32 gUpdatePropTick(void *obj, void *data) __asm__("func_8002CC30");
extern void gTryCaptureObjective(void *obj, void *other) __asm__("func_8002D1A0");
extern void gRemoveGroundKeyEntry(s32 key) __asm__("func_80063194");
extern void *gFindUnitById(s32 id) __asm__("func_8004D664");

s32 gEntityBaseHandler(s32 message, void *object, void *data, s32 arg3) { return 0; }
void gInitObjectFromDef(void *obj, void *data, s32 arg2) { }
s32 gUpdatePropTick(void *obj, void *data) { return 0; }
void gTryCaptureObjective(void *obj, void *other) { }
void gRemoveGroundKeyEntry(s32 key) { }
void *gFindUnitById(s32 id) { return 0; }

int main(void) { return 0; }
