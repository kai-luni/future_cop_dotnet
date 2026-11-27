# Intervall-Pickup: Retire auch ohne Auszahlung

## Beleg

In PS1-C ruft `8002FF6C.c` nach einem Kontakttreffer zuerst `gApplyPlayerResupply`
auf und danach bei gesetztem Bit `0x02000000` unbedingt `gRetireEntity`.
`8002FC90.c` kann vor der Auszahlung zurueckkehren, wenn der Akkumulator die
Periode noch nicht erreicht. Der QEMU-Fall „below the period“ in
`tests/host/leaf_gameplay_harness.c:test_resupply` prueft diese Rueckkehr.
`WeaponPickups.Update` entfernt dagegen nur bei `outcome.Paid`.
Alle CONFT-Pickups sind instant; deren Einmal-Auszahlung ist davon nicht betroffen.

## Akzeptanz

Mit Retire-Bit und ohne Instant-Bit: Ein Kontakt unterhalb der Periode
zahlt nichts aus, entfernt den Pickup aber sofort aus der Live-Liste.
Ohne Retire-Bit bleibt der Intervall-Pickup erhalten.
