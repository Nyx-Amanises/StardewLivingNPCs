#!/usr/bin/env python3
"""Validate LivingNPCs npc_bios JSON files without loading Stardew Valley or SMAPI."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any


MAX_FILE_BYTES = 64 * 1024
MAX_BIOGRAPHY_LENGTH = 16_384
MAX_LONG_TEXT_LENGTH = 4_096
MAX_DESCRIPTION_LENGTH = 2_048
MAX_SHORT_TEXT_LENGTH = 512
MAX_IDENTIFIER_LENGTH = 128
MAX_COLLECTION_ENTRIES = 128
MAX_PORTRAIT_FRAME = 4095

FIELDS: dict[str, type] = {
    "Biography": str,
    "Relationships": dict,
    "Traits": dict,
    "BiographyEnd": str,
    "Gender": str,
    "Unique": str,
    "ExtraPortraits": dict,
    "Preoccupations": list,
    "Dialogue": dict,
    "HomeLocationBed": bool,
    "UsePatchedDialogue": bool,
    "PromptOverrides": dict,
}


class DuplicateKeyError(ValueError):
    pass


LOCALE_PATTERN = re.compile(r"^[A-Za-z0-9]{1,16}(?:-[A-Za-z0-9]{1,16})*$")
SOURCE_ID_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]{1,126}[A-Za-z0-9]$")
WINDOWS_INVALID_FILENAME = re.compile(r'[<>:"/\\|?*]|[\x00-\x1f]')
WINDOWS_RESERVED_NAMES = {
    "CON",
    "PRN",
    "AUX",
    "NUL",
    *(f"COM{index}" for index in range(1, 10)),
    *(f"LPT{index}" for index in range(1, 10)),
}
JSON_METADATA_PROPERTIES = {"$id", "$ref", "$type", "$values"}


def reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise DuplicateKeyError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def reject_metadata_properties(value: Any, path: str = "$") -> list[str]:
    """Reject reserved metadata and invalid Unicode strings at any depth."""
    errors: list[str] = []
    if isinstance(value, dict):
        for key, child in value.items():
            display_key = key.encode("unicode_escape").decode("ascii")
            child_path = f"{path}.{display_key}"
            if any("\ud800" <= character <= "\udfff" for character in key):
                errors.append(f"unpaired UTF-16 surrogate is not allowed in a property name at {path}")
            if key in JSON_METADATA_PROPERTIES:
                errors.append(f"reserved JSON metadata property {key!r} is not allowed at {path}")
            errors.extend(reject_metadata_properties(child, child_path))
    elif isinstance(value, list):
        for index, child in enumerate(value):
            errors.extend(reject_metadata_properties(child, f"{path}[{index}]"))
    elif isinstance(value, str) and any("\ud800" <= character <= "\udfff" for character in value):
        errors.append(f"unpaired UTF-16 surrogate is not allowed at {path}")
    return errors


def require_text(
    errors: list[str],
    value: Any,
    field: str,
    max_length: int,
    *,
    allow_blank: bool,
) -> None:
    if not isinstance(value, str):
        errors.append(f"{field} must be a string")
        return
    if not allow_blank and not value.strip():
        errors.append(f"{field} must not be blank")
    if len(value) > max_length:
        errors.append(f"{field} is longer than {max_length} characters")


def validate_entry_map(errors: list[str], value: Any, field: str) -> None:
    if not isinstance(value, dict):
        return
    if len(value) > MAX_COLLECTION_ENTRIES:
        errors.append(f"{field} has more than {MAX_COLLECTION_ENTRIES} entries")
    for key, entry in value.items():
        require_text(errors, key, f"{field} key", MAX_IDENTIFIER_LENGTH, allow_blank=False)
        if not isinstance(entry, dict):
            errors.append(f"{field}[{key!r}] must be an object")
            continue
        expected = {"id", "Heading", "Description"}
        missing = sorted(expected - set(entry))
        unknown = sorted(set(entry) - expected)
        if missing:
            errors.append(f"{field}[{key!r}] is missing: {', '.join(missing)}")
        if unknown:
            errors.append(f"{field}[{key!r}] has unknown fields: {', '.join(unknown)}")
        if "id" in entry:
            require_text(errors, entry["id"], f"{field}[{key!r}].id", MAX_IDENTIFIER_LENGTH, allow_blank=False)
        if "Heading" in entry:
            require_text(errors, entry["Heading"], f"{field}[{key!r}].Heading", MAX_SHORT_TEXT_LENGTH, allow_blank=False)
        if "Description" in entry:
            require_text(errors, entry["Description"], f"{field}[{key!r}].Description", MAX_DESCRIPTION_LENGTH, allow_blank=False)


def validate_string_map(
    errors: list[str], value: Any, field: str, max_value_length: int
) -> None:
    if not isinstance(value, dict):
        return
    if len(value) > MAX_COLLECTION_ENTRIES:
        errors.append(f"{field} has more than {MAX_COLLECTION_ENTRIES} entries")
    for key, text in value.items():
        require_text(errors, key, f"{field} key", MAX_IDENTIFIER_LENGTH, allow_blank=False)
        require_text(errors, text, f"{field}[{key!r}]", max_value_length, allow_blank=False)


def normalize_extra_marker(marker: str) -> str | None:
    value = marker.strip()
    if value.lower() == "u":
        return "u"
    if not value or (len(value) > 1 and value.startswith("0")) or not value.isascii() or not value.isdigit():
        return None
    frame = int(value)
    return str(frame) if 6 <= frame <= MAX_PORTRAIT_FRAME else None


def validate_document(data: Any) -> tuple[list[str], list[str]]:
    errors: list[str] = []
    warnings: list[str] = []
    if not isinstance(data, dict):
        return ["top-level JSON value must be an object"], warnings

    errors.extend(reject_metadata_properties(data))

    missing = sorted(set(FIELDS) - set(data))
    unknown = sorted(set(data) - set(FIELDS))
    if missing:
        errors.append(f"missing top-level fields: {', '.join(missing)}")
    if unknown:
        errors.append(f"unknown top-level fields: {', '.join(unknown)}")

    for field, expected_type in FIELDS.items():
        if field not in data:
            continue
        value = data[field]
        if expected_type is bool:
            valid_type = type(value) is bool
        else:
            valid_type = isinstance(value, expected_type)
        if not valid_type:
            errors.append(f"{field} must be {expected_type.__name__}, got {type(value).__name__}")

    if isinstance(data.get("Biography"), str):
        require_text(errors, data["Biography"], "Biography", MAX_BIOGRAPHY_LENGTH, allow_blank=False)
    if isinstance(data.get("BiographyEnd"), str):
        require_text(errors, data["BiographyEnd"], "BiographyEnd", MAX_LONG_TEXT_LENGTH, allow_blank=True)
    if isinstance(data.get("Gender"), str):
        require_text(errors, data["Gender"], "Gender", MAX_IDENTIFIER_LENGTH, allow_blank=True)
    if isinstance(data.get("Unique"), str):
        require_text(errors, data["Unique"], "Unique", MAX_SHORT_TEXT_LENGTH, allow_blank=True)

    validate_entry_map(errors, data.get("Relationships"), "Relationships")
    validate_entry_map(errors, data.get("Traits"), "Traits")
    validate_string_map(errors, data.get("Dialogue"), "Dialogue", MAX_LONG_TEXT_LENGTH)
    validate_string_map(errors, data.get("PromptOverrides"), "PromptOverrides", MAX_BIOGRAPHY_LENGTH)

    extra_portraits = data.get("ExtraPortraits")
    if isinstance(extra_portraits, dict):
        if len(extra_portraits) > MAX_COLLECTION_ENTRIES:
            errors.append(f"ExtraPortraits has more than {MAX_COLLECTION_ENTRIES} entries")
        normalized: set[str] = set()
        for marker, description in extra_portraits.items():
            require_text(errors, marker, "ExtraPortraits key", MAX_IDENTIFIER_LENGTH, allow_blank=False)
            if isinstance(marker, str):
                frame = normalize_extra_marker(marker)
                if frame is None:
                    errors.append(f"ExtraPortraits key {marker!r} must be 'u' or a decimal frame from 6 to {MAX_PORTRAIT_FRAME}")
                elif frame in normalized:
                    errors.append(f"ExtraPortraits contains duplicate frame marker {frame!r}")
                else:
                    normalized.add(frame)
            require_text(errors, description, f"ExtraPortraits[{marker!r}]", MAX_SHORT_TEXT_LENGTH, allow_blank=False)

    preoccupations = data.get("Preoccupations")
    if isinstance(preoccupations, list):
        if len(preoccupations) > MAX_COLLECTION_ENTRIES:
            errors.append(f"Preoccupations has more than {MAX_COLLECTION_ENTRIES} entries")
        for index, item in enumerate(preoccupations):
            require_text(errors, item, f"Preoccupations[{index}]", MAX_SHORT_TEXT_LENGTH, allow_blank=False)

    if data.get("UsePatchedDialogue") is True:
        errors.append("UsePatchedDialogue must be false in npc_bios; use an authorized Content Patcher pack instead")
    if isinstance(data.get("Dialogue"), dict) and data["Dialogue"]:
        warnings.append("Dialogue is non-empty; verify redistribution and AI-use permission for every sample")
    if isinstance(data.get("PromptOverrides"), dict) and data["PromptOverrides"]:
        errors.append("PromptOverrides must be empty in npc_bios because community data may not replace trusted prompt sections")
    if isinstance(extra_portraits, dict) and extra_portraits:
        warnings.append("ExtraPortraits is non-empty; verify each marker against the final portrait sheet")

    return errors, warnings


def validate_file(path: Path) -> tuple[list[str], list[str]]:
    if path.stat().st_size > MAX_FILE_BYTES:
        return [f"file size exceeds {MAX_FILE_BYTES} bytes"], []
    try:
        data = json.loads(
            path.read_text(encoding="utf-8-sig"),
            object_pairs_hook=reject_duplicate_keys,
        )
    except (OSError, UnicodeError, json.JSONDecodeError, DuplicateKeyError) as exc:
        return [f"cannot parse JSON: {exc}"], []
    return validate_document(data)


def canonical_locale(locale: str) -> str:
    segments = locale.split("-")
    canonical = [segments[0].lower()]
    for segment in segments[1:]:
        if len(segment) == 2 and segment.isalpha():
            canonical.append(segment.upper())
        elif len(segment) == 4 and segment.isalpha():
            canonical.append(segment.title())
        else:
            canonical.append(segment)
    return "-".join(canonical)


def validate_portable_segment(value: str, label: str) -> list[str]:
    errors: list[str] = []
    if value != value.strip():
        errors.append(f"{label} has leading or trailing whitespace: {value!r}")
    if value in ("", ".", "..") or WINDOWS_INVALID_FILENAME.search(value) or value.endswith((".", " ")):
        errors.append(f"invalid {label}: {value!r}")
    if value.split(".", 1)[0].upper() in WINDOWS_RESERVED_NAMES:
        errors.append(f"{label} uses a Windows reserved device name: {value!r}")
    return errors


def layout_diagnostics(
    root: Path, files: list[Path], *, strict_layout: bool = False
) -> tuple[dict[Path, list[str]], dict[Path, list[str]]]:
    errors: dict[Path, list[str]] = {path: [] for path in files}
    warnings: dict[Path, list[str]] = {path: [] for path in files}
    names_by_directory: dict[Path, dict[str, Path]] = {}

    for path in files:
        relative = path.relative_to(root)
        parts = relative.parts
        if path.suffix != ".json":
            errors[path].append("file extension must be lowercase '.json'; runtime lookup is case-sensitive on Linux")

        locale: str | None = None
        source_id: str | None = None
        if len(parts) == 1:
            if not path.stem.startswith("_"):
                message = "unscoped root biography is a local/global override; repository contributions must use a SourceUniqueID namespace"
                (errors if strict_layout else warnings)[path].append(message)
        elif len(parts) == 2:
            locale = parts[0]
            message = "unscoped localized biography is a local/global override; repository contributions must use a SourceUniqueID namespace"
            (errors if strict_layout else warnings)[path].append(message)
        elif len(parts) == 3 and parts[1] == "bios":
            source_id = parts[0]
        elif len(parts) == 4 and parts[1] == "locales":
            source_id = parts[0]
            locale = parts[2]
        else:
            errors[path].append(
                "files must use npc_bios/<Npc>.json, npc_bios/<locale>/<Npc>.json, "
                "npc_bios/<SourceUniqueID>/bios/<Npc>.json, or "
                "npc_bios/<SourceUniqueID>/locales/<locale>/<Npc>.json"
            )

        if source_id is not None:
            errors[path].extend(validate_portable_segment(source_id, "SourceUniqueID directory"))
            if "." not in source_id or not SOURCE_ID_PATTERN.fullmatch(source_id):
                errors[path].append(f"invalid SourceUniqueID directory: {source_id!r}")

        if locale is not None:
            errors[path].extend(validate_portable_segment(locale, "locale directory"))
            if not LOCALE_PATTERN.fullmatch(locale):
                errors[path].append(f"invalid locale directory: {locale!r}")
            else:
                expected_locale = canonical_locale(locale)
                if locale != expected_locale:
                    errors[path].append(f"locale directory must use canonical casing {expected_locale!r}, got {locale!r}")

        stem = path.stem
        is_root_template = len(relative.parts) == 1 and stem.startswith("_")
        if not is_root_template:
            errors[path].extend(validate_portable_segment(stem, "NPC internal-name filename"))
            if stem.startswith("_"):
                errors[path].append("only root-level template files may start with '_'")

        directory_names = names_by_directory.setdefault(path.parent, {})
        folded = path.name.casefold()
        previous = directory_names.get(folded)
        if previous is not None:
            errors[path].append(f"case-insensitive filename collision with {previous.name!r}")
            errors[previous].append(f"case-insensitive filename collision with {path.name!r}")
        else:
            directory_names[folded] = path

    directories_by_parent: dict[Path, dict[str, Path]] = {}
    for directory in sorted(
        (path for path in root.rglob("*") if path.is_dir()),
        key=lambda path: path.as_posix().casefold(),
    ):
        siblings = directories_by_parent.setdefault(directory.parent, {})
        folded = directory.name.casefold()
        previous = siblings.get(folded)
        if previous is not None and previous != directory:
            for file_path in files:
                if directory in file_path.parents or previous in file_path.parents:
                    errors[file_path].append(
                        f"case-insensitive directory collision between {previous.name!r} and {directory.name!r}"
                    )
        else:
            siblings[folded] = directory

    return errors, warnings


def evaluate_csharp_integer(expression: str) -> int:
    compact = expression.replace("_", "").replace(" ", "")
    if not re.fullmatch(r"[0-9*+\-/()]+", compact):
        raise ValueError(f"unsupported C# integer expression: {expression!r}")
    return int(eval(compact, {"__builtins__": {}}, {}))  # noqa: S307 - input is regex-limited to arithmetic


def runtime_policy_errors(root: Path) -> list[str]:
    """When run from a source checkout, pin Python limits to the C# runtime constants."""
    project_root = root.parent
    loader_path = project_root / "Dialogue" / "Content" / "CommunityBioLoader.cs"
    portrait_path = project_root / "Dialogue" / "Content" / "PortraitMarkerRules.cs"
    if not loader_path.is_file() or not portrait_path.is_file():
        return []

    loader_source = loader_path.read_text(encoding="utf-8-sig")
    portrait_source = portrait_path.read_text(encoding="utf-8-sig")
    constants: dict[str, int] = {}
    for name, expression in re.findall(
        r"(?:public|internal|private)\s+const\s+(?:int|long)\s+(\w+)\s*=\s*([^;]+);",
        loader_source + "\n" + portrait_source,
    ):
        try:
            constants[name] = evaluate_csharp_integer(expression)
        except ValueError:
            continue

    expected = {
        "MaxFileBytes": MAX_FILE_BYTES,
        "MaxBiographyLength": MAX_BIOGRAPHY_LENGTH,
        "MaxLongTextLength": MAX_LONG_TEXT_LENGTH,
        "MaxDescriptionLength": MAX_DESCRIPTION_LENGTH,
        "MaxShortTextLength": MAX_SHORT_TEXT_LENGTH,
        "MaxIdentifierLength": MAX_IDENTIFIER_LENGTH,
        "MaxCollectionEntries": MAX_COLLECTION_ENTRIES,
        "MaxSupportedFrameIndex": MAX_PORTRAIT_FRAME,
    }
    errors: list[str] = []
    for name, python_value in expected.items():
        csharp_value = constants.get(name)
        if csharp_value is None:
            errors.append(f"could not find C# runtime policy constant {name}")
        elif csharp_value != python_value:
            errors.append(f"policy mismatch for {name}: Python={python_value}, C#={csharp_value}")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "root",
        nargs="?",
        default="LivingNPCs/npc_bios",
        help="directory containing community biography JSON files",
    )
    parser.add_argument(
        "--repository",
        action="store_true",
        help="enforce repository contribution layout (reject unscoped local override files)",
    )
    args = parser.parse_args()

    root = Path(args.root)
    if not root.is_dir():
        print(f"ERROR: biography directory does not exist: {root}", file=sys.stderr)
        return 2

    files = sorted(
        (
            path
            for path in root.rglob("*")
            if path.is_file() and path.suffix.casefold() == ".json"
        ),
        key=lambda path: path.as_posix().casefold(),
    )
    if not files:
        print(f"ERROR: no JSON files found under {root}", file=sys.stderr)
        return 2

    error_count = 0
    warning_count = 0
    for error in runtime_policy_errors(root):
        error_count += 1
        print(f"ERROR: runtime policy: {error}", file=sys.stderr)
    file_layout_errors, file_layout_warnings = layout_diagnostics(
        root,
        files,
        strict_layout=args.repository,
    )
    for path in files:
        errors, warnings = validate_file(path)
        errors = file_layout_errors[path] + errors
        warnings = file_layout_warnings[path] + warnings
        display = path.as_posix()
        for warning in warnings:
            warning_count += 1
            print(f"WARNING: {display}: {warning}")
        for error in errors:
            error_count += 1
            print(f"ERROR: {display}: {error}", file=sys.stderr)

    if error_count:
        print(f"Community biography validation failed: {error_count} error(s), {warning_count} warning(s).", file=sys.stderr)
        return 1

    print(f"Validated {len(files)} community biography file(s): 0 errors, {warning_count} warning(s).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
