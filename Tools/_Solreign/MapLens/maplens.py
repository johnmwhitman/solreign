#!/usr/bin/env python3
"""MapLens — fail-closed wrapper around Content.MapRenderer.

Earned on the 2026-08-02 A9 render session:
  * Renders run ONE MAP AT A TIME — parallel renders OOM'd Leviathan three
    times on this machine.
  * The nofile limit is raised in-process before any render (the ulimit fix
    baked in, not remembered).
  * Crops anchor to tile bounds derived from decoded NON-SPACE tiles and are
    cross-checked against the rendered PNG's actual dimensions. A mismatch
    fails closed: a crop from a mis-anchored render is worse than no crop.
    (Content.MapRenderer sizes the image over GetAllTiles min/max and anchors
    painting at grid.LocalAABB, then flips vertically; chunk-index math at
    16-tile granularity shifted every crop.)

Subcommands:
  render  --root R --out DIR map.yml [map2.yml ...]
  crop    --root R --map map.yml --parent UID --render out.png \
          --at X,Y --radius N --out crop.png
"""
from __future__ import annotations

import argparse
import base64
from dataclasses import dataclass
import importlib.util
from pathlib import Path
import resource
import struct
import os
import subprocess
import sys

TILE_PX = 32  # EyeManager.PixelsPerMeter == TilePainter.TileImageSize
MARKER_COLOR = (255, 0, 255, 255)
CHUNK_SIZE = 16
TILE_BYTES = 7

_MAP_PATCH_PATH = Path(__file__).resolve().parents[1] / "MapPatch" / "map_patch.py"
_SPEC = importlib.util.spec_from_file_location("solreign_map_patch_for_lens", _MAP_PATCH_PATH)
assert _SPEC is not None and _SPEC.loader is not None
_MAP_PATCH = importlib.util.module_from_spec(_SPEC)
sys.modules[_SPEC.name] = _MAP_PATCH  # keep introspection working under 3.14
_SPEC.loader.exec_module(_MAP_PATCH)


class LensError(Exception):
    """A validation error that must fail closed without writing images."""


@dataclass(frozen=True)
class TileBounds:
    min_x: int
    min_y: int
    max_x: int
    max_y: int


def grid_tile_bounds(text: str, parent: int) -> TileBounds:
    """Min/max over decoded NON-SPACE tiles of one grid — never chunk indices."""
    records = _MAP_PATCH.split_entity_records(text)
    grids = [
        record
        for record in records
        if record["uid"] == parent and "    - type: MapGrid\n" in record["text"]
    ]
    if len(grids) != 1:
        raise LensError(f"parent uid {parent} does not identify exactly one MapGrid")
    tilemap = {
        int(match.group("id")): match.group("name")
        for match in _MAP_PATCH.TILEMAP_RE.finditer(text)
    }
    if not tilemap:
        raise LensError("map has no readable tilemap")

    min_x = min_y = None
    max_x = max_y = None
    for match in _MAP_PATCH.CHUNK_RE.finditer(grids[0]["text"]):
        chunk_x, chunk_y = int(match.group("x")), int(match.group("y"))
        try:
            raw = base64.b64decode(match.group("data"), validate=True)
        except (ValueError, base64.binascii.Error) as error:
            raise LensError(f"invalid base64 tile chunk at {chunk_x},{chunk_y}") from error
        expected_length = CHUNK_SIZE * CHUNK_SIZE * TILE_BYTES
        if len(raw) != expected_length:
            raise LensError(
                f"tile chunk {chunk_x},{chunk_y} has {len(raw)} bytes; expected {expected_length}"
            )
        for local_y in range(CHUNK_SIZE):
            for local_x in range(CHUNK_SIZE):
                offset = (local_y * CHUNK_SIZE + local_x) * TILE_BYTES
                tile_id = struct.unpack_from("<I", raw, offset)[0]
                if tile_id not in tilemap:
                    raise LensError(f"tile id {tile_id} is absent from tilemap")
                if tilemap[tile_id] == "Space":
                    continue
                tile_x = chunk_x * CHUNK_SIZE + local_x
                tile_y = chunk_y * CHUNK_SIZE + local_y
                if min_x is None or tile_x < min_x:
                    min_x = tile_x
                if max_x is None or tile_x > max_x:
                    max_x = tile_x
                if min_y is None or tile_y < min_y:
                    min_y = tile_y
                if max_y is None or tile_y > max_y:
                    max_y = tile_y
    if min_x is None:
        raise LensError(f"grid {parent} has no non-space tiles; nothing to anchor")
    return TileBounds(min_x=min_x, min_y=min_y, max_x=max_x, max_y=max_y)


def expected_image_size(bounds: TileBounds) -> tuple[int, int]:
    return (
        (bounds.max_x - bounds.min_x + 1) * TILE_PX,
        (bounds.max_y - bounds.min_y + 1) * TILE_PX,
    )


def world_to_pixel(
    world: tuple[float, float],
    bounds: TileBounds,
    image_size: tuple[int, int],
) -> tuple[int, int]:
    """Convert world coordinates to pixels in the rendered (flipped) image.

    Fails closed if image_size disagrees with the bounds — a crop from a
    mis-anchored render is worse than no crop.
    """
    expected = expected_image_size(bounds)
    if tuple(image_size) != expected:
        raise LensError(
            f"render size {tuple(image_size)} does not match tile bounds "
            f"{bounds} (expected {expected}); refusing to anchor a crop"
        )
    pixel_x = int(round((world[0] - bounds.min_x) * TILE_PX))
    pixel_y = int(round(image_size[1] - (world[1] - bounds.min_y) * TILE_PX))
    return pixel_x, pixel_y


def crop_with_marker(image, bounds: TileBounds, world: tuple[float, float], radius_tiles: int):
    """Crop a (2r+1)-tile box centred on ``world`` and draw a crosshair there.

    Returns (cropped_image, marker_pixel_within_crop).
    """
    if radius_tiles < 0:
        raise LensError("radius cannot be negative")
    pixel = world_to_pixel(world, bounds, image.size)
    half = radius_tiles * TILE_PX + TILE_PX // 2
    left = max(0, pixel[0] - half)
    top = max(0, pixel[1] - half)
    right = min(image.size[0], pixel[0] + half)
    bottom = min(image.size[1], pixel[1] + half)
    if left >= right or top >= bottom:
        raise LensError(f"crop box around {world} is empty; target outside the render")
    cropped = image.crop((left, top, right, bottom))
    marker = (pixel[0] - left, pixel[1] - top)
    marker_x = min(max(marker[0], 0), cropped.size[0] - 1)
    marker_y = min(max(marker[1], 0), cropped.size[1] - 1)
    for delta in range(-TILE_PX // 2, TILE_PX // 2 + 1):
        x = marker_x + delta
        if 0 <= x < cropped.size[0]:
            cropped.putpixel((x, marker_y), MARKER_COLOR)
        y = marker_y + delta
        if 0 <= y < cropped.size[1]:
            cropped.putpixel((marker_x, y), MARKER_COLOR)
    return cropped, (marker_x, marker_y)


def ensure_nofile(minimum_soft: int) -> None:
    """Raise the soft nofile limit — the renderer opens every RSI on the map."""
    soft, hard = resource.getrlimit(resource.RLIMIT_NOFILE)
    target = min(minimum_soft, hard) if hard != resource.RLIM_INFINITY else minimum_soft
    if soft < target:
        resource.setrlimit(resource.RLIMIT_NOFILE, (target, hard))


RENDER_NOFILE_TARGET = 65536
"""Per-process soft nofile target for renders. 4096 lost leviathan on 2026-08-03 when
other processes' handle pressure ate the headroom; ensure_nofile still caps at the
OS hard limit, so this is a request, not a demand."""


def render_env() -> dict[str, str]:
    """Child env for renders: extend os.environ, kill MSBuild nodeReuse daemons —
    they outlive the render holding file handles and become the next run's pressure."""
    env = dict(os.environ)
    env["MSBUILDDISABLENODEREUSE"] = "1"
    return env


def render_commands(map_paths: list[str], output_dir: Path) -> list[list[str]]:
    """One dotnet invocation PER MAP — parallel renders OOM'd Leviathan 3x."""
    if not map_paths:
        raise LensError("no maps given")
    return [
        [
            "dotnet",
            "run",
            "--project",
            "Content.MapRenderer",
            "--",
            "--files",
            "-o",
            str(output_dir),
            map_path,
        ]
        for map_path in map_paths
    ]


def run_render(args: argparse.Namespace) -> int:
    ensure_nofile(RENDER_NOFILE_TARGET)
    output_dir = Path(args.out)
    output_dir.mkdir(parents=True, exist_ok=True)
    failures = 0
    for command in render_commands(list(args.maps), output_dir):
        print(f"maplens: rendering {command[-1]} (sequential; nofile raised)", flush=True)
        result = subprocess.run(command, cwd=args.root, env=render_env())
        if result.returncode != 0:
            print(f"maplens: RENDER FAILED ({result.returncode}): {command[-1]}", file=sys.stderr)
            failures += 1
    if failures:
        print(f"maplens: {failures} render(s) failed", file=sys.stderr)
        return 1
    return 0


def run_crop(args: argparse.Namespace) -> int:
    from PIL import Image

    map_path = Path(args.root) / args.map_path
    if not map_path.is_file():
        raise LensError(f"map {args.map_path} not found under {args.root}")
    text = map_path.read_text(encoding="utf-8")
    bounds = grid_tile_bounds(text, parent=args.parent)
    near_parts = str(args.at).split(",")
    try:
        world = (float(near_parts[0]), float(near_parts[1]))
    except (ValueError, IndexError) as error:
        raise LensError("--at must be X,Y with two numbers") from error
    with Image.open(args.render) as image:
        cropped, marker = crop_with_marker(
            image.convert("RGBA"), bounds, world, args.radius
        )
    cropped.save(args.out)
    print(
        f"maplens: wrote {args.out} ({cropped.size[0]}x{cropped.size[1]}), "
        f"marker at {marker}"
    )
    return 0


def parse_lens_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    subparsers = parser.add_subparsers(dest="command", required=True)

    render = subparsers.add_parser("render", help="render maps sequentially")
    render.add_argument("--root", type=Path, default=Path.cwd(), help="Game repository root")
    render.add_argument("--out", required=True, help="output directory for PNGs")
    render.add_argument("maps", nargs="+", help="map YAML paths relative to the root")

    crop = subparsers.add_parser("crop", help="crop a rendered PNG at a world coordinate")
    crop.add_argument("--root", type=Path, default=Path.cwd(), help="Game repository root")
    crop.add_argument("--map", dest="map_path", required=True, help="map YAML path relative to the root")
    crop.add_argument("--parent", type=int, required=True, help="MapGrid entity uid")
    crop.add_argument("--render", required=True, help="rendered PNG for this map")
    crop.add_argument("--at", required=True, help="world coordinate X,Y to centre on")
    crop.add_argument("--radius", type=int, default=8, help="crop radius in tiles")
    crop.add_argument("--out", required=True, help="output PNG path")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_lens_args(argv)
    try:
        if args.command == "render":
            return run_render(args)
        return run_crop(args)
    except LensError as error:
        print(f"maplens: ERROR: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
