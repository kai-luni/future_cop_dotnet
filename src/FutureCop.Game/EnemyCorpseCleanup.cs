namespace FutureCop.Game;

/// <summary>
/// Record of the investigation around retail address 0x8005FE80, which the task
/// description assumed to be a "corpse cleanup" function: it is not a function.
/// Evidence (verified against the retail disassembly, read-only):
/// - There is no glabel func_8005FE80 in asm/us/main.s; the address 0x8005FE80 is
///   the "addu $a0, $zero, $zero" at rom 0x50680 (main.s:93239), i.e. the second
///   gSpawnTypeAtOwner argument setup INSIDE func_8005F6E8 (glabel at main.s:92726,
///   endlabel at main.s:93307). No 'jal func_8005FE80' exists anywhere in main.s.
/// - There is no file 8005FE80.c in the decompiled source tree, and no C file mentions
///   the address (only build work logs quote the instruction).
/// - The "corpse cleanup" tail at 0x8005FE80 belongs to the fire dispatcher: it
///   spawns the s2->unk1A impact effect through func_800402DC with marker 5/0x800
///   (8005F6E8.c:444-448) - that code is ported as the flagC8 tail of EnemyWeaponFire.
/// Consequently there is nothing else portable under this address; this class exposes
/// the one shareable fact of the cluster - the entity-budget gate of func_80060238
/// (80060238.c:156-158) that governs when the scheduler runs at all.
/// </summary>
public static class EnemyCorpseCleanup
{
    /// <summary>
    /// The budget gate of the attack scheduler: the cluster only runs while
    /// D_800A8F90[1] - D_800A8F90[0] &gt;= 10, i.e. while the global entity table has
    /// at least ten free slots. C: 80060238.c:52 (extern D_800A8F90), 156-158 (gate);
    /// the C view of the table words: 8004E80C.c:21-25. The freed-slot pressure is what
    /// the original balances through its dead-entity recycling outside this cluster.
    /// </summary>
    public static bool HasFreeBudget(EnemyWorld env) => env.FreeSlots >= 10;
}
