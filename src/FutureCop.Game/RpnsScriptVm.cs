using System.Buffers.Binary;

namespace FutureCop.Game;

/// <summary>
/// Extracts the raw <c>RPNS</c> (0x52504E53) bytecode pool from a PS1 mission (<c>.MIS</c>) file
/// (SHOC/SHDR/SDAT framing, matching <c>80024EC8.c</c> and <see cref="MissionEntities"/>).
/// </summary>
public sealed class RpnsPool
{
    private const uint TagShoc = 0x53484F43u;
    private const uint TagShdr = 0x53484452u;
    private const uint TagSdat = 0x53444154u;
    private const uint TagFill = 0x46494C4Cu;
    private const uint TagRpns = 0x52504E53u;
    private const int SectorStride = 0x6000;

    private readonly byte[] _bytes;

    /// <summary>Creates a pool wrapping an already-extracted RPNS bytecode buffer.</summary>
    public RpnsPool(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        _bytes = bytes;
    }

    /// <summary>Raw RPNS pool bytes.</summary>
    public ReadOnlySpan<byte> Bytes => _bytes;

    /// <summary>Total length of the RPNS bytecode pool in bytes.</summary>
    public int Length => _bytes.Length;

    /// <summary>
    /// Returns a slice starting at <paramref name="offset"/> within the RPNS pool, or an empty
    /// span if <paramref name="offset"/> is out of range.
    /// </summary>
    public ReadOnlySpan<byte> Slice(int offset) =>
        (uint)offset < (uint)_bytes.Length ? _bytes.AsSpan(offset) : ReadOnlySpan<byte>.Empty;

    /// <summary>
    /// Extracts the <c>RPNS</c> payload from a mission file's raw bytes. Returns <c>null</c> if
    /// no <c>RPNS</c> SHOC/SHDR/SDAT block is present.
    /// </summary>
    public static RpnsPool? Extract(ReadOnlySpan<byte> missionBytes)
    {
        for (int off = 0; off + 0x20 <= missionBytes.Length; off += 4)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[off..]) != TagShoc)
                continue;
            if (BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(off + 0x10)..]) != TagShdr)
                continue;
            if (BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(off + 0x18)..]) != TagRpns)
                continue;

            int headerLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(off + 4)..]);
            int pkt = off + headerLen;
            if (pkt + 4 <= missionBytes.Length &&
                BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[pkt..]) == TagFill)
            {
                pkt = (pkt / SectorStride + 1) * SectorStride;
            }
            if (pkt + 20 > missionBytes.Length)
                return null;
            if (BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[pkt..]) != TagShoc ||
                BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(pkt + 0x10)..]) != TagSdat)
            {
                return null;
            }

            int pktLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(missionBytes[(pkt + 4)..]);
            if (pktLen < 20 || pkt + pktLen > missionBytes.Length)
                return null;

            return new RpnsPool(missionBytes.Slice(pkt + 20, pktLen - 20).ToArray());
        }
        return null;
    }
}

/// <summary>
/// Mutable runtime state backing <see cref="RpnsScriptVm"/> (mirrors the global banks read
/// and written by <c>gInterpretEntityScript</c> in <c>80069D68.c:9-15</c>).
/// </summary>
public sealed class RpnsVmState
{
    /// <summary>gGameSession[3]: active player count (1 = 1P vs Sky Captain, 2 = 2P split-screen).</summary>
    public const int SessionPlayerCount = 3;

    /// <summary>gGameSession[25] (+0x64): Player 1 / Blue team score points.</summary>
    public const int SessionScoreP1 = 25;

    /// <summary>gGameSession[26] (+0x68): Player 2 / Red team (Sky Captain) score points.</summary>
    public const int SessionScoreP2 = 26;

    /// <summary>gGameSession[27] (+0x6C): Player 1 active tank count (capped at &lt; 20).</summary>
    public const int SessionTanksP1 = 27;

    /// <summary>gGameSession[28] (+0x70): Player 1 active heli count (capped at &lt; 20).</summary>
    public const int SessionHelisP1 = 28;

    /// <summary>gGameSession[29] (+0x74): Player 2 active tank count (capped at &lt; 20).</summary>
    public const int SessionTanksP2 = 29;

    /// <summary>gGameSession[30] (+0x78): Player 2 active heli count (capped at &lt; 20).</summary>
    public const int SessionHelisP2 = 30;

    /// <summary>Session pointer bank slot written with SFX feedback (63 = bought, 73 = denied).</summary>
    public const int PointerBankSfxSlot = 3;

    /// <summary>Feedback value written to pointer-bank slot 3 on a successful purchase.</summary>
    public const int SfxPurchaseAccepted = 63;

    /// <summary>Feedback value written to pointer-bank slot 3 on a rejected purchase.</summary>
    public const int SfxPurchaseDenied = 73;

    /// <summary>gGameSession (D_8009FA94) integer bank.</summary>
    public int[] GameSession { get; }

    /// <summary>
    /// Dereferenced targets of the pointer bank <c>*(s32 *)(gGameSession[a + 0x39])</c>
    /// (<c>80069D68.c:222-224,234-236</c>).
    /// </summary>
    public int[] SessionPointerBank { get; }

    /// <summary>gScriptVmVars (D_8009FAD4) integer bank (tank spawn queues and dreadnought latches).</summary>
    public int[] ScriptVars { get; }

    /// <summary>gVmCountdowns (D_8009F9A4) bank; stored pre-multiplied by 5 (<c>80069D68.c:226,238</c>).</summary>
    public int[] Countdowns { get; }

    /// <summary>gOpcodeTable (D_8009F9F8) 32-entry table indexed by op 11 for non-negative operands (<c>80069D68.c:298-300</c>).</summary>
    public int[] OpcodeTable { get; }

    /// <summary>Optional PRNG source for op 11 with negative operands (<c>func_80019668</c>, <c>80069D68.c:302-305</c>).</summary>
    public Func<int>? RandomSource { get; set; }

    /// <summary>gScriptVmDisabled (D_8009F840): when true, scripts return 0 immediately (<c>80069D68.c:166</c>).</summary>
    public bool Disabled { get; set; }

    /// <summary>Invoked by op 60 (<c>func_8006158C</c>, <c>80069D68.c:395-397</c>): (spawnerId, cmd, arg2).</summary>
    public Action<int, int, int>? OnSpawnerCommandById { get; set; }

    /// <summary>Invoked by op 56 (<c>func_8004DC64</c>, <c>80069D68.c:379-385</c>): (tag, op, arg).</summary>
    public Action<int, int, int>? OnCommandByTag { get; set; }

    /// <summary>Invoked by op 57 (<c>func_8004DD68</c>, <c>80069D68.c:389-391</c>): (group, op, arg).</summary>
    public Action<int, int, int>? OnCommandByGroup { get; set; }

    /// <summary>Invoked by op 12 (<c>func_800298EC</c>, <c>80069D68.c:317-319</c>): (eventId).</summary>
    public Action<int>? OnAppendEvent { get; set; }

    /// <summary>Invoked by op 30 (<c>80069D68.c:234-236</c>): (slot, value).</summary>
    public Action<int, int>? OnSessionPointerWrite { get; set; }

    public RpnsVmState(int sessionSize = 64, int pointerBankSize = 16, int varsSize = 64, int countdownsSize = 32)
    {
        GameSession = new int[sessionSize];
        SessionPointerBank = new int[pointerBankSize];
        ScriptVars = new int[varsSize];
        Countdowns = new int[countdownsSize];
        OpcodeTable = new int[32];
        Array.Fill(Countdowns, -10); // func_80069B58 default
    }
}

/// <summary>
/// Stack-based RPNS bytecode interpreter mirroring <c>gInterpretEntityScript</c>
/// (<c>func_80069D68</c>, <c>80069D68.c:146-416</c>) for the Precinct Assault purchase, tank-queue,
/// and outpost-capture script paths.
/// </summary>
public static class RpnsScriptVm
{
    private const int StackCapacity = 64;

    /// <summary>
    /// Executes a NUL-terminated RPNS bytecode sequence against <paramref name="state"/>.
    /// Mirrors <c>80069D68.c:146-416</c> for all opcodes used by purchase, tank-queue, and
    /// outpost-capture scripts in <c>CONFT.MIS</c>.
    /// </summary>
    /// <param name="script">Bytecode slice starting at the first opcode byte.</param>
    /// <param name="state">Mutable VM variable banks and engine callbacks.</param>
    /// <param name="arg1">Caller operand exposed via opcode 5 (<c>80069D68.c:216-218</c>).</param>
    /// <param name="arg2">Optional initial stack value pushed when &gt;= 0 (<c>80069D68.c:172-175</c>).</param>
    /// <returns>1 for an empty script, 0 when disabled, or the final top-of-stack word.</returns>
    public static int Execute(ReadOnlySpan<byte> script, RpnsVmState state, int arg1 = 0, int arg2 = -1)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (script.IsEmpty || script[0] == 0)
            return 1; // 80069D68.c:163-165
        if (state.Disabled)
            return 0; // 80069D68.c:166-168

        Span<int> stack = stackalloc int[StackCapacity];
        int depth = 1;
        stack[0] = depth; // 80069D68.c:170-171
        if (arg2 >= 0)
        {
            stack[1] = arg2;
            depth = 2; // 80069D68.c:172-175
        }

        int pc = 0;
        do
        {
            int op = script[pc++]; // 80069D68.c:179
            if ((op & 0x80) != 0)
            {
                stack[depth++] = op & 0x7F; // 80069D68.c:180-183
                continue;
            }

            int a = 0, b = 0, c = 0;
            if (op >= 9)
            {
                a = stack[--depth]; // 80069D68.c:184-186
                if (op >= 0x1D)
                {
                    b = stack[--depth]; // 80069D68.c:187-189
                    if (op >= 0x38)
                    {
                        c = stack[--depth]; // 80069D68.c:190-194
                    }
                }
            }

            bool push = false;
            switch (op)
            {
                // Immediates (80069D68.c:198-218)
                case 1: a = ~script[pc++]; push = true; break;
                case 2: a = script[pc++] + 0x80; push = true; break;
                case 3: a = (script[pc++] << 8) | script[pc++]; push = true; break;
                case 4:
                    a = (script[pc++] << 24) | (script[pc++] << 16) | (script[pc++] << 8) | script[pc++];
                    push = true;
                    break;
                case 5: a = arg1; push = true; break;

                // Control flow (80069D68.c:320-327, 367-371)
                case 8: pc += script[pc]; break;
                case 20:
                    if (a == 0) pc += script[pc];
                    else pc++;
                    break;
                case 46:
                    if (b == 0) return a;
                    break;

                // Unary logic (80069D68.c:291-293)
                case 9: a = a == 0 ? 1 : 0; push = true; break;

                // Random / gOpcodeTable lookup (80069D68.c:297-307)
                case 11:
                    if (a >= 0)
                    {
                        a = state.OpcodeTable[a % state.OpcodeTable.Length];
                    }
                    else
                    {
                        int r = state.RandomSource?.Invoke() ?? 0;
                        int sign = r >> 31;
                        int mag = (sign + r) ^ sign;
                        a = mag % (-a);
                    }
                    push = true;
                    break;

                // Engine events (80069D68.c:317-319)
                case 12: state.OnAppendEvent?.Invoke(a); break;

                // Variable loads (80069D68.c:219-230)
                case 16: a = state.GameSession[a]; push = true; break;
                case 17: a = state.SessionPointerBank[a]; push = true; break;
                case 18: a = state.Countdowns[a] / 5; push = true; break;
                case 19: a = state.ScriptVars[a]; push = true; break;

                // Variable stores (80069D68.c:231-242)
                case 29: state.GameSession[a] = b; break;
                case 30:
                    state.SessionPointerBank[a] = b;
                    state.OnSessionPointerWrite?.Invoke(a, b);
                    break;
                case 31: state.Countdowns[a] = b * 5; break;
                case 32: state.ScriptVars[a] = b; break;

                // Variable inc/dec — note 80069D68.c:243-266 pushes index `a` back on stack
                case 21: state.GameSession[a]++; push = true; break;
                case 24: state.ScriptVars[a]++; push = true; break;
                case 25: state.GameSession[a]--; push = true; break;
                case 28: state.ScriptVars[a]--; push = true; break;

                // Variable add/sub assign — note 80069D68.c:267-290 pushes index `a` back on stack
                case 48: state.GameSession[a] += b; push = true; break;
                case 51: state.ScriptVars[a] += b; push = true; break;
                case 52: state.GameSession[a] -= b; push = true; break;
                case 55: state.ScriptVars[a] -= b; push = true; break;

                // Comparisons, arithmetic, boolean (80069D68.c:328-366)
                // Note stack order: `b` was pushed first, `a` second.
                case 33: a = b == a ? 1 : 0; push = true; break;
                case 34: a = b != a ? 1 : 0; push = true; break;
                case 35: a = a < b ? 1 : 0; push = true; break;   // 80069D68.c:335 (b > a)
                case 36: a = a <= b ? 1 : 0; push = true; break;  // 80069D68.c:338 (b >= a)
                case 37: a = b < a ? 1 : 0; push = true; break;   // 80069D68.c:341 (b < a)
                case 38: a = a >= b ? 1 : 0; push = true; break;  // 80069D68.c:344 (b <= a)
                case 39: a = a + b; push = true; break;
                case 40: a = b - a; push = true; break;
                case 41: a = a * b; push = true; break;
                case 42: a = a != 0 ? b / a : 0; push = true; break;
                case 43: a = a != 0 ? b % a : 0; push = true; break;
                case 44: a = (b != 0 && a != 0) ? 1 : 0; push = true; break;
                case 45: a = (b != 0 || a != 0) ? 1 : 0; push = true; break;

                // Three-operand engine commands on the purchase/outpost path (80069D68.c:379-397)
                case 56: state.OnCommandByTag?.Invoke(c, b, a); break;
                case 57: state.OnCommandByGroup?.Invoke(c, b, a); break;
                case 60: state.OnSpawnerCommandById?.Invoke(c, b, a); break;

                // Opcodes intentionally omitted (not reached on purchase/queue/outpost paths):
                // 10 (gRandomTable), 13 (gQueueScriptEvent),
                // 14/15 (gQueueEntityStreamRequest), 22/23/26/27/49/50/53/54 (ptr/cd arithmetic),
                // 47 (entity/tag query msg 0xC), 58 (cmd by team), 59 (path block),
                // 61 (spawner cmd by group), 62 (scene node cmd).
                default:
                    break;
            }

            if (push)
                stack[depth++] = a; // 80069D68.c:408-411
        }
        while (pc < script.Length && script[pc] != 0);

        // 80069D68.c:414-415 decrements depth and returns gOperandStack[depth] without a bounds
        // check (reading the word before D_800A3320 on underflow). Return 0 if an unmodelled
        // opcode or malformed script ever empties the operand stack.
        return depth > 0 ? stack[depth - 1] : 0;
    }
}
