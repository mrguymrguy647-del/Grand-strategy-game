#!/usr/bin/env python3
"""Bakes the terrain texture for the world map: Assets/StreamingAssets/Data/Map/terrain.jpg

The land is Natural Earth's "Shaded Relief" raster (public domain), reprojected to the game's
Miller projection. Oceans and lakes get a stylised depth gradient and rivers are drawn in.
The game tints the land with country colours on top of this.

The relief image ships on PyPI inside the basemap-data package, so no extra download site is
needed. Run generate_map.py first (it writes provinces.json, which sets the map size).

Usage:
  pip install -r Tools/mapgen/requirements.txt
  python3 Tools/mapgen/generate_terrain.py [--scale 1.5]
"""

import argparse
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(__file__))
import generate_map as gm  # noqa: E402  (shared projection and Natural Earth helpers)

Image.MAX_IMAGE_PIXELS = None

DEEP = np.array([22, 44, 70], dtype=np.float32)
MID = np.array([34, 72, 104], dtype=np.float32)
SHALLOW = np.array([78, 128, 156], dtype=np.float32)
RIVER = np.array([66, 112, 150], dtype=np.float32)

MAX_RIVER_SCALERANK = 6


def relief_path():
    try:
        import mpl_toolkits.basemap_data as bd  # provided by the basemap-data package
    except ImportError:
        sys.exit("Missing basemap-data. Run: pip install -r Tools/mapgen/requirements.txt")
    folder = list(bd.__path__)[0]  # namespace package: no __file__
    path = os.path.join(folder, "shadedrelief.jpg")
    if not os.path.exists(path):
        sys.exit(f"shadedrelief.jpg not found in {folder}")
    return path


def inverse_miller(y):
    return math.degrees(2.5 * math.atan(math.exp(0.8 * y)) - 0.625 * math.pi)


def reproject_relief(proj, width, height):
    """Equirectangular source -> Miller target, resampled with bilinear rows."""
    src = Image.open(relief_path()).convert("RGB")
    sw, sh = src.size
    # Longitude is linear in both projections, so columns can be resized directly.
    src = np.asarray(src.resize((width, sh), Image.LANCZOS), dtype=np.float32)

    out = np.empty((height, width, 3), dtype=np.float32)
    for y in range(height):
        m = proj.y_top - (y + 0.5) / height * (proj.y_top - proj.y_bottom)
        lat = inverse_miller(m)
        sy = (90.0 - lat) / 180.0 * sh - 0.5
        y0 = int(math.floor(sy))
        t = sy - y0
        y0 = max(0, min(sh - 1, y0))
        y1 = max(0, min(sh - 1, y0 + 1))
        out[y] = src[y0] * (1 - t) + src[y1] * t
    return out


def land_mask(admin1, lakes, proj, width, height):
    sx, sy = width / proj.width, height / proj.height
    img = Image.new("L", (width, height), 0)
    draw = ImageDraw.Draw(img)
    for f in admin1:
        for poly in gm.polygons_of(f["geometry"]):
            rings = [[(x * sx, y * sy) for x, y in (proj.to_image(lon, lat) for lon, lat in ring)] for ring in poly]
            if len(rings[0]) >= 3:
                draw.polygon(rings[0], fill=255)
    land = np.array(img) > 0
    land &= ~gm.lake_mask(lakes, proj, width, height)
    return land


def blur(mask, radius):
    img = Image.fromarray((mask * 255).astype(np.uint8))
    return np.asarray(img.filter(ImageFilter.GaussianBlur(radius)), dtype=np.float32) / 255.0


def river_alpha(rivers, proj, width, height):
    """Anti-aliased river lines: drawn at 2x and filtered down."""
    ss = 2
    sx, sy = width * ss / proj.width, height * ss / proj.height
    img = Image.new("L", (width * ss, height * ss), 0)
    draw = ImageDraw.Draw(img)
    scale = width / 4096.0
    for f in rivers:
        p = f["properties"]
        rank = p.get("scalerank")
        if rank is None or rank > MAX_RIVER_SCALERANK:
            continue
        w = max(1, int(round((2.2 if rank <= 2 else 1.6 if rank <= 4 else 1.1) * scale * ss)))
        geom = f["geometry"]
        if geom is None:
            continue
        lines = geom["coordinates"] if geom["type"] == "MultiLineString" else [geom["coordinates"]]
        for line in lines:
            pts = [(x * sx, y * sy) for x, y in (proj.to_image(lon, lat) for lon, lat in line)]
            if len(pts) >= 2:
                draw.line(pts, fill=255, width=w, joint="curve")
    img = img.resize((width, height), Image.BOX)
    return np.asarray(img, dtype=np.float32) / 255.0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--scale", type=float, default=1.5, help="terrain size relative to provinces.png")
    parser.add_argument("--quality", type=int, default=88, help="JPEG quality")
    args = parser.parse_args()

    with open(os.path.join(gm.OUT_DIR, "provinces.json"), encoding="utf-8") as f:
        header = json.loads(f.read().split('"provinces"')[0].rstrip().rstrip(",") + "}")
    proj = gm.Projection(header["width"])
    # Multiples of 4 so the game can block-compress the texture at load time.
    width = int(round(proj.width * args.scale / 4)) * 4
    height = int(round(proj.height * args.scale / 4)) * 4
    print(f"Terrain {width}x{height}")

    paths = gm.download_inputs()
    admin1 = gm.load_features(paths["admin1"])
    lakes = gm.lake_features(gm.load_features(paths["lakes"]))
    rivers = gm.load_features(paths["rivers"])

    relief = reproject_relief(proj, width, height)
    land = land_mask(admin1, lakes, proj, width, height)

    # Water: deep ocean far from land, lighter shallows along the coasts.
    near = blur(land, 3 * args.scale)
    far = blur(land, 28 * args.scale)
    t_mid = np.clip(far * 2.2, 0, 1)[..., None]
    t_shallow = np.clip(near * 1.6, 0, 1)[..., None] ** 1.3
    water = DEEP + (MID - DEEP) * t_mid
    water = water + (SHALLOW - water) * t_shallow

    # Land: the relief, very slightly deepened so it reads well under country colours.
    lum = relief.mean(axis=2, keepdims=True)
    land_rgb = np.clip(lum + (relief - lum) * 1.1, 0, 255) * 0.97

    rivers_a = river_alpha(rivers, proj, width, height)[..., None] * land[..., None]
    land_rgb = land_rgb + (RIVER - land_rgb) * rivers_a * 0.75

    # Soft anti-aliased coastline between the two.
    edge = blur(land, 0.6 * args.scale)[..., None]
    rgb = water + (land_rgb - water) * edge

    out = os.path.join(gm.OUT_DIR, "terrain.jpg")
    Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), "RGB").save(out, quality=args.quality, optimize=True, progressive=False)
    print(f"Wrote {out} ({os.path.getsize(out) / 1e6:.1f} MB)")


if __name__ == "__main__":
    main()
