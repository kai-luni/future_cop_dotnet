# Disc-Extraktor: Falscher Skriptname in der Hilfe

## Problem

Der Beispielbefehl in der Hilfe verweist auf `extract_disc.py`, obwohl das Skript im Repository `scripts/fc_extract.py` heisst.

## Beleg

- `scripts/fc_extract.py:4-5` — der Docstring enthaelt den falschen Beispielbefehl.
- `scripts/fc_extract.py:116-120` — argparse zeigt den Docstring mit `--help` an; der falsche Befehl ist dort reproduzierbar.

## Aufgabe

Den Beispielbefehl in der Hilfe auf den tatsaechlichen Skriptpfad umstellen.

## Akzeptanz

- `python3 scripts/fc_extract.py --help` zeigt einen aus dem Repository ausfuehrbaren Beispielbefehl.
