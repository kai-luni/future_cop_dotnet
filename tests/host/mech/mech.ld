/* Link script of the mech harness: a private script instead of ld's default so that _gp can be
 * pinned to the retail $gp (0x8009F214) mapped by -0x70000000; the default Linux script assigns
 * its own _gp and the gp-relative accesses of the -G4 retail units would not reach their data. */
ENTRY(__start)
SECTIONS
{
  . = 0x00400000 + SIZEOF_HEADERS;
  .text : { *(.text*) }
  .rodata : { *(.rodata*) }
  . = ALIGN(0x1000);
  .data : { *(.data*) *(.sdata*) }
  .bss : { *(.bss*) *(.sbss*) *(.scommon) *(COMMON) }
  .mech_entity 0x0E000000 : { *(.mech_entity) }
  .retailram 0x10010000 : { *(.retailram) }
  .scratch 0x1F800000 : { *(.scratch) }
  /DISCARD/ : { *(.MIPS.abiflags) *(.reginfo) *(.pdr) *(.gnu.attributes) *(.comment) *(.mdebug*) }
}
_gp = 0x1009F214;
