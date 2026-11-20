using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Arm32ManualMapper.Tests;

public class DlIteratePhdrTests
{
    [Fact]
    public void Arm32DlPhdrInfoLayoutMatchesGlibcArm32()
    {
        Assert.Equal(40, Marshal.SizeOf<NativeHooks.Arm32DlPhdrInfo>());
        Assert.Equal(0, Marshal.OffsetOf<NativeHooks.Arm32DlPhdrInfo>(nameof(NativeHooks.Arm32DlPhdrInfo.dlpi_addr)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<NativeHooks.Arm32DlPhdrInfo>(nameof(NativeHooks.Arm32DlPhdrInfo.dlpi_name)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<NativeHooks.Arm32DlPhdrInfo>(nameof(NativeHooks.Arm32DlPhdrInfo.dlpi_phdr)).ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<NativeHooks.Arm32DlPhdrInfo>(nameof(NativeHooks.Arm32DlPhdrInfo.dlpi_phnum)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<NativeHooks.Arm32DlPhdrInfo>(nameof(NativeHooks.Arm32DlPhdrInfo.dlpi_adds)).ToInt32());
        Assert.Equal(24, Marshal.OffsetOf<NativeHooks.Arm32DlPhdrInfo>(nameof(NativeHooks.Arm32DlPhdrInfo.dlpi_subs)).ToInt32());
        Assert.Equal(32, Marshal.OffsetOf<NativeHooks.Arm32DlPhdrInfo>(nameof(NativeHooks.Arm32DlPhdrInfo.dlpi_tls_modid)).ToInt32());
        Assert.Equal(36, Marshal.OffsetOf<NativeHooks.Arm32DlPhdrInfo>(nameof(NativeHooks.Arm32DlPhdrInfo.dlpi_tls_data)).ToInt32());
    }

    [Fact]
    public void AddPhdrModuleWrites40ByteRecordsAndUpdatesLiveCountWithoutSyntheticTlsLeak()
    {
        var hooks = new NativeHooks();
        hooks.EnsurePhdrTable();
        Assert.Equal(0, hooks.ReadPhdrCount());

        hooks.AddPhdrModule(0x11000000u, 0x11000100u, 0x11000200u, 4);
        Assert.Equal(1, hooks.PhdrCount);
        Assert.Equal(1, hooks.ReadPhdrCount());

        NativeHooks.Arm32DlPhdrInfo first = hooks.ReadPhdrEntry(0);
        Assert.Equal(0x11000000u, first.dlpi_addr);
        Assert.Equal(0x11000100u, first.dlpi_name);
        Assert.Equal(0x11000200u, first.dlpi_phdr);
        Assert.Equal((ushort)4, first.dlpi_phnum);
        Assert.Equal(1UL, first.dlpi_adds);
        Assert.Equal(0UL, first.dlpi_subs);
        Assert.Equal(0u, first.dlpi_tls_modid);
        Assert.Equal(0u, first.dlpi_tls_data);

        hooks.AddPhdrModule(0x22000000u, 0x22000100u, 0x22000200u, 6);
        Assert.Equal(2, hooks.PhdrCount);
        Assert.Equal(2, hooks.ReadPhdrCount());

        first = hooks.ReadPhdrEntry(0);
        NativeHooks.Arm32DlPhdrInfo second = hooks.ReadPhdrEntry(1);
        Assert.Equal(2UL, first.dlpi_adds);
        Assert.Equal(2UL, second.dlpi_adds);
        Assert.Equal(0u, second.dlpi_tls_modid);
        Assert.Equal(0u, second.dlpi_tls_data);
    }

    [Fact]
    public void AddPhdrModuleThrowsWhenCapacityExceeded()
    {
        var hooks = new NativeHooks();
        hooks.EnsurePhdrTable();
        for (int i = 0; i < NativeHooks.MaxPhdrModules; i++)
        {
            hooks.AddPhdrModule(0x10000000u + (uint)i * 0x10000u, 0x10001000u, 0x10002000u, 2);
        }
        Assert.False(hooks.CanAddPhdrModule());
        Assert.Throws<InvalidOperationException>(() =>
            hooks.AddPhdrModule(0x90000000u, 0x90001000u, 0x90002000u, 2));
    }

    [Fact]
    public void BuildDlIteratePhdrInstructionsEncodesValidArm32LoopAndBarriers()
    {
        var insns = NativeHooks.BuildDlIteratePhdrInstructions(0x12345678u, 0x23456789u, 0x34567890u);
        Assert.Equal(26, insns.Count);
        Assert.Equal(0xE92D41F0u, insns[0]);  // push {r4-r8, lr}
        Assert.Equal(0xE1A04000u, insns[1]);  // mov r4, r0
        Assert.Equal(0xE1A05001u, insns[2]);  // mov r5, r1
        Assert.Equal(0xE59FC048u, insns[3]);  // ldr r12, [pc, #72] -> [23]
        Assert.Equal(0xE12FFF3Cu, insns[4]);  // blx r12 (real dl_iterate_phdr)
        Assert.Equal(0xE3500000u, insns[5]);  // cmp r0, #0
        Assert.Equal(0x1A00000Eu, insns[6]);  // bne [22]
        Assert.Equal(0xE59F603Cu, insns[7]);  // ldr r6, [pc, #60] -> [24]
        Assert.Equal(0xE59F703Cu, insns[8]);  // ldr r7, [pc, #60] -> [25]
        Assert.Equal(0xE5977000u, insns[9]);  // ldr r7, [r7] (*countPtr)
        Assert.Equal(0xF57FF05Bu, insns[10]); // dmb ish
        Assert.Equal(0xE3570000u, insns[11]); // cmp r7, #0
        Assert.Equal(0x0A000008u, insns[12]); // beq [22]
        Assert.Equal(0xE1A00006u, insns[13]); // mov r0, r6
        Assert.Equal(0xE3A01028u, insns[14]); // mov r1, #40
        Assert.Equal(0xE1A02005u, insns[15]); // mov r2, r5
        Assert.Equal(0xE12FFF34u, insns[16]); // blx r4 (cb)
        Assert.Equal(0xE3500000u, insns[17]); // cmp r0, #0
        Assert.Equal(0x1A000002u, insns[18]); // bne [22]
        Assert.Equal(0xE2866028u, insns[19]); // add r6, r6, #40
        Assert.Equal(0xE2477001u, insns[20]); // sub r7, r7, #1
        Assert.Equal(0xEAFFFFF4u, insns[21]); // b [11]
        Assert.Equal(0xE8BD81F0u, insns[22]); // pop {r4-r8, pc}
        Assert.Equal(0x12345678u, insns[23]); // literal realIterate
        Assert.Equal(0x23456789u, insns[24]); // literal infoTable
        Assert.Equal(0x34567890u, insns[25]); // literal countPtr
    }

    [Fact]
    public void ToArm32AddressAcceptsHigh32BitAddressesAndRejects64BitOverflow()
    {
        Assert.Equal(0xB6000000u, NativeHooks.ToArm32Address(unchecked((IntPtr)(long)0xB6000000u)));
        Assert.Equal(0xFFFFFFFFu, NativeHooks.ToArm32Address(unchecked((IntPtr)(long)0xFFFFFFFFu)));
        if (IntPtr.Size == 8)
        {
            Assert.Throws<OverflowException>(() => NativeHooks.ToArm32Address((IntPtr)0x1_0000_0000L));
        }
    }

    [Fact]
    public void ReadAndValidateProgramHeadersExtractsAllOriginalPhdrsIncludingDynamicEhFrameTlsAndExidx()
    {
        byte[] elf = BuildMinimalArm32SharedObject();
        Elf32_Phdr[] phs = Arm32ElfLoader.ReadAndValidateProgramHeaders(elf, "libtest_phdrs.so");

        Assert.Equal(6, phs.Length);
        var types = new List<uint>();
        foreach (Elf32_Phdr ph in phs)
            types.Add(ph.p_type);

        Assert.Equal(new uint[]
        {
            6,                        // PT_PHDR
            ElfConstants.PT_LOAD,
            ElfConstants.PT_DYNAMIC,
            ElfConstants.PT_TLS,
            0x6474E550u,              // PT_GNU_EH_FRAME
            ElfConstants.PT_ARM_EXIDX,
        }, types);
    }

    [Fact]
    public void ReadAndValidateProgramHeadersRejectsTruncatedProgramHeaderTable()
    {
        byte[] elf = BuildMinimalArm32SharedObject();
        byte[] truncated = elf[..80];
        Assert.Throws<InvalidDataException>(() =>
            Arm32ElfLoader.ReadAndValidateProgramHeaders(truncated, "truncated.so"));
    }

    [Fact]
    public void EnsureModuleInitialized_SetsInitializedOnlyAfterConstructorCompletesAndSupportsReentrancy()
    {
        var loader = new Arm32ElfLoader(Path.GetTempPath());
        var mod = new MappedElfModule(Path.Combine(Path.GetTempPath(), "libinit_order.so"), (IntPtr)0x70000100, 0x4001)
        {
            Relocated = true,
        };

        try
        {
            bool observedInitializedInsideCtor = true;
            bool observedInitializingInsideCtor = false;
            int observedThreadIdInsideCtor = 0;
            bool reentrantResult = false;
            int ctorCalls = 0;

            Assert.True(loader.EnsureModuleInitialized(mod, m =>
            {
                Interlocked.Increment(ref ctorCalls);
                observedInitializedInsideCtor = m.Initialized;
                observedInitializingInsideCtor = m.Initializing;
                observedThreadIdInsideCtor = m.InitThreadId;
                reentrantResult = loader.EnsureModuleInitialized(m, _ => Interlocked.Increment(ref ctorCalls));
            }));

            Assert.Equal(1, ctorCalls);
            Assert.False(observedInitializedInsideCtor);
            Assert.True(observedInitializingInsideCtor);
            Assert.Equal(Environment.CurrentManagedThreadId, observedThreadIdInsideCtor);
            Assert.True(reentrantResult);
            Assert.True(mod.Initialized);
            Assert.False(mod.Initializing);
            Assert.Null(mod.InitException);

            // Subsequent RunModuleInitializers returns immediately without re-running constructor.
            loader.RunModuleInitializers(mod);
            Assert.Equal(1, ctorCalls);
        }
        finally
        {
            Marshal.FreeHGlobal(mod.FullPathCStr);
        }
    }

    [Fact]
    public void EnsureModuleInitialized_RecordsConstructorFailurePermanentlyWithoutRerunning()
    {
        var loader = new Arm32ElfLoader(Path.GetTempPath());
        var mod = new MappedElfModule(Path.Combine(Path.GetTempPath(), "libinit_fail.so"), (IntPtr)0x70000101, 0x4002)
        {
            Relocated = true,
        };
        try
        {
            int ctorCalls = 0;
            bool ok = loader.EnsureModuleInitialized(mod, _ =>
            {
                Interlocked.Increment(ref ctorCalls);
                throw new InvalidOperationException("ctor exploded");
            });

            Assert.False(ok);
            Assert.False(mod.Initialized);
            Assert.False(mod.Initializing);
            var recorded = Assert.IsType<InvalidOperationException>(mod.InitException);
            Assert.Equal("ctor exploded", recorded.Message);

            // Both EnsureModuleInitialized and RunModuleInitializers preserve the original failure and never rerun ctor.
            Assert.False(loader.EnsureModuleInitialized(mod, _ => Interlocked.Increment(ref ctorCalls)));
            var thrown = Assert.Throws<InvalidOperationException>(() => loader.RunModuleInitializers(mod));
            Assert.Same(recorded, thrown);
            Assert.Equal(1, ctorCalls);
        }
        finally
        {
            Marshal.FreeHGlobal(mod.FullPathCStr);
        }
    }

    [Fact]
    public void EnsureModuleInitialized_CoordinatesConcurrentThreadsWithoutDeadlockingOtherModules()
    {
        var loader = new Arm32ElfLoader(Path.GetTempPath());
        var modA = new MappedElfModule(Path.Combine(Path.GetTempPath(), "liba.so"), (IntPtr)0x70000102, 0x4003)
        {
            Relocated = true,
        };
        var modB = new MappedElfModule(Path.Combine(Path.GetTempPath(), "libb.so"), (IntPtr)0x70000103, 0x4004)
        {
            Relocated = true,
        };
        loader.PublishModuleForTest(modA);
        loader.PublishModuleForTest(modB);

        try
        {
            using var enteredCtorA = new ManualResetEventSlim(false);
            using var allowFinishA = new ManualResetEventSlim(false);
            int callsA = 0;
            int callsB = 0;

            // Thread 1 initializes modA; inside modA's ctor it spawns a helper thread initializing modB and waits for it.
            // Thread 2 concurrently waits on modA and must block until modA finishes without observing Initialized == true early.
            Task<bool> t1 = Task.Run(() => loader.EnsureModuleInitialized(modA, _ =>
            {
                Interlocked.Increment(ref callsA);
                enteredCtorA.Set();
                Task<bool> helperB = Task.Run(() =>
                {
                    IntPtr h = loader.ResolveOrStageDynamicLibrary("libb.so", out bool handled, __ =>
                    {
                        Interlocked.Increment(ref callsB);
                    });
                    Assert.True(handled);
                    return h == modB.SyntheticHandle;
                });
                Assert.True(helperB.Wait(TimeSpan.FromSeconds(5)));
                Assert.True(helperB.Result);
                allowFinishA.Wait(TimeSpan.FromSeconds(5));
            }));

            Assert.True(enteredCtorA.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(modA.Initialized);

            using var t2Started = new ManualResetEventSlim(false);
            bool t2SawInitializedBeforeFinish = false;
            Task<bool> t2 = Task.Run(() =>
            {
                t2SawInitializedBeforeFinish = modA.Initialized;
                t2Started.Set();
                IntPtr h = loader.ResolveOrStageDynamicLibrary("liba.so", out bool handled, _ =>
                {
                    Interlocked.Increment(ref callsA);
                });
                Assert.True(handled);
                return h == modA.SyntheticHandle && modA.Initialized;
            });

            Assert.True(t2Started.Wait(TimeSpan.FromSeconds(5)));
            Thread.Sleep(20);
            Assert.False(t2.IsCompleted);
            allowFinishA.Set();

            Assert.True(Task.WaitAll(new Task[] { t1, t2 }, TimeSpan.FromSeconds(5)));
            Assert.True(t1.Result);
            Assert.True(t2.Result);
            Assert.False(t2SawInitializedBeforeFinish);
            Assert.Equal(1, callsA);
            Assert.Equal(1, callsB);
        }
        finally
        {
            Marshal.FreeHGlobal(modA.FullPathCStr);
            Marshal.FreeHGlobal(modB.FullPathCStr);
        }
    }

    [Fact]
    public void HookedDlOpen_AndRunModuleInitializers_RejectUnrelocatedModulesWithoutSilentSuccess()
    {
        var loader = new Arm32ElfLoader(Path.GetTempPath());
        var unrelocated = new MappedElfModule(Path.Combine(Path.GetTempPath(), "libunreloc.so"), (IntPtr)0x70000104, 0x4005)
        {
            Relocated = false,
            BaseAddress = (IntPtr)0x11000000,
            LoadBias = 0x11000000,
        };
        unrelocated.ExportedSymbols["sym"] = 0x11000123;
        loader.PublishModuleForTest(unrelocated);

        try
        {
            IntPtr h = loader.ResolveOrStageDynamicLibrary("libunreloc.so", out bool handledDlOpen);
            Assert.True(handledDlOpen);
            Assert.Equal(IntPtr.Zero, h);
            Assert.False(unrelocated.Initialized);

            IntPtr symPtr = loader.ResolveRuntimeSymbol(unrelocated.SyntheticHandle, "sym", out bool handledSym);
            Assert.True(handledSym);
            Assert.Equal(IntPtr.Zero, symPtr);

            Assert.False(loader.EnsureModuleInitialized(unrelocated, _ => throw new InvalidOperationException("must not run")));
            Assert.Null(unrelocated.InitException);
            Assert.Throws<InvalidOperationException>(() => loader.RunModuleInitializers(unrelocated));
        }
        finally
        {
            Marshal.FreeHGlobal(unrelocated.FullPathCStr);
        }
    }

    [Fact]
    public void FailedInitModule_IsExcludedFromDlSymAndRelocationLookupAndSnapshotToleratesConcurrentPublish()
    {
        var loader = new Arm32ElfLoader(Path.GetTempPath());
        var failedMod = new MappedElfModule(Path.Combine(Path.GetTempPath(), "libfailed.so"), (IntPtr)0x70000105, 0x4006)
        {
            Relocated = true,
            BaseAddress = (IntPtr)0x12000000,
            LoadBias = 0x12000000,
        };
        failedMod.ExportedSymbols["shared_sym"] = 0x12000111;
        loader.PublishModuleForTest(failedMod);

        Assert.False(loader.EnsureModuleInitialized(failedMod, _ => throw new InvalidOperationException("init broken")));

        var healthyMod = new MappedElfModule(Path.Combine(Path.GetTempPath(), "libhealthy.so"), (IntPtr)0x70000106, 0x4007)
        {
            Relocated = true,
            BaseAddress = (IntPtr)0x13000000,
            LoadBias = 0x13000000,
        };
        healthyMod.ExportedSymbols["shared_sym"] = 0x13000222;
        loader.PublishModuleForTest(healthyMod);

        try
        {
            // Runtime dlsym against the failed module handle must fail cleanly.
            IntPtr failedSym = loader.ResolveRuntimeSymbol(failedMod.SyntheticHandle, "shared_sym", out bool handledFailed);
            Assert.True(handledFailed);
            Assert.Equal(IntPtr.Zero, failedSym);

            // Runtime dlopen against the failed module must keep returning IntPtr.Zero.
            Assert.Equal(IntPtr.Zero, loader.ResolveOrStageDynamicLibrary("libfailed.so", out bool handledOpen));
            Assert.True(handledOpen);

            // Relocation symbol resolution must skip failedMod and resolve from healthyMod instead.
            uint resolved = loader.ResolveExportFromMappedModules("shared_sym", out MappedElfModule? definer);
            Assert.Same(healthyMod, definer);
            Assert.Equal(0x13000222u, resolved);

            // Snapshot lookup stays valid under concurrent module publication.
            var extraMods = new List<MappedElfModule>();
            using var cts = new CancellationTokenSource();
            Task publisher = Task.Run(() =>
            {
                for (int i = 0; i < 64 && !cts.IsCancellationRequested; i++)
                {
                    var m = new MappedElfModule(Path.Combine(Path.GetTempPath(), $"libextra_{i}.so"), (IntPtr)(0x70000200 + i), (uint)(0x4100 + i))
                    {
                        Relocated = true,
                    };
                    m.ExportedSymbols[$"extra_sym_{i}"] = 0x14000000u + (uint)i * 4u;
                    extraMods.Add(m);
                    loader.PublishModuleForTest(m);
                }
            });

            while (!publisher.IsCompleted)
            {
                uint addr = loader.ResolveExportFromMappedModules("shared_sym", out MappedElfModule? def);
                Assert.Same(healthyMod, def);
                Assert.Equal(0x13000222u, addr);
            }
            publisher.GetAwaiter().GetResult();

            foreach (MappedElfModule m in extraMods)
                Marshal.FreeHGlobal(m.FullPathCStr);
        }
        finally
        {
            Marshal.FreeHGlobal(failedMod.FullPathCStr);
            Marshal.FreeHGlobal(healthyMod.FullPathCStr);
        }
    }

    [Fact]
    public void CommitDynamicModuleLocked_PrePublishFailuresLeaveManagedAndNativeTablesUntouched()
    {
        var loader = new Arm32ElfLoader(Path.GetTempPath());
        var hooks = new NativeHooks();
        hooks.EnsurePhdrTable();
        hooks.EnsureExidxAndTlsTablesForTest(baseModId: 0x4000, maxModules: 4);
        loader.ConfigureHooksForTest(hooks, useNativeHooks: true, dlIterateInstalled: true);

        // Case 1: Full PHDR table fails before publishing or mutating EXIDX/TLS.
        for (int i = 0; i < NativeHooks.MaxPhdrModules; i++)
            hooks.AddPhdrModule(0x10000000u + (uint)i * 0x1000u, 0x10001000u, 0x10002000u, 2);

        var phdrOverflowMod = new MappedElfModule(Path.Combine(Path.GetTempPath(), "libphdr_full.so"), (IntPtr)0x70000300, 0x4001)
        {
            Relocated = true,
            BaseAddress = (IntPtr)0x20000000,
            LoadBias = 0x20000000,
            TotalMappedSize = 0x4000,
            ProgramHeadersPtr = (IntPtr)0x20001000,
            ProgramHeaderCount = 3,
            ExidxSegment = new Elf32_Phdr { p_type = ElfConstants.PT_ARM_EXIDX, p_vaddr = 0x100, p_memsz = 16 },
            TlsMemSize = 32,
            TlsFileSize = 16,
            TlsTemplate = (IntPtr)0x20002000,
        };

        try
        {
            Assert.Throws<InvalidOperationException>(() => loader.CommitDynamicModuleForTest(phdrOverflowMod));
            Assert.False(phdrOverflowMod.Published);
            Assert.False(loader.IsModulePublishedForTest(phdrOverflowMod));
            Assert.Equal(NativeHooks.MaxPhdrModules, hooks.PhdrCount);
            Assert.Equal(0, hooks.ExidxCount);
        }
        finally
        {
            Marshal.FreeHGlobal(phdrOverflowMod.FullPathCStr);
        }

        // Case 2: Full TLS table fails before publishing or mutating PHDR/EXIDX.
        var tlsHooks = new NativeHooks();
        tlsHooks.EnsurePhdrTable();
        tlsHooks.EnsureExidxAndTlsTablesForTest(baseModId: 0x4000, maxModules: 2);
        loader.ConfigureHooksForTest(tlsHooks, useNativeHooks: true, dlIterateInstalled: true);

        var tlsOverflowMod = new MappedElfModule(Path.Combine(Path.GetTempPath(), "libtls_full.so"), (IntPtr)0x70000301, 0x4005)
        {
            Relocated = true,
            BaseAddress = (IntPtr)0x21000000,
            LoadBias = 0x21000000,
            TotalMappedSize = 0x4000,
            ExidxSegment = new Elf32_Phdr { p_type = ElfConstants.PT_ARM_EXIDX, p_vaddr = 0x100, p_memsz = 16 },
            TlsMemSize = 32,
            TlsFileSize = 16,
            TlsTemplate = (IntPtr)0x21002000,
        };

        try
        {
            Assert.Throws<InvalidOperationException>(() => loader.CommitDynamicModuleForTest(tlsOverflowMod));
            Assert.False(tlsOverflowMod.Published);
            Assert.False(loader.IsModulePublishedForTest(tlsOverflowMod));
            Assert.Equal(0, tlsHooks.ExidxCount);
            Assert.Equal(0, tlsHooks.PhdrCount);
        }
        finally
        {
            Marshal.FreeHGlobal(tlsOverflowMod.FullPathCStr);
        }

        // Case 3: Full EXIDX table fails before publishing or mutating PHDR/TLS.
        for (int i = 0; i < NativeHooks.MaxExidxEntries - 1; i++)
            tlsHooks.AddExidxEntry(0x30000000u + (uint)i * 0x1000u, 0x30000800u + (uint)i * 0x1000u, 0x30000100u, 1);

        var exidxOverflowMod = new MappedElfModule(Path.Combine(Path.GetTempPath(), "libexidx_full.so"), (IntPtr)0x70000302, 0x4001)
        {
            Relocated = true,
            BaseAddress = (IntPtr)0x22000000,
            LoadBias = 0x22000000,
            TotalMappedSize = 0x4000,
            ExidxSegment = new Elf32_Phdr { p_type = ElfConstants.PT_ARM_EXIDX, p_vaddr = 0x100, p_memsz = 16 },
            TlsMemSize = 32,
            TlsFileSize = 16,
            TlsTemplate = (IntPtr)0x22002000,
        };

        try
        {
            Assert.Throws<InvalidOperationException>(() => loader.CommitDynamicModuleForTest(exidxOverflowMod));
            Assert.False(exidxOverflowMod.Published);
            Assert.False(loader.IsModulePublishedForTest(exidxOverflowMod));
            Assert.Equal(0, tlsHooks.PhdrCount);
        }
        finally
        {
            Marshal.FreeHGlobal(exidxOverflowMod.FullPathCStr);
        }

        // Case 4 (64-bit host): ToArm32Address overflow throws before publishing or mutating tables.
        if (IntPtr.Size == 8)
        {
            var addrHooks = new NativeHooks();
            addrHooks.EnsurePhdrTable();
            addrHooks.EnsureExidxAndTlsTablesForTest(baseModId: 0x4000, maxModules: 4);
            loader.ConfigureHooksForTest(addrHooks, useNativeHooks: true, dlIterateInstalled: true);

            var overflowPtrMod = new MappedElfModule(Path.Combine(Path.GetTempPath(), "libaddr_ovf.so"), (IntPtr)0x70000303, 0x4001)
            {
                Relocated = true,
                BaseAddress = (IntPtr)0x1_0000_0000L,
                LoadBias = 0x23000000,
                TotalMappedSize = 0x4000,
                ExidxSegment = new Elf32_Phdr { p_type = ElfConstants.PT_ARM_EXIDX, p_vaddr = 0x100, p_memsz = 16 },
            };

            try
            {
                Assert.Throws<OverflowException>(() => loader.CommitDynamicModuleForTest(overflowPtrMod));
                Assert.False(overflowPtrMod.Published);
                Assert.False(loader.IsModulePublishedForTest(overflowPtrMod));
                Assert.Equal(0, addrHooks.ExidxCount);
                Assert.Equal(0, addrHooks.PhdrCount);
            }
            finally
            {
                Marshal.FreeHGlobal(overflowPtrMod.FullPathCStr);
            }
        }
    }

    [Fact]
    public void CommitDynamicModuleLocked_PublishesManagedAndExidxEntriesTogetherOnSuccess()
    {
        var loader = new Arm32ElfLoader(Path.GetTempPath());
        var hooks = new NativeHooks();
        hooks.EnsurePhdrTable();
        hooks.EnsureExidxAndTlsTablesForTest(baseModId: 0x4000, maxModules: 4);
        loader.ConfigureHooksForTest(hooks, useNativeHooks: true, dlIterateInstalled: true);

        var okMod = new MappedElfModule(Path.Combine(Path.GetTempPath(), "libcommit_ok.so"), (IntPtr)0x70000400, 0x4001)
        {
            Relocated = true,
            BaseAddress = (IntPtr)0x24000000,
            LoadBias = 0x24000000,
            TotalMappedSize = 0x8000,
            ExidxSegment = new Elf32_Phdr { p_type = ElfConstants.PT_ARM_EXIDX, p_vaddr = 0x200, p_memsz = 24 },
            TlsMemSize = 32,
            TlsFileSize = 16,
            TlsTemplate = (IntPtr)0x24004000,
        };
        okMod.ExportedSymbols["dyn_export"] = 0x24000120u;

        try
        {
            loader.CommitDynamicModuleForTest(okMod);

            Assert.True(okMod.Published);
            Assert.True(loader.IsModulePublishedForTest(okMod));
            Assert.Equal(1, hooks.ExidxCount);
            Assert.Equal(0x24000120u, loader.ResolveExportFromMappedModules("dyn_export", out MappedElfModule? definer));
            Assert.Same(okMod, definer);
        }
        finally
        {
            Marshal.FreeHGlobal(okMod.FullPathCStr);
        }
    }

    [Fact]
    [Trait("Category", "Arm32Device")]
    public unsafe void MapLibraryAndEmitDlIteratePhdrWorkOnArm32Target()
    {
        Assert.Equal(Architecture.Arm, RuntimeInformation.ProcessArchitecture);

        string dir = Path.Combine(Path.GetTempPath(), "fcmap-arm32-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string soPath = Path.Combine(dir, "libtest_phdrs.so");
        MappedElfModule? mod = null;
        try
        {
            File.WriteAllBytes(soPath, BuildMinimalArm32SharedObject());
            var loader = new Arm32ElfLoader(dir);
            mod = loader.MapLibrary(soPath);
            loader.InstallDlIteratePhdrHook();

            Assert.Equal((ushort)6, mod.ProgramHeaderCount);
            Assert.NotEqual(IntPtr.Zero, mod.ProgramHeadersPtr);
            Assert.True(mod.DynamicSegment.HasValue);
            Assert.True(mod.TlsSegment.HasValue);
            Assert.True(mod.ExidxSegment.HasValue);
        }
        finally
        {
            if (mod != null)
            {
                if (mod.BaseAddress != IntPtr.Zero && mod.TotalMappedSize > 0)
                    NativeLinux.Munmap(mod.BaseAddress, mod.TotalMappedSize);
                if (mod.ProgramHeadersPtr != IntPtr.Zero)
                    Marshal.FreeHGlobal(mod.ProgramHeadersPtr);
                if (mod.FullPathCStr != IntPtr.Zero)
                    Marshal.FreeHGlobal(mod.FullPathCStr);
            }
            Directory.Delete(dir, recursive: true);
        }
    }

    private static unsafe byte[] BuildMinimalArm32SharedObject()
    {
        const ushort phnum = 6;
        const uint ehdrSize = 52;
        const uint phdrSize = 32;
        const uint phoff = ehdrSize;
        const uint dynOff = 0x200;
        const uint strOff = 0x240;
        const uint symOff = 0x250;
        const uint tlsOff = 0x280;
        const uint ehFrameOff = 0x2A0;
        const uint exidxOff = 0x2C0;
        const uint fileSize = 0x300;

        byte[] img = new byte[fileSize];
        fixed (byte* p = img)
        {
            Elf32_Ehdr* eh = (Elf32_Ehdr*)p;
            eh->e_ident[0] = 0x7F;
            eh->e_ident[1] = (byte)'E';
            eh->e_ident[2] = (byte)'L';
            eh->e_ident[3] = (byte)'F';
            eh->e_ident[4] = 1; // ELFCLASS32
            eh->e_ident[5] = 1; // ELFDATA2LSB
            eh->e_ident[6] = 1; // EV_CURRENT
            eh->e_type = ElfConstants.ET_DYN;
            eh->e_machine = ElfConstants.EM_ARM;
            eh->e_version = 1;
            eh->e_phoff = phoff;
            eh->e_ehsize = (ushort)ehdrSize;
            eh->e_phentsize = (ushort)phdrSize;
            eh->e_phnum = phnum;

            Elf32_Phdr* ph = (Elf32_Phdr*)(p + phoff);
            ph[0] = new Elf32_Phdr
            {
                p_type = 6,
                p_offset = phoff,
                p_vaddr = phoff,
                p_paddr = phoff,
                p_filesz = phnum * phdrSize,
                p_memsz = phnum * phdrSize,
                p_flags = ElfConstants.PF_R,
                p_align = 4,
            };
            ph[1] = new Elf32_Phdr
            {
                p_type = ElfConstants.PT_LOAD,
                p_offset = 0,
                p_vaddr = 0,
                p_paddr = 0,
                p_filesz = fileSize,
                p_memsz = fileSize,
                p_flags = ElfConstants.PF_R | ElfConstants.PF_W | ElfConstants.PF_X,
                p_align = 0x1000,
            };
            ph[2] = new Elf32_Phdr
            {
                p_type = ElfConstants.PT_DYNAMIC,
                p_offset = dynOff,
                p_vaddr = dynOff,
                p_paddr = dynOff,
                p_filesz = 32,
                p_memsz = 32,
                p_flags = ElfConstants.PF_R | ElfConstants.PF_W,
                p_align = 4,
            };
            ph[3] = new Elf32_Phdr
            {
                p_type = ElfConstants.PT_TLS,
                p_offset = tlsOff,
                p_vaddr = tlsOff,
                p_paddr = tlsOff,
                p_filesz = 4,
                p_memsz = 16,
                p_flags = ElfConstants.PF_R,
                p_align = 4,
            };
            ph[4] = new Elf32_Phdr
            {
                p_type = 0x6474E550u,
                p_offset = ehFrameOff,
                p_vaddr = ehFrameOff,
                p_paddr = ehFrameOff,
                p_filesz = 8,
                p_memsz = 8,
                p_flags = ElfConstants.PF_R,
                p_align = 4,
            };
            ph[5] = new Elf32_Phdr
            {
                p_type = ElfConstants.PT_ARM_EXIDX,
                p_offset = exidxOff,
                p_vaddr = exidxOff,
                p_paddr = exidxOff,
                p_filesz = 8,
                p_memsz = 8,
                p_flags = ElfConstants.PF_R,
                p_align = 4,
            };

            Elf32_Dyn* dyn = (Elf32_Dyn*)(p + dynOff);
            dyn[0] = new Elf32_Dyn { d_tag = ElfConstants.DT_STRTAB, d_val = strOff };
            dyn[1] = new Elf32_Dyn { d_tag = ElfConstants.DT_SYMTAB, d_val = symOff };
            dyn[2] = new Elf32_Dyn { d_tag = ElfConstants.DT_STRSZ, d_val = 16 };
            dyn[3] = new Elf32_Dyn { d_tag = ElfConstants.DT_NULL, d_val = 0 };
        }
        return img;
    }
}
