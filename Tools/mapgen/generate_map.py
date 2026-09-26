#!/usr/bin/env python3
"""Builds the world map data for the game from Natural Earth (public domain).

Outputs, written to Assets/StreamingAssets/Data/Map/:
  provinces.png   Province ID map. Every province has a unique RGB colour, ocean is black.
  provinces.json  Province definitions (id, colour, name, owner, neighbours, ...).
  countries.json  Country definitions (tag, name, colour, capital, population, GDP, ...).

Coordinates written to the JSON files use texture space: x grows to the right,
y grows upwards, (0, 0) is the bottom-left pixel. That matches Unity's Texture2D.

Usage:
  pip install -r Tools/mapgen/requirements.txt
  python3 Tools/mapgen/generate_map.py [--width 4096]
"""

import argparse
import colorsys
import json
import math
import os
import random
import statistics
import sys
import urllib.request
from collections import defaultdict

import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CACHE_DIR = os.path.join(os.path.dirname(__file__), ".cache")
OUT_DIR = os.path.join(ROOT, "Assets", "StreamingAssets", "Data", "Map")

NE_BASE = "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/"
NE_FILES = {
    "admin0": "ne_10m_admin_0_countries.geojson",
    "admin1": "ne_10m_admin_1_states_provinces.geojson",
    "places": "ne_10m_populated_places_simple.geojson",
}

# Latitude range of the map (degrees). Antarctica is left out.
MIN_LAT = -56.0
MAX_LAT = 84.0

# Countries with many small subdivisions get them merged into their regions.
MERGE_MIN_UNITS = 40
MERGE_MAX_MEDIAN_AREA_KM2 = 15000

# Provinces smaller than this (in pixels) are merged into a neighbour.
MIN_PROVINCE_PIXELS = 30
# Countries smaller than this (in pixels) are too small to play and are left out.
MIN_COUNTRY_PIXELS = 4

# Uninhabited or indeterminate areas that don't belong to any country.
UNOWNED = {"ATA", "BRI", "SPI", "BRT", "CNM", "KAS", "PGA", "BJN", "SER", "SCR"}
# Areas Natural Earth assigns to another sovereign that the game treats as a country.
INDEPENDENT = {"PSX"}

# Countries with several capitals in Natural Earth: the seat of government to use.
CAPITAL_OVERRIDES = {"ZAF": "Pretoria"}

# Hand-picked colours for well known countries; everyone else gets a generated colour.
FIXED_COLORS = {
    "USA": "#3a5ba0", "CAN": "#b5484f", "MEX": "#4f8a5b", "BRA": "#4a9a4f",
    "ARG": "#7fb3d5", "GBR": "#b83a3a", "FRA": "#3f6fc0", "DEU": "#6b6b6b",
    "ITA": "#4e9a6a", "ESP": "#d6a33c", "RUS": "#3f7a4a", "CHN": "#c4513c",
    "IND": "#e08f3c", "JPN": "#d9d0c1", "KOR": "#6f8fc9", "PRK": "#8c3b3b",
    "AUS": "#3e8f86", "TUR": "#a8563c", "IRN": "#4c8a4c", "SAU": "#5f9e5a",
    "EGY": "#c7b06a", "ISR": "#5f7fb5", "UKR": "#e2c24a", "POL": "#c75b6b",
    "PAK": "#2f6e4a", "IDN": "#b0453a", "NGA": "#5ca35c", "ZAF": "#c98e3f",
}


def download_inputs():
    os.makedirs(CACHE_DIR, exist_ok=True)
    paths = {}
    for key, name in NE_FILES.items():
        path = os.path.join(CACHE_DIR, name)
        if not os.path.exists(path):
            print(f"Downloading {name} ...")
            urllib.request.urlretrieve(NE_BASE + name, path)
        paths[key] = path
    return paths


def load_features(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)["features"]


# --------------------------------------------------------------------------- projection

def miller_y(lat_deg):
    lat = math.radians(max(MIN_LAT, min(MAX_LAT, lat_deg)))
    return 1.25 * math.log(math.tan(math.pi / 4 + 0.4 * lat))


class Projection:
    def __init__(self, width):
        self.width = width
        self.y_top = miller_y(MAX_LAT)
        self.y_bottom = miller_y(MIN_LAT)
        self.height = int(round(width * (self.y_top - self.y_bottom) / (2 * math.pi)))

    def to_image(self, lon, lat):
        """Longitude/latitude to image pixel coordinates (y grows downwards)."""
        x = (lon + 180.0) / 360.0 * self.width
        y = (self.y_top - miller_y(lat)) / (self.y_top - self.y_bottom) * self.height
        return x, y


# --------------------------------------------------------------------------- ownership

def build_owner_lookup(admin0):
    rows = {f["properties"]["ADM0_A3"]: f["properties"] for f in admin0}
    sov_main = {}
    for r in rows.values():
        if r["ADMIN"] == r["SOVEREIGNT"]:
            sov_main[r["SOV_A3"]] = r["ADM0_A3"]

    def owner_of(adm0_a3):
        if adm0_a3 in UNOWNED:
            return None
        if adm0_a3 in INDEPENDENT:
            return adm0_a3
        row = rows.get(adm0_a3)
        if row is None:
            return None
        return sov_main.get(row["SOV_A3"])

    return rows, owner_of


# --------------------------------------------------------------------------- units

def geodesic_area_km2(geometry):
    """Approximate area of a lon/lat polygon on a sphere."""
    r = 6371.0
    total = 0.0
    for poly in polygons_of(geometry):
        for i, ring in enumerate(poly):
            a = 0.0
            for (lon1, lat1), (lon2, lat2) in zip(ring, ring[1:] + ring[:1]):
                a += math.radians(lon2 - lon1) * (2 + math.sin(math.radians(lat1)) + math.sin(math.radians(lat2)))
            a = abs(a) * r * r / 2
            total += a if i == 0 else -a
    return total


def build_units(admin1, owner_of):
    """Groups admin-1 features into province units, merging tiny subdivisions by region."""
    by_country = defaultdict(list)
    for f in admin1:
        by_country[f["properties"]["adm0_a3"]].append(f)

    merge_by_region = set()
    for adm0, feats in by_country.items():
        if len(feats) <= MERGE_MIN_UNITS:
            continue
        areas = [geodesic_area_km2(f["geometry"]) for f in feats]
        regions = {f["properties"].get("region") for f in feats} - {None, ""}
        if statistics.median(areas) < MERGE_MAX_MEDIAN_AREA_KM2 and len(regions) >= 3:
            merge_by_region.add(adm0)

    units = {}          # key -> {name, owner, adm0}
    feature_keys = []   # (feature, key)
    for f in admin1:
        p = f["properties"]
        adm0 = p["adm0_a3"]
        region = p.get("region")
        if adm0 in merge_by_region and region:
            key = f"{adm0}:{region}"
            name = region
        else:
            key = p["adm1_code"]
            name = p.get("name_en") or p.get("name") or p.get("gn_name") or key
            if name == p.get("admin") and p.get("name"):
                name = p["name"]  # a few name_en entries repeat the country name
        if key not in units:
            units[key] = {"name": name, "owner": owner_of(adm0), "adm0": adm0}
        feature_keys.append((f, key))
    return units, feature_keys, merge_by_region


# --------------------------------------------------------------------------- rasterising

def polygons_of(geometry):
    if geometry is None:
        return []
    if geometry["type"] == "Polygon":
        return [geometry["coordinates"]]
    if geometry["type"] == "MultiPolygon":
        return geometry["coordinates"]
    return []


def rasterize(feature_keys, key_to_id, proj):
    img = Image.new("I", (proj.width, proj.height), 0)
    draw = ImageDraw.Draw(img)

    def ring_area(ring):
        a = 0.0
        for (x1, y1), (x2, y2) in zip(ring, ring[1:] + ring[:1]):
            a += x1 * y2 - x2 * y1
        return abs(a) / 2

    shapes = []
    for f, key in feature_keys:
        for poly in polygons_of(f["geometry"]):
            rings = [[proj.to_image(lon, lat) for lon, lat in ring] for ring in poly]
            if not rings or len(rings[0]) < 3:
                continue
            shapes.append((ring_area(rings[0]), key_to_id[key], rings))

    # Largest first so enclaves drawn later end up on top of the shape around them.
    shapes.sort(key=lambda s: -s[0])
    for _, pid, rings in shapes:
        draw.polygon(rings[0], fill=pid)
        for hole in rings[1:]:
            if len(hole) >= 3:
                draw.polygon(hole, fill=0)
    return np.array(img, dtype=np.int32)


def adjacency(arr):
    """Returns {(a, b): shared_edge_pixels} for a < b, including ocean (0)."""
    pairs = []
    for a, b in ((arr[:, :-1], arr[:, 1:]), (arr[:-1, :], arr[1:, :])):
        mask = a != b
        lo = np.minimum(a[mask], b[mask]).astype(np.int64)
        hi = np.maximum(a[mask], b[mask]).astype(np.int64)
        pairs.append(lo * 1_000_000 + hi)
    codes, counts = np.unique(np.concatenate(pairs), return_counts=True)
    return {(int(c // 1_000_000), int(c % 1_000_000)): int(n) for c, n in zip(codes, counts)}


def centroids(arr, n_ids):
    h, w = arr.shape
    ys, xs = np.indices(arr.shape)
    flat = arr.ravel()
    count = np.bincount(flat, minlength=n_ids)
    sx = np.bincount(flat, weights=xs.ravel(), minlength=n_ids)
    sy = np.bincount(flat, weights=ys.ravel(), minlength=n_ids)
    with np.errstate(invalid="ignore", divide="ignore"):
        return count, sx / count, sy / count


# --------------------------------------------------------------------------- merging

def merge_small(arr, units, id_to_key):
    """Merges provinces below MIN_PROVINCE_PIXELS into a same-owner neighbour."""
    n = len(id_to_key) + 1
    count, cx, cy = centroids(arr, n)
    adj = defaultdict(dict)
    for (a, b), shared in adjacency(arr).items():
        if a and b:
            adj[a][b] = shared
            adj[b][a] = shared

    parent = list(range(n))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    owner = [None] + [units[id_to_key[i]]["owner"] for i in range(1, n)]
    size = [int(c) for c in count]
    alive = {i for i in range(1, n) if size[i] > 0}

    def merge(src, dst):
        parent[src] = dst
        total = size[src] + size[dst]
        cx[dst] = (cx[dst] * size[dst] + cx[src] * size[src]) / total
        cy[dst] = (cy[dst] * size[dst] + cy[src] * size[src]) / total
        size[dst] = total
        size[src] = 0
        for nb, shared in adj.pop(src, {}).items():
            adj[nb].pop(src, None)
            if nb != dst:
                adj[dst][nb] = adj[dst].get(nb, 0) + shared
                adj[nb][dst] = adj[nb].get(dst, 0) + shared
        adj[dst].pop(dst, None)
        alive.discard(src)

    dropped = 0
    changed = True
    while changed:
        changed = False
        for pid in sorted(alive, key=lambda i: size[i]):
            if pid not in alive or size[pid] >= MIN_PROVINCE_PIXELS:
                continue
            same = [nb for nb in adj[pid] if owner[nb] == owner[pid] and nb in alive]
            if same:
                target = max(same, key=lambda nb: adj[pid][nb])
            else:
                others = [i for i in alive if i != pid and owner[i] == owner[pid]]
                if not others:
                    if owner[pid] is None:
                        parent[pid] = 0
                        alive.discard(pid)
                        dropped += 1
                    continue  # a country's only province stays, however small
                target = min(others, key=lambda i: (cx[i] - cx[pid]) ** 2 + (cy[i] - cy[pid]) ** 2)
            merge(pid, target)
            changed = True

    lut = np.array([find(i) for i in range(n)], dtype=np.int32)
    return lut[arr], dropped


# --------------------------------------------------------------------------- colours

def hex_color(rgb):
    return "#%02x%02x%02x" % rgb


def province_colors(n, seed=1337):
    rng = random.Random(seed)
    used = {(0, 0, 0)}
    colors = []
    while len(colors) < n:
        c = (rng.randrange(256), rng.randrange(256), rng.randrange(256))
        if c not in used:
            used.add(c)
            colors.append(c)
    return colors


def country_colors(tags, neighbours, sizes):
    palette = []
    for i in range(36):
        h = (i * 0.61803398875) % 1.0
        for s, v in ((0.45, 0.72), (0.55, 0.58), (0.35, 0.82)):
            r, g, b = colorsys.hsv_to_rgb(h, s, v)
            palette.append((int(r * 255), int(g * 255), int(b * 255)))

    def dist(a, b):
        return sum((x - y) ** 2 for x, y in zip(a, b))

    result = {}
    for tag in tags:
        if tag in FIXED_COLORS:
            h = FIXED_COLORS[tag].lstrip("#")
            result[tag] = tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))
    rng = random.Random(42)
    for tag in sorted(tags, key=lambda t: -sizes[t]):
        if tag in result:
            continue
        near = [result[n] for n in neighbours[tag] if n in result]
        candidates = rng.sample(palette, 24)
        if near:
            best = max(candidates, key=lambda c: min(dist(c, o) for o in near))
        else:
            best = candidates[0]
        result[tag] = best
    return {t: hex_color(c) for t, c in result.items()}


# --------------------------------------------------------------------------- main

def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--width", type=int, default=4096)
    args = parser.parse_args()

    paths = download_inputs()
    admin0 = load_features(paths["admin0"])
    admin1 = load_features(paths["admin1"])
    places = load_features(paths["places"])
    proj = Projection(args.width)
    print(f"Map size {proj.width}x{proj.height}")

    a0_rows, owner_of = build_owner_lookup(admin0)
    units, feature_keys, merged = build_units(admin1, owner_of)
    print(f"{len(admin1)} admin-1 features -> {len(units)} units "
          f"(merged by region: {', '.join(sorted(merged))})")

    id_to_key = {}
    key_to_id = {}
    for i, key in enumerate(units, start=1):
        id_to_key[i] = key
        key_to_id[key] = i

    arr = rasterize(feature_keys, key_to_id, proj)
    arr, dropped = merge_small(arr, units, id_to_key)
    print(f"Dropped {dropped} tiny unowned areas")

    # Drop countries that are too small to play.
    count = np.bincount(arr.ravel(), minlength=len(id_to_key) + 1)
    country_pixels = defaultdict(int)
    for pid in np.nonzero(count)[0]:
        if pid:
            country_pixels[units[id_to_key[pid]]["owner"]] += int(count[pid])
    too_small = {t for t, px in country_pixels.items() if t and px < MIN_COUNTRY_PIXELS}
    if too_small:
        kill = np.zeros(len(id_to_key) + 1, dtype=bool)
        for pid, key in id_to_key.items():
            if units[key]["owner"] in too_small:
                kill[pid] = True
        arr[kill[arr]] = 0

    # Re-number the surviving provinces 1..N, grouped by owner.
    present = [int(p) for p in np.unique(arr) if p]
    present.sort(key=lambda p: (units[id_to_key[p]]["owner"] or "~", id_to_key[p]))
    lut = np.zeros(len(id_to_key) + 1, dtype=np.int32)
    for new_id, old_id in enumerate(present, start=1):
        lut[old_id] = new_id
    arr = lut[arr]
    n = len(present) + 1
    old_of = {new_id: old_id for new_id, old_id in enumerate(present, start=1)}
    info = {pid: units[id_to_key[old_of[pid]]] for pid in range(1, n)}

    # Geometry: size, bbox, label point, neighbours, coast.
    h = proj.height
    flat = arr.ravel()
    order = np.argsort(flat, kind="stable")
    count = np.bincount(flat, minlength=n)
    starts = np.concatenate([[0], np.cumsum(count)])
    adj = adjacency(arr)
    neighbours = defaultdict(set)
    coastal = set()
    for (a, b), _ in adj.items():
        if a == 0:
            coastal.add(b)
        else:
            neighbours[a].add(b)
            neighbours[b].add(a)

    geo = {}
    for pid in range(1, n):
        idx = order[starts[pid]:starts[pid + 1]]
        ys, xs = np.divmod(idx, proj.width)
        mx, my = xs.mean(), ys.mean()
        k = int(np.argmin((xs - mx) ** 2 + (ys - my) ** 2))
        geo[pid] = {
            "pixels": int(count[pid]),
            # Flip y so the JSON uses texture space (y up).
            "center": [int(xs[k]), int(h - 1 - ys[k])],
            "bbox": [int(xs.min()), int(h - 1 - ys.max()), int(xs.max()), int(h - 1 - ys.min())],
        }

    # Population / GDP, distributed over provinces by city population and area.
    country_pop = defaultdict(float)
    country_gdp = defaultdict(float)
    for adm0, row in a0_rows.items():
        owner = owner_of(adm0)
        if owner:
            country_pop[owner] += row.get("POP_EST") or 0
            country_gdp[owner] += row.get("GDP_MD") or 0

    def pixel_province(lon, lat, owner=None, radius=12):
        x, y = proj.to_image(lon, lat)
        x, y = int(x), int(y)
        best = None
        for r in range(radius + 1):
            for dy in range(-r, r + 1):
                for dx in range(-r, r + 1):
                    if max(abs(dx), abs(dy)) != r:
                        continue
                    px, py = x + dx, y + dy
                    if 0 <= px < proj.width and 0 <= py < h:
                        pid = int(arr[py, px])
                        if pid and (owner is None or info[pid]["owner"] == owner):
                            best = pid
                            break
                if best:
                    return best
        return None

    city_pop = defaultdict(float)
    for f in places:
        p = f["properties"]
        pid = pixel_province(p["longitude"], p["latitude"], radius=2)
        if pid:
            city_pop[pid] += max(p.get("pop_max") or 0, 0)

    by_owner = defaultdict(list)
    for pid in range(1, n):
        by_owner[info[pid]["owner"]].append(pid)

    province_pop = {}
    province_gdp = {}
    for owner, pids in by_owner.items():
        total_city = sum(city_pop[p] for p in pids)
        total_px = sum(geo[p]["pixels"] for p in pids)
        for p in pids:
            share_px = geo[p]["pixels"] / total_px
            share_city = city_pop[p] / total_city if total_city else share_px
            w = 0.7 * share_city + 0.3 * share_px
            province_pop[p] = int(round(country_pop.get(owner, 0) * w))
            province_gdp[p] = round(country_gdp.get(owner, 0) * w, 1)

    # Capitals.
    capital_of = {}
    capital_name = {}
    candidates = defaultdict(list)
    for f in places:
        p = f["properties"]
        if p["featurecla"] in ("Admin-0 capital", "Admin-0 capital alt"):
            owner = owner_of(p["adm0_a3"]) if p["adm0_a3"] in a0_rows else None
            if owner:
                rank = (CAPITAL_OVERRIDES.get(owner) == p["name"], p["adm0_a3"] == owner,
                        p["featurecla"] == "Admin-0 capital", p.get("pop_max") or 0)
                candidates[owner].append((rank, p))
    for owner, pids in by_owner.items():
        if owner is None:
            continue
        for _, p in sorted(candidates.get(owner, []), key=lambda c: c[0], reverse=True):
            pid = pixel_province(p["longitude"], p["latitude"], owner=owner, radius=15)
            if pid:
                capital_of[owner] = pid
                capital_name[owner] = " ".join(p["name"].split())
                break
        if owner not in capital_of:
            capital_of[owner] = max(pids, key=lambda q: province_pop[q])
            capital_name[owner] = info[capital_of[owner]]["name"]

    # Colours.
    colors = province_colors(n - 1)
    owners = sorted(o for o in by_owner if o)
    country_neigh = defaultdict(set)
    for a, nbs in neighbours.items():
        for b in nbs:
            oa, ob = info[a]["owner"], info[b]["owner"]
            if oa and ob and oa != ob:
                country_neigh[oa].add(ob)
    # Countries close to each other across the sea should also get different colours.
    for a in by_owner:
        for b in by_owner:
            if a and b and a < b:
                ax, ay = geo[capital_of[a]]["center"]
                bx, by_ = geo[capital_of[b]]["center"]
                if (ax - bx) ** 2 + (ay - by_) ** 2 < 220 ** 2:
                    country_neigh[a].add(b)
                    country_neigh[b].add(a)
    sizes = {o: sum(geo[p]["pixels"] for p in by_owner[o]) for o in owners}
    ccolors = country_colors(owners, country_neigh, sizes)

    # Write outputs.
    os.makedirs(OUT_DIR, exist_ok=True)
    rgb = np.zeros((n, 3), dtype=np.uint8)
    for pid in range(1, n):
        rgb[pid] = colors[pid - 1]
    Image.fromarray(rgb[arr], "RGB").save(os.path.join(OUT_DIR, "provinces.png"), optimize=True)

    provinces = []
    for pid in range(1, n):
        g = geo[pid]
        provinces.append({
            "id": pid,
            "color": hex_color(colors[pid - 1]),
            "name": info[pid]["name"],
            "owner": info[pid]["owner"] or "",
            "population": province_pop[pid],
            "gdpMillions": province_gdp[pid],
            "pixels": g["pixels"],
            "center": g["center"],
            "bbox": g["bbox"],
            "coastal": pid in coastal,
            "neighbors": sorted(neighbours[pid]),
        })

    countries = []
    for tag in owners:
        row = a0_rows[tag]
        countries.append({
            "tag": tag,
            "name": row["NAME"],
            "formalName": row.get("FORMAL_EN") or row.get("NAME_LONG") or row["NAME"],
            "isoA2": row.get("ISO_A2_EH") or row.get("ISO_A2") or "",
            "color": ccolors[tag],
            "capitalProvince": capital_of[tag],
            "capitalName": capital_name[tag],
            "population": int(country_pop[tag]),
            "gdpMillions": round(country_gdp[tag], 1),
            "continent": row.get("CONTINENT") or "",
            "subregion": row.get("SUBREGION") or "",
            "incomeGroup": row.get("INCOME_GRP") or "",
        })

    def write_json(name, key, items, meta=None):
        path = os.path.join(OUT_DIR, name)
        with open(path, "w", encoding="utf-8") as f:
            f.write("{\n")
            for k, v in (meta or {}).items():
                f.write(f'  "{k}": {json.dumps(v)},\n')
            f.write(f'  "{key}": [\n')
            f.write(",\n".join("    " + json.dumps(i, ensure_ascii=False) for i in items))
            f.write("\n  ]\n}\n")

    meta = {
        "source": "Natural Earth 1:10m admin-1 (public domain), generated by Tools/mapgen/generate_map.py",
        "width": proj.width,
        "height": proj.height,
        "projection": "miller",
        "minLatitude": MIN_LAT,
        "maxLatitude": MAX_LAT,
    }
    write_json("provinces.json", "provinces", provinces, meta)
    write_json("countries.json", "countries", countries, {"source": meta["source"]})

    print(f"Wrote {len(provinces)} provinces, {len(countries)} countries to {OUT_DIR}")
    if too_small:
        print("Left out (too small for the map): " + ", ".join(sorted(too_small)))


if __name__ == "__main__":
    sys.exit(main())
