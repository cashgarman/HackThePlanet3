#!/usr/bin/env python3
"""Search a binary for a word or phrase encoded in many charsets (Ghidra-style).

For each supported encoding, the query text is encoded to bytes and every
occurrence is located in the file. Results include file offset, optional
image-base virtual address, encoding name, and a short preview.

Examples:
  python scripts/search_encoded_strings.py game.exe "minidump"
  python scripts/search_encoded_strings.py dump.bin "vector" --image-base 0x140000000
  python scripts/search_encoded_strings.py game.exe "密码" --charsets gbk,gb18030,utf-8
"""

from __future__ import annotations

import argparse
import codecs
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path


# Encodings that are not useful for raw binary string search.
_SKIP_PREFIXES = (
    "cp7",  # codec state machines, not static byte maps
)
_SKIP_NAMES = frozenset(
    {
        "idna",
        "punycode",
        "rot_13",
        "hex_codec",
        "unicode_escape",
        "raw_unicode_escape",
        "unicode_internal",
        "mbcs",
        "oem",
        "undefined",
        "base64_codec",
        "bz2_codec",
        "quopri_codec",
        "uu_codec",
        "zlib_codec",
    }
)

# Ghidra-like defaults: always try these first (duplicates removed later).
_PRIORITY = (
    "ascii",
    "utf-8",
    "utf-16-le",
    "utf-16-be",
    "utf-32-le",
    "utf-32-be",
    "latin-1",
    "cp1252",
    "cp437",
    "cp850",
    "cp866",
    "iso-8859-1",
    "iso-8859-15",
    "big5",
    "big5hkscs",
    "gbk",
    "gb18030",
    "gb2312",
    "euc-jp",
    "euc-kr",
    "shift_jis",
    "iso-2022-jp",
    "mac_roman",
    "koi8-r",
    "koi8-u",
)


@dataclass(frozen=True)
class Match:
    offset: int
    needle: bytes
    encodings: tuple[str, ...]
    preview: str


def _parse_int(value: str) -> int:
    value = value.strip().lower()
    return int(value, 16 if value.startswith("0x") else 10)


def _is_searchable_encoding(name: str) -> bool:
    lower = name.lower()
    if lower in _SKIP_NAMES:
        return False
    if any(lower.startswith(p) for p in _SKIP_PREFIXES):
        return False
    if lower.endswith("_codec"):
        return False
    return True


def _discover_encodings(
    extra: list[str] | None, *, all_charsets: bool, only_extra: bool = False
) -> list[str]:
    seen: set[str] = set()
    ordered: list[str] = []

    def add(name: str) -> None:
        key = name.lower()
        if key in seen:
            return
        seen.add(key)
        ordered.append(name)

    if not only_extra:
        for name in _PRIORITY:
            add(name)
    if extra:
        for name in extra:
            add(name.strip())
    if all_charsets:
        try:
            from encodings import aliases

            for name in aliases.aliases.values():
                if _is_searchable_encoding(name):
                    add(name)
            for name in aliases.aliases:
                if _is_searchable_encoding(name):
                    add(name)
        except Exception:
            pass
    return ordered


def _build_needle_index(
    query: str, encodings: list[str], case_insensitive: bool
) -> tuple[dict[bytes, list[str]], int]:
    """Map encoded needle bytes -> encoding names that produce them."""
    index: dict[bytes, list[str]] = {}
    skipped = 0
    for enc in encodings:
        needles = _encode_needles(query, enc, case_insensitive)
        if not needles:
            skipped += 1
            continue
        for needle in needles:
            index.setdefault(needle, []).append(enc)
    for needle in index:
        index[needle] = sorted(set(index[needle]), key=str.lower)
    return index, skipped


def _encode_needles(text: str, encoding: str, case_insensitive: bool) -> list[bytes]:
    variants = {text}
    if case_insensitive:
        variants.add(text.casefold())
    needles: list[bytes] = []
    for variant in variants:
        try:
            needles.append(variant.encode(encoding))
        except (LookupError, UnicodeEncodeError):
            continue
    # Deduplicate while preserving order.
    out: list[bytes] = []
    seen: set[bytes] = set()
    for n in needles:
        if n and n not in seen:
            seen.add(n)
            out.append(n)
    return out


def _preview(data: bytes, start: int, end: int, width: int = 48) -> str:
    lo = max(0, start - width // 4)
    hi = min(len(data), end + width // 4)
    chunk = data[lo:hi]
    # Prefer UTF-8 for display; fall back to escaped bytes.
    for enc in ("utf-8", "latin-1"):
        try:
            s = chunk.decode(enc)
            s = s.replace("\r", "\\r").replace("\n", "\\n").replace("\t", "\\t")
            s = re.sub(r"[\x00-\x08\x0b\x0c\x0e-\x1f]", ".", s)
            if len(s) > width:
                s = s[: width - 3] + "..."
            return repr(s)
        except UnicodeDecodeError:
            continue
    return chunk[:width].hex()


def _find_all(haystack: bytes, needle: bytes, start: int, end: int) -> list[int]:
    hits: list[int] = []
    pos = start
    while pos < end:
        idx = haystack.find(needle, pos, end)
        if idx < 0:
            break
        hits.append(idx)
        pos = idx + 1
    return hits


def search_file(
    path: Path,
    query: str,
    *,
    encodings: list[str],
    case_insensitive: bool,
    region_start: int,
    region_end: int | None,
    max_hits: int,
) -> tuple[list[Match], dict[str, int]]:
    data = path.read_bytes()
    file_len = len(data)
    end = file_len if region_end is None else min(region_end, file_len)
    start = max(0, min(region_start, file_len))
    if start >= end:
        raise ValueError(f"Search region [{start:#x}, {end:#x}) is empty for file size {file_len:#x}")

    needle_index, skipped = _build_needle_index(query, encodings, case_insensitive)
    stats = {
        "encodings_tried": len(encodings),
        "unique_needles": len(needle_index),
        "encodings_skipped": skipped,
    }
    matches: list[Match] = []

    for needle, enc_names in sorted(needle_index.items(), key=lambda kv: (-len(kv[0]), kv[0])):
        for off in _find_all(data, needle, start, end):
            preview = _preview(data, off, off + len(needle))
            matches.append(Match(off, needle, tuple(enc_names), preview))
            if len(matches) >= max_hits:
                return matches, stats

    matches.sort(key=lambda m: (m.offset, m.needle))
    return matches, stats


@dataclass(frozen=True)
class MemoryRegion:
    dump_offset: int
    address: int
    length: int


def _load_region_map(path: Path) -> list[MemoryRegion]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    raw_regions = payload.get("regions", payload)
    regions: list[MemoryRegion] = []
    for item in raw_regions:
        addr = item["address"]
        if isinstance(addr, str):
            addr = _parse_int(addr)
        regions.append(
            MemoryRegion(
                dump_offset=int(item["dump_offset"]),
                address=int(addr),
                length=int(item["length"]),
            )
        )
    regions.sort(key=lambda r: r.dump_offset)
    return regions


def _dump_offset_to_va(dump_offset: int, regions: list[MemoryRegion]) -> int | None:
    for region in regions:
        if region.dump_offset <= dump_offset < region.dump_offset + region.length:
            return region.address + (dump_offset - region.dump_offset)
    return None


def _format_location(
    offset: int, image_base: int | None, regions: list[MemoryRegion] | None
) -> str:
    if regions:
        va = _dump_offset_to_va(offset, regions)
        if va is not None:
            return f"{va:#x}"
        return f"dump{offset:#x}"
    if image_base is None:
        return f"{offset:#x}"
    return f"{image_base + offset:#x} (off {offset:#x})"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Search a binary for a phrase under many character encodings."
    )
    parser.add_argument("binary", type=Path, help="Path to the file to scan")
    parser.add_argument("query", help="Word or phrase to find")
    parser.add_argument(
        "--charsets",
        help="Comma-separated encodings to use (replaces the default priority set)",
    )
    parser.add_argument(
        "--all-charsets",
        action="store_true",
        help="Include every discoverable Python codec alias (slow; many duplicates for ASCII)",
    )
    parser.add_argument(
        "--list-charsets",
        action="store_true",
        help="Print discoverable encodings and exit",
    )
    parser.add_argument(
        "-i",
        "--ignore-case",
        action="store_true",
        help="Also search a case-folded variant (Latin/CJK safe where encodable)",
    )
    parser.add_argument(
        "--start",
        type=_parse_int,
        default=0,
        help="Start offset (decimal or 0x hex)",
    )
    parser.add_argument(
        "--end",
        type=_parse_int,
        default=None,
        help="End offset exclusive (decimal or 0x hex); default = EOF",
    )
    parser.add_argument(
        "--image-base",
        type=_parse_int,
        default=None,
        help="Add to each hit offset for VA display (e.g. 0x140000000)",
    )
    parser.add_argument(
        "--max-hits",
        type=int,
        default=10_000,
        help="Stop after this many matches (default 10000)",
    )
    parser.add_argument(
        "--csv",
        action="store_true",
        help="Print CSV: location,offset,encoding,length,preview",
    )
    parser.add_argument(
        "--region-map",
        type=Path,
        default=None,
        help="JSON map from concatenated dump offsets to Ghidra VAs (see scripts/ghidra/)",
    )
    parser.add_argument(
        "--ghidra-csv",
        action="store_true",
        help="CSV for Ghidra GUI: address,dump_offset,length,encodings,preview",
    )
    args = parser.parse_args(argv)

    if args.charsets:
        encoding_list = _discover_encodings(
            [s.strip() for s in args.charsets.split(",") if s.strip()],
            all_charsets=False,
            only_extra=True,
        )
    else:
        encoding_list = _discover_encodings(None, all_charsets=args.all_charsets)

    if args.list_charsets:
        for name in encoding_list:
            print(name)
        return 0

    if not args.binary.is_file():
        print(f"error: not a file: {args.binary}", file=sys.stderr)
        return 2

    region_map: list[MemoryRegion] | None = None
    if args.region_map is not None:
        region_map = _load_region_map(args.region_map)
        if args.image_base is not None:
            print(
                "warning: --image-base ignored when --region-map is set",
                file=sys.stderr,
            )

    matches, stats = search_file(
        args.binary,
        args.query,
        encodings=encoding_list,
        case_insensitive=args.ignore_case,
        region_start=args.start,
        region_end=args.end,
        max_hits=args.max_hits,
    )

    if args.ghidra_csv:
        print("address,dump_offset,length,encodings,preview")
        for m in matches:
            if region_map:
                mapped = _dump_offset_to_va(m.offset, region_map)
                va = f"{mapped:#x}" if mapped is not None else f"dump{m.offset:#x}"
            else:
                va = _format_location(m.offset, args.image_base, None)
            prev = m.preview.replace('"', '""')
            enc = ";".join(m.encodings).replace('"', '""')
            print(f'{va},{m.offset},{len(m.needle)},"{enc}","{prev}"')
    elif args.csv:
        print("location,offset,encodings,length,needle_hex,preview")
        for m in matches:
            loc = _format_location(m.offset, args.image_base, region_map)
            prev = m.preview.replace('"', '""')
            enc = ";".join(m.encodings).replace('"', '""')
            print(
                f'"{loc}",{m.offset},"{enc}",{len(m.needle)},'
                f'"{m.needle.hex()}","{prev}"'
            )
    else:
        for m in matches:
            loc = _format_location(m.offset, args.image_base, region_map)
            enc = ", ".join(m.encodings[:8])
            if len(m.encodings) > 8:
                enc += f", … (+{len(m.encodings) - 8} more)"
            print(f"{loc}\tlen={len(m.needle)}\t[{enc}]\t{m.preview}")

    unique_offsets = len({m.offset for m in matches})
    print(
        f"\n{len(matches)} hit(s) at {unique_offsets} offset(s); "
        f"{stats['unique_needles']} unique needle byte pattern(s); "
        f"{stats['encodings_skipped']} encoding(s) could not encode the query",
        file=sys.stderr,
    )
    return 0 if matches else 1


if __name__ == "__main__":
    raise SystemExit(main())
