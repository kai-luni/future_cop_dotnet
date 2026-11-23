namespace FutureCop.Game;

/// <summary>Port of func_8003532C gUpdateStrategicDrone - the message-6 tick of the type-37 drone (src/game/8003532C.c:119-233).</summary>
public sealed class StrategicDroneTick
{
    /// <summary>AI record view (entity +0x134 -> struct State, 8003532C.c:8-23).</summary>
    public sealed class AiState
    {
        public int[] Pos = new int[3];     // +0x0C strategic node position
        public int TickTimer;              // +0x18 strategy re-think timer
        public byte Mode;                  // +0x1A AI mode (1 and 2 halve the limit)
        public short Limit;                // +0x1E approach limit
        public short PromoteTo;            // +0x20 the +0xF8 promotion word
        public int A4Mirror;               // +0xA4 mirror of entity +0x1C
    }

    /// <summary>Nearest type-16 fly-in node (func_80035184 result view).</summary>
    public sealed class Type16Unit
    {
        public bool RetireFlagC8;          // +0xC8 bit 0x02000000 -> tear-down
        public int[] Pos = new int[3];     // +0x50 position copy source
    }

    /// <summary>Entity words of the drone block that func_8003532C reads.</summary>
    public sealed class DroneEntity
    {
        public int Unk1C;                  // +0x1C approach word
        public int[] Pos = new int[3];    // +0xDC position copy taken from targets
        public int UnkF4;                  // +0xF4 charge word
        public short UnkF8;                // +0xF8 cached promotion word
        public byte UnkFA;                 // +0xFA approach state
        public AiState Ai = new AiState();
    }

    /// <summary>World services for the strategic drone tick.</summary>
    public interface IWorld
    {
        int NextRandom();                              // func_80019668
        int Difficulty();                              // gGameSession +0x4C
        Type16Unit? FindNearest(DroneEntity self, out int distance); // func_80035184
        void ResetDroneApproach(DroneEntity self);     // func_8003526C
        void SetDroneAttackTarget(DroneEntity self);   // func_80033608(self, 0)
        void UpdateDroneStrategy(DroneEntity self);    // func_800336DC
        void RetireEntity(Type16Unit unit);            // func_8004D958
        void StepAttackScheduler(AiState ai, int which, DroneEntity self, int delta); // func_80060238
        void CopyUnitPos(DroneEntity self, Type16Unit unit); // self->pos = unit->unk50
        void UpdateHoverEnemy(DroneEntity self, int delta); // func_800324E0(message 6)
        int SessionWord { get; }                       // gGameSession->unk4C mirror for tests
    }

    /// <summary>Ticks one strategic drone (8003532C.c:119-233).</summary>
    public static void Tick(DroneEntity self, IWorld w, int delta)
    {
        unchecked
        {
            var ai = self.Ai;
            ai.A4Mirror = self.Unk1C;

            // Approach limit (:136-141): modes 1 and 2 halve it.
            int limit = ai.Limit;
            if (ai.Mode < 3 && ai.Mode != 0)
                limit >>= 1;

            if (self.Unk1C < limit)
            {
                // Planar octagonal distance entity -> AI node (:144-166).
                int adx = self.Pos[0] - ai.Pos[0];
                int sign = adx >> 31;
                int ax = (sign + adx) ^ sign;
                int adz = self.Pos[2] - ai.Pos[2];
                sign = adz >> 31;
                int az = (sign + adz) ^ sign;
                int min = ax < az ? ax : az;
                int dist = ax + az - (min >> 1);

                if (dist < 0x4000)
                {
                    w.ResetDroneApproach(self);
                    RunTimer(self, w, delta);
                    Final(self, w, delta);
                    return;
                }

                if (self.UnkFA == 0)
                {
                    // Acquire: take the AI node position, grow the charge word.
                    self.Pos[0] = ai.Pos[0];
                    self.Pos[1] = ai.Pos[1];
                    self.Pos[2] = ai.Pos[2];
                    self.UnkFA = 1;
                    self.UnkF4 += self.UnkF4 >> (4 - (w.Difficulty() >> 2));
                    w.SetDroneAttackTarget(self);
                    Final(self, w, delta);
                    return;
                }
                if (self.UnkFA == 1)
                {
                    Type16Unit? unit = w.FindNearest(self, out int d);
                    if (unit is not null)
                    {
                        if (d <= 0x13FFF && self.UnkF8 == ai.PromoteTo)
                            self.UnkF8 = (short)(ai.PromoteTo << 2);
                        if (d < 0x1000)
                        {
                            if (unit.RetireFlagC8)
                                w.RetireEntity(unit);
                            w.ResetDroneApproach(self);
                            RunTimer(self, w, delta);
                            Final(self, w, delta);
                            return;
                        }
                        if (d < dist)
                        {
                            w.CopyUnitPos(self, unit);
                            Final(self, w, delta);
                            return;
                        }
                        RunTimer(self, w, delta);
                        Final(self, w, delta);
                        return;
                    }
                    if (dist <= 0x13FFF && self.UnkF8 == ai.PromoteTo)
                        self.UnkF8 = (short)(ai.PromoteTo << 2);
                    self.Pos[0] = ai.Pos[0];
                    self.Pos[1] = ai.Pos[1];
                    self.Pos[2] = ai.Pos[2];
                    Final(self, w, delta);
                    return;
                }
                // Other approach states: timer only.
                RunTimer(self, w, delta);
                Final(self, w, delta);
                return;
            }

            RunTimer(self, w, delta);
            Final(self, w, delta);
        }
    }

    /// <summary>Timer block (:217-228): reload + strategic tick on negative.</summary>
    private static void RunTimer(DroneEntity self, IWorld w, int delta)
    {
        unchecked
        {
            var ai = self.Ai;
            ai.TickTimer -= delta;
            if (ai.TickTimer < 0)
            {
                int reload = (9 - w.Difficulty()) * 80 + 128;
                if (ai.Mode != 2)
                    reload += w.NextRandom() & 0xFF;
                else
                    reload -= w.NextRandom() & 0x1F;
                ai.TickTimer = reload;
                w.UpdateDroneStrategy(self);
            }
        }
    }

    /// <summary>Tail (:230-233): both attack descriptors then the flyer mover.</summary>
    private static void Final(DroneEntity self, IWorld w, int delta)
    {
        w.StepAttackScheduler(self.Ai, 0, self, delta);
        w.StepAttackScheduler(self.Ai, 1, self, delta);
        w.UpdateHoverEnemy(self, delta);
    }
}
