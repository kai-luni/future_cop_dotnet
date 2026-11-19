namespace FutureCop.Assets;

/// <summary>
/// Retail front-end tables used by the single-player Precinct Assault path, copied
/// byte-for-byte from the SLUS_007.39 executable image (load address 0x80010000,
/// file offset = address - 0x80010000 + 0x800). PrecinctAssaultDataTests compares
/// the tables against the executable when it is available.
/// </summary>
public static class PrecinctAssaultData
{
    /// <summary>D_8009B724: Crime War mission record indices (gMissionListTable).</summary>
    public const uint CrimeWarMissionListAddress = 0x8009B724;
    /// <summary>D_8009B72C: Precinct Assault mission record indices (gFrontEndListTable).</summary>
    public const uint PrecinctAssaultMissionListAddress = 0x8009B72C;
    public static readonly sbyte[] CrimeWarMissionList = { 9, 11, 5, 1, 13, 4, 2, 0 };
    public static readonly sbyte[] PrecinctAssaultMissionList = { 3, 7, 10, 15, 6 };

    /// <summary>D_80012094 (gMultiBrfMovieName), queued once by the Precinct Assault mode handlers.</summary>
    public const string MultiBrfMovieName = "MultiBrf.wve";
    /// <summary>D_8009F444, compared against the selected module name by func_8007B854.</summary>
    public const string Lax1ModuleName = "lax1";
    /// <summary>D_8009F488, the pending module name chosen when the selection equals D_8009F444.</summary>
    public const string Lax2ModuleName = "lax2";
    /// <summary>Retail initial contents of gFrontEndModuleName (D_8009B300).</summary>
    public const string InitialModuleName = "m3A";
    /// <summary>Retail initial value of gModuleLoadState (D_8009F3F0).</summary>
    public const byte InitialLoadScreen = 1;

    /// <summary>D_8009B4E4 (gWideTextTable): 16 records of 36 bytes, retail image bytes.</summary>
    public const uint MissionRecordTableAddress = 0x8009B4E4;
    public const int MissionRecordSize = 36;
    private static readonly string[] RecordHex =
    {
        "50f409807c2001807020018002000000200d2f0030000001940404040000000000010000",
        "4cf40980642001805820018003000000f80c31003200df00940203030000000000000000",
        "44f409804c20018040200180040000000c0d33003400bd00840304040000000000000000",
        "3cf40980000000003420018006001200480d3500360001016b0403030f017e0000000000",
        "38f40980282001801c20018007000000160d37003800f4007d0303040000000000000000",
        "34f40980102001800420018008000000020d39003a00d200720202020000000000000000",
        "2cf4098000000000f81f01801500160008523b003c00a70066040303b2006d0000010000",
        "24f4098000000000ec1f018009000f00520d3d003e00990058040303a200780000000000",
        "20f40980e01f0180d41f01800a000000340d3f004000fc00350101010000000000000000",
        "1cf40980c81f0180bc1f01800a000000e40c23002400e900590101010000000000000000",
        "18f4098000000000b01f01800b0010005c0d25002600cf0052040303e100610000000000",
        "14f40980a41f0180981f01800c000000ee0c27002800af003a0202010000000000000000",
        "10f40980000000000000000000000000000029002a00fd00350000000000000000000000",
        "08f409808c1f0180801f01800d0000002a0d2b002c00de00350303030000000000000000",
        "10f4098000000000000000000000000000002d002e00b400350000000000000000000000",
        "00f4098000000000741f01800e001100f80c31003200df0094040303f5009b0000000000",
    };

    /// <summary>Strings behind the record pointer fields (+0x00 name, +0x04 briefing, +0x08 reel movie).</summary>
    private static readonly string?[,] RecordStrings =
    {
        { "m4a1", "brfm4a1.wve", "pom4a1.wve" },
        { "ov", "brfov.wve", "poov.wve" },
        { "lax1", "brflax.wve", "polax.wve" },
        { "conft", null, "poconft.wve" },
        { "un", "brfun.wve", "poun.wve" },
        { "m3b", "brfm3b.wve", "pom3b.wve" },
        { "joke", null, "pojoke.wve" },
        { "slim", null, "poslim.wve" },
        { "e3", "brfe3.wve", "poe3.wve" },
        { "m2c", "brfm2c.wve", "pom2c.wve" },
        { "hk", null, "pohk.wve" },
        { "m3a", "brfm3a.wve", "pom3a.wve" },
        { "", null, null },
        { "m1a1", "brfm1a1.wve", "pom1a1.wve" },
        { "", null, null },
        { "ovmp", null, "poovmp.wve" },
    };

    public static readonly IReadOnlyList<MissionRecord> MissionRecords = Enumerable.Range(0, 16)
        .Select(i => new MissionRecord(i, Convert.FromHexString(RecordHex[i]),
            RecordStrings[i, 0] ?? "", RecordStrings[i, 1], RecordStrings[i, 2]))
        .ToArray();

    /// <summary>
    /// D_8009C030..D_8009C0A7: the four 6-byte-row loadout item lists, contiguous in retail
    /// memory. Lists start at D_8009C030 (gRowRecordTable, 5 rows), D_8009C050 (5 rows),
    /// D_8009C070 (5 rows, third column when gGameSession+0x5C != 1) and D_8009C090
    /// (4 rows, third column when +0x5C == 1). Row byte +0 is the value matched by
    /// func_8007D39C and stored into the session by func_8007E3EC.
    /// </summary>
    public const uint LoadoutRowsAddress = 0x8009C030;
    public static readonly byte[] LoadoutRows = Convert.FromHexString(
        "054d4c00ec2700424100742702050400922701222100d82703030200e2270000" +
        "15201f005028101110007e27120100008827131312003c281407060046280000" +
        "240d0c00b428204443009c2722525100a02821474600aa2823504f00a6270000" +
        "240d0c00b428204443009c2722525100a02823504f00a627");
    public const uint LoadoutList0Address = 0x8009C030;
    public const uint LoadoutList1Address = 0x8009C050;
    public const uint LoadoutList2SplitOffAddress = 0x8009C070;
    public const uint LoadoutList2SplitOnAddress = 0x8009C090;
    /// <summary>D_8009C0A8 (gLoadoutTable): retail initial item lists, counts and bounds.</summary>
    public const uint LoadoutTableAddress = 0x8009C0A8;
    public static readonly (uint Items, byte Count, byte Min, byte Max)[] InitialLoadoutTable =
    {
        (LoadoutList0Address, 5, 0, 0), (LoadoutList1Address, 5, 0, 0), (LoadoutList2SplitOffAddress, 5, 0, 0),
    };

    /// <summary>Byte +0 of loadout row <paramref name="row"/> in the list at <paramref name="listAddress"/>,
    /// read from the contiguous retail block like the retail index arithmetic.</summary>
    public static byte LoadoutRowValue(uint listAddress, int row)
    {
        long offset = (long)listAddress - LoadoutRowsAddress + row * 6L;
        if (offset < 0 || offset >= LoadoutRows.Length)
            throw new ArgumentOutOfRangeException(nameof(row), "row outside the retail loadout block");
        return LoadoutRows[offset];
    }

    public static sbyte[] MissionList(uint address) => address switch
    {
        CrimeWarMissionListAddress => CrimeWarMissionList,
        PrecinctAssaultMissionListAddress => PrecinctAssaultMissionList,
        _ => throw new ArgumentOutOfRangeException(nameof(address), $"unknown mission list 0x{address:X8}"),
    };
}

/// <summary>
/// One 36-byte retail mission record of D_8009B4E4. Only fields whose use is shown in
/// the C sources are named; <see cref="Raw"/> keeps the full record.
/// </summary>
public sealed record MissionRecord(int Index, byte[] Raw, string Name, string? BriefingMovie, string? ReelMovie)
{
    /// <summary>+0x0C s16: loading-screen index copied to D_8009F3F0 by func_8007B854 when non-zero.</summary>
    public short LoadScreen => BitConverter.ToInt16(Raw, 0x0C);
    /// <summary>+0x0E: loading-screen index used instead when the record state (+0x1F) is 10.</summary>
    public byte FinishedLoadScreen => Raw[0x0E];
    /// <summary>+0x19..+0x1B: upper bounds of the three loadout columns (func_8007D408).</summary>
    public byte LoadoutMax0 => Raw[0x19];
    public byte LoadoutMax1 => Raw[0x1A];
    public byte LoadoutMax2 => Raw[0x1B];
    /// <summary>+0x1F: state byte (10 = finished, see func_80077FFC, func_8007B854).</summary>
    public byte InitialState => Raw[0x1F];
    /// <summary>+0x20: second state byte, set to 10 together with +0x1F by the unlock-all cheat.</summary>
    public byte InitialState2 => Raw[0x20];
}
