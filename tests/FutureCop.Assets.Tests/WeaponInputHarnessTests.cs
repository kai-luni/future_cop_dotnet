using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The behavioural checks of <c>tests/host/weapon_input_harness.c</c> (7 cases) against the port:
/// func_80055E10 gWeaponSlotInput, the pad-to-slot poller, and func_8005780C gDecodeButtonEdge, the
/// four-phase edge decoder it runs on all three action records.
/// </summary>
/// <remarks>
/// <para>
/// The harness links both units from the unchanged decomp sources and stubs only the two callees (the
/// lock-on acquisition and the slot driver), so its expectations are retail. Its constants are inline
/// in that file: slot 0 = 0x04000000, slot 1 = 0x00400000, the weapon button = 0x01000000, the fire
/// button = 0x00800000, the special button = 0x02000000, the idle phase 2, the press phase 0, the held
/// phase 3, the release phase 1, and the pause byte at control + 0x310.
/// </para>
/// <para>
/// The port's poller is <see cref="WeaponInput.Poll"/> with the same phase numbering (its
/// <c>PhaseIdle</c> is 2, <c>PhasePressed</c> 0, <c>PhaseHeld</c> 3, <c>PhaseReleased</c> 1), so the
/// numbers in the facts below are the harness's own. The pause byte is <see cref="MechBody.Frozen"/>:
/// the port annotates it +0x310, which is the byte func_80055E10:61 tests and func_800538C8:55 reads.
/// </para>
/// <para>
/// Four of the facts below fail, and all four are genuine port defects rather than bad expectations:
/// the pause gate and the tap consequence are <c>[RetailFact]</c>s, the two decoder facts are plain
/// <c>[Fact]</c>s because they need no retail data. Each one carries the contradicting C line in its
/// remarks. The port's phase numbering is the harness's own (2 / 0 / 3 / 1), so the numbers in the
/// assertions are the harness's.
/// </para>
/// </remarks>
public class WeaponInputHarnessTests
{
    private const uint Slot0Bit = 0x04000000;
    private const uint Slot1Bit = 0x00400000;
    private const uint WeaponBit = 0x01000000;
    private const uint FireBit = 0x00800000;
    private const uint SpecialBit = 0x02000000;

    private static string FindMission()
    {
        foreach (var candidate in new[]
                 {
                     "build/disc/files/PA/CONFT.MIS", "../../../../build/disc/files/PA/CONFT.MIS",
                     "../../../../../build/disc/files/PA/CONFT.MIS",
                 })
            if (File.Exists(candidate)) return candidate;
        throw new FileNotFoundException("build/disc/files/PA/CONFT.MIS");
    }

    private static List<int> Poll(WeaponInput input, uint word,
        byte weaponState = WeaponSlots.WeaponButtonHeld)
    {
        var fired = new List<int>();
        input.Poll(word, weaponState, fired.Add);
        return fired;
    }

    // ---------------------------------------------------------------- harness case 1

    /// <summary>
    /// weapon_input_harness.c case 1: func_80055E10:61-63 returns before the decoder runs, so a set
    /// pause byte stops every slot call, the lock refresh <em>and</em> the phase updates - the harness
    /// asserts <c>fire.phase == 2</c> afterwards, i.e. the record is not even advanced to a press edge.
    /// </summary>
    /// <remarks>
    /// PORT DEFECT: <see cref="LevelWeapons.Advance"/> (src/FutureCop.View/LevelWeapons.cs:140-150)
    /// polls unconditionally and never reads <see cref="MechBody.Frozen"/>. The only user of that byte
    /// in the port is the stick read at src/FutureCop.Game/Mech/MechWalker.cs:453, so a paused player
    /// keeps firing. The C gate is func_80055E10:61.
    /// </remarks>
    [RetailFact]
    public void ARealSceneDoesNotFireWhileThePauseByteIsSet()
    {
        string mis = FindMission();
        var scene = new LevelScene(mis);
        scene.EnableMechMovement();
        scene.EnableFiring();
        var weapons = scene.Weapons!;
        weapons.Slots.Timers[1] = 0;
        int ammo = weapons.Slots.Ammo[1];

        scene.Walker!.Frozen = 1;
        scene.HeldPadWord = Slot1Bit;
        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        scene.AdvanceWalker(1, 0, AnalogPad.Digital);

        Assert.Empty(weapons.Shots);
        Assert.Equal(ammo, weapons.Slots.Ammo[1]);

        // Clearing the pause resumes the poller on the very next frame.
        scene.Walker.Frozen = 0;
        scene.AdvanceWalker(1, 0, AnalogPad.Digital);
        Assert.Single(weapons.Shots);
        Assert.Equal(ammo - 1, weapons.Slots.Ammo[1]);
    }

    // ---------------------------------------------------------------- harness case 3

    /// <summary>
    /// weapon_input_harness.c case 3: the two slot bits are independent held-bit tests
    /// (func_80055E10:71-76) and both are emitted in ascending slot order in the same frame.
    /// </summary>
    [Fact]
    public void BothSlotBitsCallTheirOwnSlotInAscendingOrder()
    {
        var input = new WeaponInput();
        Assert.Equal(new[] { 0 }, Poll(input, Slot0Bit));
        Assert.Equal(new[] { 1 }, Poll(input, Slot1Bit));
        Assert.Equal(new[] { 0, 1 }, Poll(input, Slot0Bit | Slot1Bit));
        Assert.Empty(Poll(input, 0));
    }

    // ---------------------------------------------------------------- harness case 4

    /// <summary>
    /// weapon_input_harness.c case 4: slot 2 is a phase <em>match</em>
    /// (<c>control->weapon.phase == control->weaponState</c>, func_80055E10:77-79), not a bit test, so
    /// the press edge 0 stays silent while the entity state is the held value 3, and a fresh press
    /// fires at once when the state is 0.
    /// </summary>
    [Fact]
    public void SlotTwoFiresOnAPhaseMatchNotOnTheBit()
    {
        // state 3 (an ordinary weapon): the press edge is silent, the held phase matches.
        var held = new WeaponInput();
        Assert.Empty(Poll(held, WeaponBit, WeaponSlots.WeaponButtonHeld));
        Assert.Equal(new[] { 2 }, Poll(held, WeaponBit, WeaponSlots.WeaponButtonHeld));

        // state 0 (a press-edge weapon): the press edge itself matches.
        var press = new WeaponInput();
        Assert.Equal(new[] { 2 }, Poll(press, WeaponBit, weaponState: 0));
    }

    // ---------------------------------------------------------------- harness case 5

    /// <summary>
    /// weapon_input_harness.c case 5: the lock refresh happens on the fire button's press edge only
    /// (func_80055E10:68-70), passing the literal 0, and never again while the button is held. The port
    /// has no target list, so the observable part is the phase: press edge on the first frame, held on
    /// the second.
    /// </summary>
    [Fact]
    public void TheFirePressEdgeHappensOnceAndThenTheButtonIsHeld()
    {
        var input = new WeaponInput();
        Assert.Equal(WeaponInput.PhaseIdle, input.FirePhase);

        Poll(input, FireBit);
        Assert.Equal(WeaponInput.PhasePressed, input.FirePhase);

        // Still held: no second press edge, so no second lock refresh.
        Poll(input, FireBit);
        Assert.Equal(WeaponInput.PhaseHeld, input.FirePhase);
    }

    // ---------------------------------------------------------------- harness case 7

    /// <summary>
    /// weapon_input_harness.c case 7: all three action records are decoded every frame
    /// (func_80055E10:65-67) whether or not their slot ends up firing, an untouched record stays idle,
    /// and only a fire press refreshes the lock. The harness asserts exactly
    /// <c>special.phase == 0</c>, <c>weapon.phase == 0</c>, <c>fire.phase == 2</c>, no lock call.
    /// </summary>
    [Fact]
    public void AllThreeActionsAreDecodedEvenWhenNoSlotFires()
    {
        var input = new WeaponInput();
        var fired = Poll(input, SpecialBit | WeaponBit);

        Assert.Equal(WeaponInput.PhasePressed, input.SpecialPhase);
        Assert.Equal(WeaponInput.PhasePressed, input.WeaponPhase);
        Assert.Equal(WeaponInput.PhaseIdle, input.FirePhase);
        // Both are only on their press edge, so slot 2 waits for the held phase.
        Assert.Empty(fired);
    }

    // ---------------------------------------------------------------- harness case 6

    /// <summary>
    /// weapon_input_harness.c case 6: the decoder table of func_8005780C:45-54 verbatim - idle to
    /// press, press to held, held to release, release to idle - and an unrelated bit is not a press.
    /// </summary>
    [Fact]
    public void TheFourPhasesFollowTheRetailTable()
    {
        var input = new WeaponInput();
        Assert.Equal(WeaponInput.PhaseIdle, input.WeaponPhase);

        Poll(input, WeaponBit);
        Assert.Equal(WeaponInput.PhasePressed, input.WeaponPhase);
        Poll(input, WeaponBit);
        Assert.Equal(WeaponInput.PhaseHeld, input.WeaponPhase);
        Poll(input, 0);
        Assert.Equal(WeaponInput.PhaseReleased, input.WeaponPhase);
        Poll(input, 0);
        Assert.Equal(WeaponInput.PhaseIdle, input.WeaponPhase);

        // An unrelated bit is not a press: the phase stays idle.
        Poll(input, 0x00100000);
        Assert.Equal(WeaponInput.PhaseIdle, input.WeaponPhase);
    }

    /// <summary>
    /// func_8005780C:45-54, the press branch: <c>if (buttons &amp; action-&gt;buttons) { if (phase == 2)
    /// phase = 0; else phase = 3; }</c>. The condition is <em>phase == 2</em>, not "was idle", so a
    /// button pressed while the record sits on the release edge 1 goes straight to held 3.
    /// </summary>
    /// <remarks>
    /// PORT DEFECT: <see cref="WeaponInput"/>'s private table
    /// (src/FutureCop.Game/WeaponInput.cs:55-66) has a bare <c>PhaseReleased =&gt; PhaseIdle</c> arm that
    /// ignores the button, so a press from the release edge falls to idle 2 instead of held 3. The
    /// effect is a lost press edge: the record never passes through 0 again, so a state-0 weapon sees
    /// no fresh press.
    /// </remarks>
    [Fact]
    public void AButtonPressedFromTheReleaseEdgeGoesToHeld()
    {
        var input = new WeaponInput();
        Poll(input, WeaponBit);                   // idle 2 -> press 0
        Poll(input, WeaponBit);                   // press 0 -> held 3
        Poll(input, 0);                           // held 3 -> release 1
        Assert.Equal(WeaponInput.PhaseReleased, input.WeaponPhase);

        // C: button down and phase != 2, so the record goes to held 3 - not back to idle.
        Poll(input, WeaponBit);
        Assert.Equal(WeaponInput.PhaseHeld, input.WeaponPhase);
    }

    /// <summary>
    /// func_8005780C:51-54, the release branch: <c>else if (action-&gt;phase != 2) action-&gt;phase =
    /// 2</c>. A release from the press edge (phase 0) therefore resets the record to idle 2.
    /// </summary>
    /// <remarks>
    /// PORT DEFECT: the port's table has no arm for "pressed, then released" and falls through to
    /// <c>_ =&gt; phase</c> (src/FutureCop.Game/WeaponInput.cs:55-66), leaving the record stuck on 0
    /// forever. Because slot 2 fires on <c>phase == weaponState</c>, a stuck 0 makes a state-0 weapon
    /// fire on <em>every</em> frame after a single tap - see
    /// <see cref="ATapOnAPressEdgeWeaponFiresOnceNotForever"/>.
    /// </remarks>
    [Fact]
    public void AButtonReleasedFromThePressEdgeReturnsToIdle()
    {
        var input = new WeaponInput();
        Poll(input, WeaponBit);                   // idle 2 -> press 0
        Assert.Equal(WeaponInput.PhasePressed, input.WeaponPhase);

        Poll(input, 0);                           // C: phase 0 -> idle 2
        Assert.Equal(WeaponInput.PhaseIdle, input.WeaponPhase);
    }

    /// <summary>
    /// The gameplay consequence of the stuck press edge, measured on a real level rather than on the
    /// decoder alone. Slot id 0x21 has the flag that drives <see cref="WeaponSlots.WeaponState"/> to 0,
    /// i.e. it fires on the press edge. Retail: one tap, one shot, then the release resets the phase to
    /// idle and nothing more fires. The port keeps the phase at 0 and re-fires every frame.
    /// </summary>
    /// <remarks>
    /// The reload timer is cleared every frame so that the phase gate is the only thing under test;
    /// the C's own phase gate is func_80055E10:77.
    /// </remarks>
    [RetailFact]
    public void ATapOnAPressEdgeWeaponFiresOnceNotForever()
    {
        string mis = FindMission();
        var scene = new LevelScene(mis);
        scene.EnableMechMovement();
        scene.EnableFiring();
        var weapons = scene.Weapons!;
        weapons.Equip(new byte[] { 0x21, 0x21, 0x21 });
        Assert.Equal(0, weapons.Slots.WeaponState);

        var perFrame = new List<int>();
        for (int frame = 0; frame < 6; frame++)
        {
            weapons.Slots.Timers[2] = 0;
            int before = weapons.Shots.Count;
            scene.HeldPadWord = frame == 0 ? WeaponBit : 0u;
            scene.AdvanceWalker(1, 0, AnalogPad.Digital);
            perFrame.Add(weapons.Shots.Count - before);
        }

        // One tap, one shot; the following released frames fire nothing.
        Assert.Equal(new[] { 1, 0, 0, 0, 0, 0 }, perFrame);
    }
}
