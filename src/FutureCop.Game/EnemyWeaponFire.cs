using FutureCop.Game.Mech;

namespace FutureCop.Game;

/// <summary>
/// Port of func_8005F6E8 gFireEntityWeapon (8005F6E8.c:165-463) plus a documented
/// memorial of addr 0x8005FE80: that address is NOT a function in the retail binary;
/// it sits inside func_8005F6E8 at the second gSpawnTypeAtOwner tail (asm main.s:93239,
/// rom 0x50680; no glabel, no caller; endlabel func_8005F6E8 at main.s:93307) - see
/// EnemyCorpseCleanup for the record. Fire-flag semantics, gate bands, hit-path and
/// fail-tail state writes follow the C reference line by line, verified against the
/// assembler for every field width (asm 93218-93298).
/// </summary>
public static class EnemyWeaponFire
{
    /// <summary>
    /// gTracerCoreColour D_8009F378: four colour bytes memcpy'd for the type-12 tracer.
    /// C: 8005F6E8.c:91, 327. Retail probe of SLUS_007.39 (little-endian word at
    /// file offset 0x8F178 = 0x8009F378 - 0x8000F800): 0x08087F00.
    /// </summary>
    public const uint TracerCoreColour = 0x08087F00u;

    /// <summary>
    /// Fire entry as called by the scheduler: the low byte of the return value becomes
    /// the scheduler result (80060238.c:286). Public surface per task description.
    /// </summary>
    public static int Fire(AttackSlot slot, EnemyEntity actor, EnemyEntity? second, int fireFlag, EnemyWorld env) =>
        (byte)FireUnchecked(slot, actor, second, fireFlag, env);

    /// <summary>The raw s32 return as the C function produces it (8005F6E8.c:165).</summary>
    public static int FireUnchecked(AttackSlot slot, EnemyEntity actor, EnemyEntity? a2, int fireFlag, EnemyWorld env)
    {
        unchecked
        {
            return FireCore(slot, actor, a2, fireFlag, env);
        }
    }

    /// <summary>
    /// The s4 flag packing of the fire paths: bits 0x80/0x200/0x400 of the slot flag
    /// word map to 0x80000/0x200/1 (8005F6E8.c:193-199). Kept public for tests and for
    /// the future spawner ports that receive it as their last argument.
    /// </summary>
    public static uint PackSpawnFlags(ushort flags)
    {
        uint s4 = (uint)(((flags & 0x80) != 0) ? 1 : 0) << 19; // 8005F6E8.c:193
        if ((flags & 0x200) != 0)
            s4 |= 0x200;                                        // 8005F6E8.c:194-196
        if ((flags & 0x400) == 0)
            s4 |= 1;                                            // 8005F6E8.c:197-199
        return s4;
    }

    private static int FireCore(AttackSlot slot, EnemyEntity actor, EnemyEntity? a2, int a3, EnemyWorld env)
    {
        unchecked
        {
            AttackDefinition s2;
            EnemyEntity? s1;
            int ret = 0;
            bool s7;
            bool flagC8 = false;
            var vec = new int[3];
            var pos = new int[3];
            var vec2 = new int[3];

            s1 = slot.Target18;                                  // 8005F6E8.c:186
            if (s1 is not null && s1.Id != slot.Target18Value)   // 8005F6E8.c:187-189
                goto Fail;

            // Muzzle from the model part transform (8005F6E8.c:191).
            (pos[0], pos[1], pos[2]) = env.Muzzle(slot.Part23, actor);

            uint s4 = PackSpawnFlags(slot.Flags);
            s2 = slot.Definition!;                               // 8005F6E8.c:201
            s7 = s2.W1B != 0;                                    // 8005F6E8.c:202
            byte type = s2.Type;

            int aimCase = type switch
            {
                1 or 3 or 9 or 10 or 11 or 12 or 13 or 14 or 17 or 18 or 19 or 0x15 => 1,
                7 => 2,
                2 => 3,
                8 => 4,
                4 or 20 => 5,
                5 => 6,
                6 => 7,
                _ => 0,
            };
            switch (aimCase)
            {
                case 4: // type 8 falls through to 1..0x15 unless +0x84 set (8005F6E8.c:206-214)
                    if (actor.State84 != 0)
                    {
                        if ((a3 & 0xFF) == 0) goto SoundTail;
                        ret = env.OnSpawnJitterProjectile is null ? ThrowJitter() : env.OnSpawnJitterProjectile(null, 0, s4);
                        goto Tail;
                    }
                    goto case 1;
                case 1: // 8005F6E8.c:215-235
                    if (s1 is null) goto ActorVector;
                    vec[0] = s1.Vec5CX - pos[0];
                    vec[1] = s1.Vec5CY - pos[1];
                    vec[2] = s1.Vec5CZ - pos[2];
                    MechMath.Normalize(vec);
                    goto AimReady;
                case 2: // type 7 and the null-target path share the actor vector (8005F6E8.c:236-238)
                ActorVector:
                    vec[0] = actor.FacingX; vec[1] = actor.FacingY; vec[2] = actor.FacingZ;
                    goto AimReady;
                case 3: // type 2: lead prediction (8005F6E8.c:241-257)
                    if (s1 is null) goto ActorVector;
                    if (s1.Mode16 == 1 && s1.Timer324 > 0) goto case 1;
                    if (s1.Mode16 == 0x12 && s1.TimerB4Sub324 > 0) goto case 1;
                    LeadDirectionSolver.Solve(pos, vec, (short)s2.W12 << 4, (short)s2.W16 << 4,
                        new[] { s1.Vec5CX, s1.Vec5CY, s1.Vec5CZ },
                        new[] { s1.HeadingX, s1.HeadingY, s1.HeadingZ }, s1.State84);
                    goto AimReady;
                case 5: // types 4/20 (8005F6E8.c:259-267)
                    if ((a3 & 0xFF) == 0) goto SoundTail;
                    ret = env.OnSpawnLobShot is null ? ThrowLob() : env.OnSpawnLobShot(type, s2.W1B);
                    goto Tail;
                case 6: // type 5 (8005F6E8.c:269-275)
                    if ((a3 & 0xFF) == 0) goto SoundTail;
                    ret = env.OnSpawnStaticProjectile is null ? ThrowStatic() : env.OnSpawnStaticProjectile((int)s2.W0C);
                    goto Tail;
                case 7: // type 6 (8005F6E8.c:277-283)
                    if ((a3 & 0xFF) == 0) goto SoundTail;
                    ret = env.OnSpawnTargetedShot is null ? ThrowTargeted() : env.OnSpawnTargetedShot((int)s2.W0C);
                    goto Tail;
                default:
                    goto AimReady; // 8005F6E8.c:285-286
            }

        AimReady:
            // Jitter (8005F6E8.c:289-300).
            if ((slot.Flags & 0x8) != 0)
            {
                uint rnd = env.Random();
                vec[1] = (int)((uint)vec[1] + ((rnd & 0x1FFF) - 0x1000)); // 8005F6E8.c:290-293
            }
            if ((slot.Flags & 0x10) != 0)
            {
                uint r = env.Random();
                vec[0] = (int)((uint)vec[0] + ((r & 0x1FFF) - 0x1000));       // 8005F6E8.c:295-300
                vec[2] = (int)((uint)vec[2] + (((r >> 16) & 0x1FFF) - 0x1000));
            }

            // Angle gate (8005F6E8.c:302-320).
            if ((slot.Flags & 0x807) != 0)
            {
                int dist = MechMath.Dot(actor.FacingX, actor.FacingY, actor.FacingZ, vec[0], vec[1], vec[2]);
                if ((slot.Flags & 0x800) != 0)
                {
                    if (dist <= 0xF22F)
                        goto Fail;                               // 8005F6E8.c:304-307
                }
                else
                {
                    int mask = dist > 0xEC82 ? 1
                        : dist <= unchecked((int)0xFFFF137D) ? 2
                        : 4;                                    // 8005F6E8.c:309-315
                    if ((slot.Flags & mask) != 0)
                        goto Fail;                               // 8005F6E8.c:316-319
                }
            }

            switch (type)
            {
                case 12: // 8005F6E8.c:323-344
                    if ((a3 & 0xFF) == 0) goto SoundTail;
                    if (s1 is null)
                        throw new NotImplementedException(
                            "EnemyWeaponFire.cs: type 12 without a cached target (8005F6E8.c:328 computes &s1->unk5C) is not part of this headless port");
                    if (env.TraceMapRay(actor, s1!) == 0)
                    {
                        // Retail keeps the traced impact corner in vec2 on a hit; only the miss
                        // branch (8005F6E8.c:328-336) fills it - with the target's position words
                        // +0x50, not the aim words +0x5C (8005F6E8.c:329).
                        vec2[0] = s1.PositionX; vec2[1] = s1.PositionY; vec2[2] = s1.PositionZ;
                        env.TargetMessage(s1!);                              // 8005F6E8.c:330-336
                    }
                    s7 = false;                                   // 8005F6E8.c:338
                    int t = env.SpawnTracerBeam(0x140, 0x60, 0x30000, TracerCoreColour);
                    if ((byte)t != 0)
                    {
                        flagC8 = s2.W1A != 0;                      // 8005F6E8.c:340-343
                        goto Tail;
                    }
                    goto SoundTailOnly;                           // 8005F6E8.c:344 (FE2C)

                case 14: // 8005F6E8.c:346-352
                    if ((a3 & 0xFF) == 0) goto SoundTail;
                    s7 = false;
                    goto SoundTailOnly;

                case 17: // 8005F6E8.c:354-360
                    if ((a3 & 0xFF) == 0) goto SoundTail;
                    s7 = false;
                    goto SoundTailOnly;

                case 10:
                case 11: // 8005F6E8.c:362-370
                    if ((a3 & 0xFF) == 0) goto SoundTail;
                    env.OnFireRayAttack?.Invoke((byte)(type == 11 ? 8 : 1), 0); // gFireRayAttack func_8005F018
                    ret = 0;
                    goto Tail;

                case 8: // 8005F6E8.c:372-377
                    if ((a3 & 0xFF) == 0) goto SoundTail;
                    ret = env.OnSpawnJitterProjectile is null ? ThrowJitter() : env.OnSpawnJitterProjectile(vec, 0, s4);
                    goto Tail;

                case 9: // 8005F6E8.c:379-385
                    if ((a3 & 0xFF) == 0) goto SoundTail;
                    ret = env.OnSpawnJitterProjectile is null ? ThrowJitter() : env.OnSpawnJitterProjectile(vec, 1, s4);
                    goto Tail;

                case 13: // 8005F6E8.c:387-394
                    if ((a3 & 0xFF) == 0) goto SoundTail;
                    ret = 0;
                    s7 = false;
                    goto SoundTailOnly;

                default: // 8005F6E8.c:396-417
                    if (s1 is not null && env.TraceMapRay(actor, s1) != 0)
                        goto Fail;                               // 8005F6E8.c:397-401
                    if ((a3 & 0xFF) == 0) goto SoundTail;
                    if (type != 3 && type != 0x12 && type != 0x13 && type != 0x15)
                        s1 = null;                                // 8005F6E8.c:405-408
                    int temp = 0;
                    if ((slot.Flags & 0x1000) != 0) temp = 1;      // 8005F6E8.c:409-413
                    if ((slot.Flags & 0x2000) != 0) temp |= 2;
                    ret = env.SpawnDirectShot(new DirectSpawnArgs( // 8005F6E8.c:414-416
                        slot, s2, pos[0], pos[1], pos[2], vec[0], vec[1], vec[2], s4,
                        type, slot.TargetClass06, (short)slot.Value08, (byte)s2.W1B, temp, s1)
                    {
                        ShooterRecordId = actor.Word14,
                        Kind = actor.Type18,
                    });
                    goto Tail;
            }

        Tail: // 8005F6E8.c:420-449
            if ((a3 & 0xFF) == 0) goto SoundTail;
            slot.Playing22 = 0;                                    // 8005F6E8.c:426 (asm 93221)
            if (slot.AmmoGate20 >= 0)                              // 8005F6E8.c:427-429 (asm 93218-93223)
                slot.AmmoGate20 = (short)(slot.AmmoGate20 - 1);
            if (s7)
            {
                if (ret != 0)
                {
                    env.TypeAtOwnerSpawns.Add(new TypeAtOwnerSpawn( // 8005F6E8.c:432-441
                        s2.W1B, 4, ret, Array.Empty<int>(), 0));
                }
                else
                {
                    env.TypeAtOwnerSpawns.Add(new TypeAtOwnerSpawn( // 8005F6E8.c:436-441 (objBuf+0x10 path)
                        s2.W1B, 4, 0, new[] { pos[0], pos[1], pos[2] }, 4));
                }
            }
            if (flagC8)
            {
                env.TypeAtOwnerSpawns.Add(new TypeAtOwnerSpawn(     // 8005F6E8.c:444-448 (addr 0x8005FE80 tail)
                    s2.W1A, 5, 0, new[] { vec2[0], vec2[1], vec2[2] }, 0x800));
            }

        SoundTail: // 8005F6E8.c:451-453
            env.PlaySound(s2.W1C, actor.PositionX, actor.PositionZ, -1);
            return 1;

        SoundTailOnly:
            env.PlaySound(s2.W1C, actor.PositionX, actor.PositionZ, -1);
            return 1;

        Fail: // 8005F6E8.c:455-462
            if ((a3 & 0xFF) != 0)
            {
                slot.Playing22 = 0;                                // 8005F6E8.c:457 (asm 93213)
                slot.Burst26++;                                    // 8005F6E8.c:458 (asm 93211-93220)
                uint rnd = env.Random();                           // 8005F6E8.c:459-460 (asm 93284-93290)
                slot.FrameAcc24 = (ushort)(slot.FrameAcc24 - ((rnd + 0xC8) & 0xFF));
            }
            return 0;
        }
    }

    private static int ThrowJitter() => throw new NotImplementedException(
        "EnemyWeaponFire.cs: func_8005EE04 jitter projectile spawn (8005F6E8.c:376) is not part of this headless port");
    private static int ThrowLob() => throw new NotImplementedException(
        "EnemyWeaponFire.cs: func_8005E9E4 lob shot spawn (8005F6E8.c:264) is not part of this headless port");
    private static int ThrowStatic() => throw new NotImplementedException(
        "EnemyWeaponFire.cs: func_8005ECB8 static projectile spawn (8005F6E8.c:273) is not part of this headless port");
    private static int ThrowTargeted() => throw new NotImplementedException(
        "EnemyWeaponFire.cs: func_8005EB74 targeted shot spawn (8005F6E8.c:281) is not part of this headless port");
}
