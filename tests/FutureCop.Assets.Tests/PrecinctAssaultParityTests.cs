using System.Globalization;
using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Runs tests/golden/precinct-assault.vectors, the fixture shared with
/// future-cop-lapd-ps1/tests/host/precinct_assault_test.sh, which checks the same
/// lines against the unchanged retail C units under qemu-mipsel. Key names mirror
/// the field table in precinct_assault_harness.c.
/// </summary>
public class PrecinctAssaultParityTests
{
    private static string VectorPath => Path.Combine(AppContext.BaseDirectory, "golden", "precinct-assault.vectors");

    [Fact]
    public void SharedVectorsMatchRetailFrontEnd()
    {
        int cases = 0;
        var failures = new List<string>();
        foreach (var raw in File.ReadLines(VectorPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var tok = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int bar = Array.IndexOf(tok, "|");
            Assert.True(bar >= 2, $"bad line {tok[0]}");
            var fe = new PrecinctAssaultFrontEnd();
            for (int i = 2; i < bar; i++)
            {
                var (k, v) = Split(tok[i]);
                Set(fe, k, v);
            }
            foreach (var op in tok[1].Split(','))
                Run(fe, op);
            for (int i = bar + 1; i < tok.Length; i++)
            {
                var (k, want) = Split(tok[i]);
                Assert.NotEqual("?", want);
                var got = Get(fe, k);
                if (got != want) failures.Add($"{tok[0]} {k}: got {got} want {want}");
            }
            cases++;
        }
        Assert.True(cases >= 60, $"only {cases} cases");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void FinalizeStartsGameOnlyAfterConfirm()
    {
        var fe = new PrecinctAssaultFrontEnd();
        fe.SelectPrecinctAssault1P(3);
        fe.ActiveMenu = PrecinctAssaultMenus.LevelSelect;
        fe.HandleProfileListItem(3);
        fe.HandleProfileListItem(3);
        fe.ActiveMenu = PrecinctAssaultMenus.Loadout1P;
        fe.InitSelectionIndices();
        Assert.Equal(0, fe.ScreenState);
        fe.FinalizeMissionLoadout(3, 0);
        Assert.Equal(5, fe.ScreenState);
        Assert.Equal("conft", fe.ModuleName);
        Assert.Equal(new[] { PrecinctAssaultMenus.Loadout1P }, fe.PushedMenus);
    }

    private static (string, string) Split(string t)
    {
        int eq = t.IndexOf('=');
        Assert.True(eq > 0, $"bad token {t}");
        return (t[..eq], t[(eq + 1)..]);
    }

    private static long Num(string v) =>
        v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? long.Parse(v[2..], NumberStyles.HexNumber)
        : v.StartsWith("-0x", StringComparison.OrdinalIgnoreCase) ? -long.Parse(v[3..], NumberStyles.HexNumber)
        : long.Parse(v, CultureInfo.InvariantCulture);

    private static void Run(PrecinctAssaultFrontEnd fe, string op)
    {
        var a = op.Split(':');
        int x0 = a.Length > 1 ? (int)Num(a[1]) : 0, x1 = a.Length > 2 ? (int)Num(a[2]) : 0;
        switch (a[0])
        {
            case "setupCW": fe.SetupCrimeWarMissionList(); break;
            case "setupPA": fe.SetupPrecinctAssaultMissionList(); break;
            case "unlock": fe.UpdatePrecinctUnlockFlag(); break;
            case "select1p": fe.SelectPrecinctAssault1P(x0); break;
            case "select2p": fe.SelectPrecinctAssault2P(x0); break;
            case "initSetup": fe.InitMissionSelectState(); break;
            case "join": fe.HandlePlayerJoinSelection(x0, x1); break;
            case "list": fe.HandleProfileListItem(x0); break;
            case "find": _finds.AddOrUpdate(fe, (int)fe.FindListEntryByValue(x0, (byte)x1)); break;
            case "loadoutInit": fe.InitSelectionIndices(); break;
            case "clamp": fe.ClampLoadoutSelections(x0); break;
            case "finalize": fe.FinalizeMissionLoadout(x0, x1); break;
            case "menu": fe.ActiveMenu = (uint)x0; break;
            default: throw new InvalidOperationException($"unknown op {op}");
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PrecinctAssaultFrontEnd, object> _finds = new();

    private static string Hex(long v) => v == 0 ? "0" : $"0x{v:X}";

    private static int SessionOffset(string k) => int.Parse(k[5..], NumberStyles.HexNumber);

    private static void Set(PrecinctAssaultFrontEnd fe, string k, string v)
    {
        if (k == "module") { fe.ModuleName = v; return; }
        long n = Num(v);
        if (k.StartsWith("sess_")) { SetSession(fe.Session, SessionOffset(k), (int)n); return; }
        if (k.StartsWith("menu_")) { SetMenu(fe, int.Parse(k[5..], NumberStyles.HexNumber), n); return; }
        if (TryRecord(k, out int rec, out bool second))
        {
            (second ? fe.RecordState2 : fe.RecordState)[rec] = (byte)n;
            return;
        }
        switch (k)
        {
            case "frontEndListFlag": fe.FrontEndListFlag = (byte)n; break;
            case "unlockFlag": fe.UnlockFlag = (byte)n; break;
            case "selectedMission": fe.SelectedMission = (sbyte)n; break;
            case "listMin": fe.ListMin = (sbyte)n; break;
            case "listMax": fe.ListMax = (sbyte)n; break;
            case "listTable": fe.ListTable = (uint)n; break;
            case "listCount": fe.ListCount = (byte)n; break;
            case "listMode": fe.ListMode = (byte)n; break;
            case "selectLock": fe.SelectLock = (int)n; break;
            case "paProgress": fe.PrecinctProgress = (byte)n; break;
            case "briefingShown": fe.BriefingShown = (byte)n; break;
            case "deferredTransition": fe.DeferredTransition = (int)n; break;
            case "D_800A377D": fe.SetMenuByte(0x800A377D, (byte)n); break;
            case "playerSelection": fe.PlayerSelection = (int)n; break;
            case "playerSelection2": fe.PlayerSelection2 = (int)n; break;
            case "ready0": fe.Ready[0] = (int)n; break;
            case "ready1": fe.Ready[1] = (int)n; break;
            case "activePlayer": fe.ActivePlayer = (int)n; break;
            case "depth0": fe.MenuDepth[0] = (sbyte)n; break;
            case "depth1": fe.MenuDepth[1] = (sbyte)n; break;
            case "selectedRow": fe.SelectedRow = (sbyte)n; break;
            case "lineCount": fe.LineCount = (sbyte)n; break;
            case "uiHold": fe.UiHold = (byte)n; break;
            case "rowClear": fe.RowClear = (byte)n; break;
            case "scaleP1": fe.ScaleP1 = (short)n; break;
            case "scaleP2": fe.ScaleP2 = (short)n; break;
            case "menu": fe.ActiveMenu = (uint)n; break;
            default:
                if (TrySel(k, out int col, out int p)) { fe.LoadoutSelection[col, p] = (sbyte)n; break; }
                if (TryLt(k, out int c, out string f))
                {
                    var t = fe.LoadoutTable[c];
                    if (f == "Min") t.Min = (byte)n; else if (f == "Max") t.Max = (byte)n; else if (f == "Count") t.Count = (byte)n;
                    else throw new InvalidOperationException(k);
                    break;
                }
                throw new InvalidOperationException($"unsupported input key {k}");
        }
    }

    private static string Get(PrecinctAssaultFrontEnd fe, string k)
    {
        if (k.StartsWith("sess_")) return GetSession(fe.Session, SessionOffset(k)).ToString(CultureInfo.InvariantCulture);
        if (TryRecord(k, out int rec, out bool second)) return (second ? fe.RecordState2 : fe.RecordState)[rec].ToString();
        if (TrySel(k, out int col, out int p)) return fe.LoadoutSelection[col, p].ToString();
        if (TryLt(k, out int c, out string f))
        {
            var t = fe.LoadoutTable[c];
            return f switch { "Min" => t.Min.ToString(), "Max" => t.Max.ToString(), "Count" => t.Count.ToString(), "Items" => Hex(t.Items), _ => "!" };
        }
        return k switch
        {
            "frontEndListFlag" => fe.FrontEndListFlag.ToString(),
            "unlockFlag" => fe.UnlockFlag.ToString(),
            "selectedMission" => fe.SelectedMission.ToString(),
            "listMin" => fe.ListMin.ToString(),
            "listMax" => fe.ListMax.ToString(),
            "listTable" => Hex(fe.ListTable),
            "listCount" => fe.ListCount.ToString(),
            "listMode" => fe.ListMode.ToString(),
            "crimeWarProgress" => fe.CrimeWarProgress.ToString(),
            "savedCrimeWarProgress" => fe.SavedCrimeWarProgress.ToString(),
            "paProgress" => fe.PrecinctProgress.ToString(),
            "briefingShown" => fe.BriefingShown.ToString(),
            "deferredTransition" => fe.DeferredTransition.ToString(),
            "pendingMovie" => fe.PendingMovie ?? "-",
            "D_800A377D" or "D_800A368B" or "D_800A37AB" or "D_800A37DB" or "D_800A362B" =>
                fe.GetMenuByte(uint.Parse(k[2..], NumberStyles.HexNumber)).ToString(),
            "playerSelection" => fe.PlayerSelection.ToString(),
            "playerSelection2" => fe.PlayerSelection2.ToString(),
            "ready0" => fe.Ready[0].ToString(),
            "ready1" => fe.Ready[1].ToString(),
            "depth0" => fe.MenuDepth[0].ToString(),
            "depth1" => fe.MenuDepth[1].ToString(),
            "selectedRow" => fe.SelectedRow.ToString(),
            "lineCount" => fe.LineCount.ToString(),
            "uiHold" => fe.UiHold.ToString(),
            "module" => fe.ModuleName,
            "pendingModule" => fe.PendingModule ?? "-",
            "loadScreen" => fe.LoadScreen.ToString(),
            "returnMode" => fe.ReturnMode.ToString(),
            "rowClear" => fe.RowClear.ToString(),
            "screenState" => fe.ScreenState.ToString(),
            "scaleP1" => fe.ScaleP1.ToString(),
            "scaleP2" => fe.ScaleP2.ToString(),
            "loadoutScaleP1" => fe.LoadoutScaleP1.ToString(),
            "loadoutScaleP2" => fe.LoadoutScaleP2.ToString(),
            "menu" => Hex(fe.ActiveMenu),
            "push" => fe.PushedMenus.Count == 0 ? "0" : Hex(fe.PushedMenus[^1]),
            "pushes" => fe.PushedMenus.Count.ToString(),
            "sfx" => fe.Sounds.Count == 0 ? "-1" : fe.Sounds[^1].ToString(),
            "sfxs" => fe.Sounds.Count.ToString(),
            "prompts" => fe.QueuedPrompts.Count.ToString(),
            "slotDraws" => fe.ProfileSlotDraws.ToString(),
            "ret" => _finds.TryGetValue(fe, out var r) ? r.ToString()! : "0",
            _ => throw new InvalidOperationException($"unsupported output key {k}"),
        };
    }

    private static bool TryRecord(string k, out int rec, out bool second)
    {
        rec = 0; second = false;
        if (!k.StartsWith("rec")) return false;
        int us = k.IndexOf('_');
        rec = int.Parse(k[3..us]);
        second = k[(us + 1)..] == "state2";
        return true;
    }

    private static bool TrySel(string k, out int col, out int player)
    {
        col = player = 0;
        if (k.Length != 6 || !k.StartsWith("sel") || k[4] != 'p') return false;
        col = k[3] - '0'; player = k[5] - '0';
        return true;
    }

    private static bool TryLt(string k, out int col, out string field)
    {
        col = 0; field = "";
        if (!k.StartsWith("lt") || k.Length < 4) return false;
        col = k[2] - '0'; field = k[3..];
        return true;
    }

    private static void SetMenu(PrecinctAssaultFrontEnd fe, int offset, long n)
    {
        if (offset == 0x24) fe.MenuParent[fe.ActiveMenu] = (uint)n;
        else fe.SetMenuByte(fe.ActiveMenu + (uint)offset, (byte)n);
    }

    private static void SetSession(PrecinctAssaultSession s, int off, int v)
    {
        switch (off)
        {
            case 0x08: s.PlayerOption = v; break;
            case 0x0C: s.Players = v; break;
            case >= 0x28 and <= 0x3C when off % 4 == 0: s.Loadout[(off - 0x28) / 4] = v; break;
            case 0x4C: s.LevelRow = v; break;
            case 0x5C: s.Split = v; break;
            case 0x8C: case 0x90: case 0x94: s.LoadoutColumnUnlocked[(off - 0x8C) / 4] = v; break;
            case 0x98: s.PlayerOption2 = v; break;
            default: throw new InvalidOperationException($"session offset 0x{off:X2} not modelled");
        }
    }

    private static int GetSession(PrecinctAssaultSession s, int off) => off switch
    {
        0x08 => s.PlayerOption,
        0x0C => s.Players,
        >= 0x28 and <= 0x3C when off % 4 == 0 => s.Loadout[(off - 0x28) / 4],
        0x4C => s.LevelRow,
        0x5C => s.Split,
        0x8C or 0x90 or 0x94 => s.LoadoutColumnUnlocked[(off - 0x8C) / 4],
        0x98 => s.PlayerOption2,
        _ => throw new InvalidOperationException($"session offset 0x{off:X2} not modelled"),
    };
}
