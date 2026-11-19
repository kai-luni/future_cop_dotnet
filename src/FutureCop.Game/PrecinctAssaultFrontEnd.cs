namespace FutureCop.Assets;

/// <summary>Front-end menu descriptors named by their retail addresses (docs/precinct-assault-path.md).</summary>
public static class PrecinctAssaultMenus
{
    public const uint MainMenu = 0x800A3808;          // D_800A3808 gMenuDescriptor
    public const uint PlayerSetup = 0x800A37A8;       // D_800A37A8
    public const uint BriefingSkip = 0x800A3688;      // D_800A3688, bypassed by func_8007B004
    public const uint Hub1P = 0x800A38C8;             // D_800A38C8, pushed by func_8007A5F0 (1P split)
    public const uint Hub2PSplit = 0x800A3898;        // D_800A3898, pushed by func_8007A5F0 (2P menu)
    public const uint SetupNoSplit = 0x800A3868;      // D_800A3868, pushed by func_8007A5F0 when +0x5C == 0
    public const uint LevelSelect = 0x800A3838;       // D_800A3838 gScreenDescriptor
    public const uint Loadout1P = 0x800A3628;         // D_800A3628
    public const uint Loadout2P = 0x800A3658;         // D_800A3658
}

/// <summary>The 0xF4-byte session block behind gGameSession (D_8009FA94); only proven fields.</summary>
public sealed class PrecinctAssaultSession
{
    /// <summary>+0x08: player option copied to/from D_8009F8F0.</summary>
    public int PlayerOption { get; set; }
    /// <summary>+0x0C: player count (1 or 2).</summary>
    public int Players { get; set; }
    /// <summary>+0x28..+0x3C: six loadout values (columns 0..2 for player 0, then player 1).</summary>
    public int[] Loadout { get; } = new int[6];
    /// <summary>+0x4C: level row chosen in the level-select sub list.</summary>
    public int LevelRow { get; set; }
    /// <summary>+0x5C: game type, 1 for Precinct Assault, 0 for Crime War.</summary>
    public int Split { get; set; }
    /// <summary>+0x8C/+0x90/+0x94: when non-zero the loadout column minimum is 0, else 1.</summary>
    public int[] LoadoutColumnUnlocked { get; } = new int[3];
    /// <summary>+0x98: second player option copied to/from D_8009F8F4.</summary>
    public int PlayerOption2 { get; set; }
}

/// <summary>One gLoadoutTable (D_8009C0A8) entry: item list, row count, clamp bounds.</summary>
public sealed class PrecinctAssaultLoadoutColumn
{
    public uint Items { get; set; }
    public byte Count { get; set; }
    public byte Min { get; set; }
    public byte Max { get; set; }
}

/// <summary>
/// Single-player Precinct Assault front-end state, ported from the retail C units listed on
/// each method. UI-free: menu pushes, sounds and draw requests are recorded as events.
/// Fields mirror retail globals; defaults are the retail boot image.
/// </summary>
public sealed class PrecinctAssaultFrontEnd
{
    public PrecinctAssaultSession Session { get; } = new();

    public byte FrontEndListFlag;        // D_8009F3C0 unlock-all cheat
    public byte UnlockFlag;              // D_8009F3C2
    public sbyte SelectedMission;        // D_8009F880
    public sbyte ListMin;                // D_8009F881
    public sbyte ListMax;                // D_8009F882
    public uint ListTable;               // D_8009F884 (retail address of the active list)
    public byte ListCount;               // D_8009F888
    public byte ListMode;                // D_8009F889 (0 Crime War, 1 Precinct Assault)
    public byte CrimeWarProgress;        // D_8009F458
    public byte SavedCrimeWarProgress;   // D_8009F3F1
    public byte PrecinctProgress;        // D_8009F459
    public int SelectLock;               // D_800A35CC
    public byte BriefingShown;           // D_8009F3E6 gLayoutPointerFlagA
    public int DeferredTransition;       // D_8009F8C8
    public string? PendingMovie;         // D_8009F8C4
    public int PlayerSelection;          // D_8009F8F0
    public int PlayerSelection2;         // D_8009F8F4
    public int[] Ready = new int[2];     // D_8009F8E0 / D_8009F8E4
    public int ActivePlayer;             // D_8009F8B8
    public sbyte[] MenuDepth = new sbyte[2]; // D_8009F8C0 / D_8009F8C1
    public sbyte SelectedRow;            // D_8009F87E
    public sbyte LineCount;              // D_8009F87F
    public byte UiHold;                  // D_8009F8BD
    public string ModuleName = PrecinctAssaultData.InitialModuleName; // D_8009B300
    public string? PendingModule;        // D_8009F3EC
    public byte LoadScreen = PrecinctAssaultData.InitialLoadScreen;  // D_8009F3F0
    public byte ReturnMode;              // D_8009F45A (1 Crime War, 2 Precinct Assault)
    public byte RowClear;                // D_8009FA10
    public int ScreenState;              // D_8009FA00 (5 = leave the front end, start the level)
    /// <summary>D_8009F924/926/928: loadout row index [column, player].</summary>
    public sbyte[,] LoadoutSelection = new sbyte[3, 2];
    public PrecinctAssaultLoadoutColumn[] LoadoutTable = PrecinctAssaultData.InitialLoadoutTable
        .Select(t => new PrecinctAssaultLoadoutColumn { Items = t.Items, Count = t.Count, Min = t.Min, Max = t.Max })
        .ToArray();
    public short ScaleP1 = 100, ScaleP2 = 100;                 // D_8009BB66 / D_8009BBBA
    public short LoadoutScaleP1 = 100, LoadoutScaleP2 = 100;   // D_8009B7E6 / D_8009B856
    /// <summary>Mission record state bytes +0x1F/+0x20 (mutable; save data may change them).</summary>
    public byte[] RecordState = PrecinctAssaultData.MissionRecords.Select(r => r.InitialState).ToArray();
    public byte[] RecordState2 = PrecinctAssaultData.MissionRecords.Select(r => r.InitialState2).ToArray();

    /// <summary>D_8009F468: active menu descriptor address.</summary>
    public uint ActiveMenu = PrecinctAssaultMenus.MainMenu;
    /// <summary>Bytes of menu descriptors keyed by retail address (e.g. 0x800A37AB, 0x800A362B).</summary>
    public Dictionary<uint, byte> MenuBytes { get; } = new();
    /// <summary>Descriptor +0x24 parent links keyed by descriptor address.</summary>
    public Dictionary<uint, uint> MenuParent { get; } = new();

    // Events recorded instead of calling UI/sound code.
    public List<uint> PushedMenus { get; } = new();          // func_80078650(menu, 1)
    public List<int> Sounds { get; } = new();                // func_80046C84(id, 0)
    public List<(int Id, int Slot)> QueuedPrompts { get; } = new(); // func_80078B34
    public int ProfileSlotDraws;                             // func_8007B460

    public byte GetMenuByte(uint address) => MenuBytes.TryGetValue(address, out var b) ? b : (byte)0;
    public void SetMenuByte(uint address, byte value) => MenuBytes[address] = value;
    private sbyte ActiveMenuByte(int offset) => (sbyte)GetMenuByte(ActiveMenu + (uint)offset);

    public MissionRecord SelectedRecord =>
        PrecinctAssaultData.MissionRecords[PrecinctAssaultData.MissionList(ListTable)[SelectedMission]];
    private int SelectedRecordIndex => SelectedRecord.Index;

    /// <summary>func_800776B8 gSetupCrimeWarMissionList.</summary>
    public void SetupCrimeWarMissionList()
    {
        SelectedMission = 0; ListMin = 0; ListMode = 0;
        if (FrontEndListFlag != 0) { ListMax = 7; CrimeWarProgress = 8; SavedCrimeWarProgress = 8; }
        else ListMax = 0;
        ListTable = PrecinctAssaultData.CrimeWarMissionListAddress;
        ListCount = 8;
    }

    /// <summary>func_80077708 gSetupPrecinctAssaultMissionList.</summary>
    public void SetupPrecinctAssaultMissionList()
    {
        SelectedMission = 0; ListMin = 0; ListMode = 1;
        if (FrontEndListFlag != 0)
        {
            foreach (var r in PrecinctAssaultData.PrecinctAssaultMissionList) { RecordState[r] = 10; RecordState2[r] = 10; }
            UpdatePrecinctUnlockFlag();
        }
        ListTable = PrecinctAssaultData.PrecinctAssaultMissionListAddress;
        ListMax = (sbyte)(UnlockFlag + 3);
        ListCount = (byte)(UnlockFlag + 4);
    }

    /// <summary>func_80077FFC gUpdatePrecinctUnlockFlag: the fifth map unlocks when the first four are state 10.</summary>
    public void UpdatePrecinctUnlockFlag()
    {
        var l = PrecinctAssaultData.PrecinctAssaultMissionList;
        UnlockFlag = (byte)(RecordState[l[0]] == 10 && RecordState[l[1]] == RecordState[l[0]]
            && RecordState[l[2]] == RecordState[l[1]] && RecordState[l[3]] == RecordState[l[2]] ? 1 : 0);
    }

    /// <summary>func_8007A4CC gSelectPrecinctAssault1P (main-menu entry 3).</summary>
    public void SelectPrecinctAssault1P(int action)
    {
        if (action != 3) return;
        if (SelectLock != 0) { Sounds.Add(0x4C); return; }
        Session.Players = 1; Session.Split = 1;
        SetMenuByte(0x800A377D, 1);
        PrecinctProgress = 0;
        SetupPrecinctAssaultMissionList();
        QueueBriefing();
        SetMenuByte(0x800A368B, 9);
    }

    /// <summary>func_8007A56C gSelectPrecinctAssault2P (main-menu entry 4).</summary>
    public void SelectPrecinctAssault2P(int action)
    {
        if (action != 3) return;
        Session.Players = 2; Session.Split = 1;
        SetMenuByte(0x800A377D, 0);
        PrecinctProgress = 0;
        SetupPrecinctAssaultMissionList();
        QueueBriefing();
        SetMenuByte(0x800A368B, 5);
    }

    private void QueueBriefing()
    {
        DeferredTransition = 5;
        PendingMovie = null;
        if (BriefingShown == 0) { BriefingShown = 1; PendingMovie = PrecinctAssaultData.MultiBrfMovieName; }
    }

    /// <summary>func_8007B004 gInitMissionSelectState (player-setup screen init).</summary>
    public void InitMissionSelectState()
    {
        PlayerSelection = Session.PlayerOption;
        Ready[0] = 0; Ready[1] = 0;
        ScaleP1 = 100; ScaleP2 = 100;
        PlayerSelection2 = Session.PlayerOption2;
        if (MenuParent.TryGetValue(ActiveMenu, out var parent) && parent == PrecinctAssaultMenus.BriefingSkip)
            MenuParent[ActiveMenu] = MenuParent.TryGetValue(parent, out var up) ? up : 0;
        if (Session.Split == 1)
        {
            if (Session.Players == 1) SetMenuByte(0x800A37AB, 9); else SetMenuByte(0x800A37DB, 3);
        }
        else
        {
            if (Session.Players == 1) SetMenuByte(0x800A37AB, 1); else SetMenuByte(0x800A37DB, 8);
        }
    }

    /// <summary>func_8007A5F0 gHandlePlayerJoinSelection (player-setup item callback).</summary>
    public void HandlePlayerJoinSelection(int action, int item)
    {
        if (action == 0) { HoverReadyScale(item, ref ScaleP1, ref ScaleP2); return; }
        if (action != 3) return;
        Session.PlayerOption = PlayerSelection;
        Session.PlayerOption2 = PlayerSelection2;
        if (ActiveMenuByte(5) == 2)
        {
            Ready[ActivePlayer] = 1;
            if (Ready[0] == 0 || Ready[1] == 0) return;
            Ready[0] = 0; Ready[1] = 0;
            ScaleP1 = 100; ScaleP2 = 100;
            if (Session.Split == 0) { Session.PlayerOption2 = PlayerSelection; Push(PrecinctAssaultMenus.SetupNoSplit); }
            else Push(PrecinctAssaultMenus.Hub2PSplit);
        }
        else Push(Session.Split == 0 ? PrecinctAssaultMenus.SetupNoSplit : PrecinctAssaultMenus.Hub1P);
    }

    private void HoverReadyScale(int item, ref short p1, ref short p2)
    {
        int player = ActivePlayer;
        if (ActiveMenuByte(player) != item)
        {
            Ready[player] = 0;
            if (player == 1) p2 = 100; else p1 = 100;
        }
        if (Ready[player] != 0)
        {
            if (player == 1) p2 = 110; else p1 = 110;
        }
    }

    /// <summary>func_8007B854 gHandleProfileListItem (level select). Messages: 0 draw,
    /// 1/5 next, 2/4 previous, 3 confirm.</summary>
    public void HandleProfileListItem(int message)
    {
        switch (message)
        {
            case 3: ConfirmLevel(); break;
            case 2: case 4: StepLevel(-1); break;
            case 1: case 5: StepLevel(+1); break;
            case 0:
                if (ActiveMenu == PrecinctAssaultMenus.LevelSelect)
                {
                    QueuedPrompts.Add((0x44C, 0)); QueuedPrompts.Add((0x10CC, 1));
                    ProfileSlotDraws++;
                }
                break;
        }
    }

    private void StepLevel(int direction)
    {
        if (MenuDepth[ActivePlayer] == 0) return;
        Sounds.Add(0x46);
        if (MenuDepth[ActivePlayer] == 2)
        {
            SelectedRow = (sbyte)(SelectedRow + direction);
            if (direction < 0 && SelectedRow < 0) SelectedRow = LineCount;
            if (direction > 0 && SelectedRow > LineCount) SelectedRow = 0;
        }
        else
        {
            SelectedMission = (sbyte)(SelectedMission + direction);
            if (direction < 0 && SelectedMission < ListMin) SelectedMission = ListMax;
            if (direction > 0 && SelectedMission > ListMax) SelectedMission = ListMin;
        }
    }

    private void ConfirmLevel()
    {
        ref sbyte depth = ref MenuDepth[ActivePlayer];
        byte special = 0;
        if (depth == 0) { depth = 1; return; }
        if (Session.Split == 1)
        {
            bool ready;
            if (Session.Players == 1)
            {
                ready = true;
                int value;
                if (depth == 1)
                {
                    value = RecordState[SelectedRecordIndex];
                    if (value != 0)
                    {
                        // Promote to the per-map row list; the row count is clamped to 0..9.
                        depth = 2;
                        sbyte limit = (sbyte)value;
                        sbyte clamped = limit < 0 ? (sbyte)0 : limit >= 10 ? (sbyte)9 : limit;
                        LineCount = clamped;
                        SelectedRow = clamped;
                        UiHold = 0;
                        ready = false;
                    }
                }
                else value = SelectedRow;
                if (ready) Session.LevelRow = value;
            }
            else ready = true;
            if (ready)
            {
                Push(Session.Players == 1 ? PrecinctAssaultMenus.Loadout1P : PrecinctAssaultMenus.Loadout2P);
                DeferredTransition = 2;
                PendingMovie = null;
                if (RecordState[SelectedRecordIndex] == 10) special = SelectedRecord.FinishedLoadScreen;
            }
        }
        else
        {
            DeferredTransition = 1;
            PendingMovie = SelectedRecord.BriefingMovie;
        }
        if (DeferredTransition != 0)
        {
            var record = SelectedRecord;
            ModuleName = record.Name;
            PendingModule = ModuleName == PrecinctAssaultData.Lax1ModuleName ? PrecinctAssaultData.Lax2ModuleName : null;
            if (special != 0) LoadScreen = special;
            else if (record.LoadScreen != 0) LoadScreen = (byte)record.LoadScreen;
        }
    }

    /// <summary>func_8007D39C gFindListEntryByValue: row index of <paramref name="value"/> or 0.</summary>
    public byte FindListEntryByValue(int column, byte value)
    {
        var c = LoadoutTable[column];
        byte i = 0;
        while (i < c.Count && PrecinctAssaultData.LoadoutRowValue(c.Items, i) != value) i++;
        return i == c.Count ? (byte)0 : i;
    }

    /// <summary>func_8007D408 gInitSelectionIndices (loadout screen init).</summary>
    public void InitSelectionIndices()
    {
        if (Session.Split == 1)
        {
            LoadoutTable[2].Items = PrecinctAssaultData.LoadoutList2SplitOnAddress; LoadoutTable[2].Count = 4;
            SetMenuByte(0x800A362B, 7); LoadoutTable[1].Count = 4;
        }
        else
        {
            LoadoutTable[2].Items = PrecinctAssaultData.LoadoutList2SplitOffAddress; LoadoutTable[2].Count = 5;
            SetMenuByte(0x800A362B, 2); LoadoutTable[1].Count = 5;
        }
        for (int col = 0; col < 3; col++)
            for (int p = 0; p < 2; p++)
                LoadoutSelection[col, p] = (sbyte)FindListEntryByValue(col, (byte)Session.Loadout[p * 3 + col]);
        var rec = SelectedRecord;
        byte[] max = { rec.LoadoutMax0, rec.LoadoutMax1, rec.LoadoutMax2 };
        for (int col = 0; col < 3; col++)
        {
            var c = LoadoutTable[col];
            c.Min = (byte)(Session.LoadoutColumnUnlocked[col] != 0 ? 0 : 1);
            c.Max = max[col];
            for (int p = 0; p < 2; p++)
            {
                int x = LoadoutSelection[col, p];
                LoadoutSelection[col, p] = (sbyte)(x < c.Min ? c.Min : x > c.Max ? c.Max : x);
            }
        }
        Ready[0] = 0; Ready[1] = 0;
        LoadoutScaleP1 = 100; LoadoutScaleP2 = 100;
    }

    /// <summary>func_8007D2DC gClampLoadoutSelections. Mode 0 spares the row focused by each player.</summary>
    public void ClampLoadoutSelections(int mode)
    {
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 3; j++)
            {
                var c = LoadoutTable[j];
                if ((byte)LoadoutSelection[j, i] <= c.Max) continue;
                if (mode == 0)
                {
                    if (i == 0 && j == ActiveMenuByte(0) - 1) continue;
                    if (i == 1 && j == ActiveMenuByte(1) - 5) continue;
                }
                LoadoutSelection[j, i] = (sbyte)c.Min;
            }
    }

    /// <summary>func_8007E3EC gFinalizeMissionLoadout: on confirm writes the six loadout values
    /// and sets gScreenState = 5, which leaves the front end and starts the selected module.</summary>
    public void FinalizeMissionLoadout(int action, int item)
    {
        if (action == 0)
        {
            if (ActiveMenuByte(5) == 2) HoverReadyScale(item, ref LoadoutScaleP1, ref LoadoutScaleP2);
            return;
        }
        if (action != 3) return;
        if (ActiveMenuByte(5) == 2)
        {
            Ready[ActivePlayer] = 1;
            if (Ready[0] == 0 || Ready[1] == 0) return;
        }
        ReturnMode = (byte)(Session.Split == 1 ? 2 : 1);
        ClampLoadoutSelections(1);
        for (int p = 0; p < 2; p++)
            for (int col = 0; col < 3; col++)
            {
                uint list = col == 2 ? LoadoutTable[2].Items
                    : col == 0 ? PrecinctAssaultData.LoadoutList0Address : PrecinctAssaultData.LoadoutList1Address;
                Session.Loadout[p * 3 + col] = PrecinctAssaultData.LoadoutRowValue(list, LoadoutSelection[col, p]);
            }
        RowClear = 0;
        ScreenState = 5;
    }

    private void Push(uint menu) => PushedMenus.Add(menu);
}
