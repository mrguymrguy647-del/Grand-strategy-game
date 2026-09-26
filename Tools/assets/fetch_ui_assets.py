#!/usr/bin/env python3
"""Downloads and prepares the UI art: flags, icons, fonts and panel textures.

Everything is freely licensed:
  Flags  - flag-icons by Panayiotis Lipiridis (MIT)            https://github.com/lipis/flag-icons
  Icons  - game-icons.net by Lorc and Delapouite (CC BY 3.0)    https://game-icons.net
  Fonts  - Barlow by Jeremy Tribby (SIL Open Font License)      https://github.com/google/fonts
Textures (panel gradients, vignette) are generated here.

Outputs go to Assets/Resources/{Flags,Icons,Fonts,UI/Textures}. Credits are listed in
Assets/Resources/Credits.txt and the README.

Usage:
  pip install cairosvg pillow numpy
  python3 Tools/assets/fetch_ui_assets.py
"""

import io
import json
import os
import re
import sys
import urllib.request

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

try:
    import cairosvg
except ImportError:
    sys.exit("pip install cairosvg")

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RES = os.path.join(ROOT, "Assets", "Resources")
CACHE = os.path.join(os.path.dirname(__file__), ".cache")

FLAG_URL = "https://raw.githubusercontent.com/lipis/flag-icons/main/flags/4x3/{}.svg"
ICON_URL = "https://raw.githubusercontent.com/game-icons/icons/master/{}.svg"
FONT_URL = "https://raw.githubusercontent.com/google/fonts/main/ofl/{}"

# Our icon name -> game-icons path.
ICONS = {
    "treasury": "delapouite/money-stack",
    "debt": "delapouite/bank",
    "gdp": "delapouite/histogram",
    "growth": "lorc/profit",
    "stability": "lorc/checked-shield",
    "approval": "delapouite/thumb-up",
    "population": "delapouite/three-friends",
    "government": "delapouite/podium",
    "ballot": "delapouite/vote",
    "diplomacy": "delapouite/shaking-hands",
    "handshake": "delapouite/shaking-hands",
    "dove": "lorc/dove",
    "trade": "lorc/trade",
    "ship": "delapouite/cargo-ship",
    "sanctions": "lorc/padlock",
    "alliance": "delapouite/flag-objective",
    "alliance_leave": "lorc/broken-shield",
    "war": "lorc/crossed-swords",
    "denounce": "delapouite/megaphone",
    "protest": "delapouite/barricade",
    "news": "delapouite/newspaper",
    "scandal": "delapouite/newspaper",
    "disaster": "delapouite/flood",
    "chip": "lorc/processor",
    "energy": "delapouite/oil-pump",
    "strike": "lorc/gears",
    "factory": "delapouite/factory",
    "military": "lorc/tank",
    "education": "delapouite/graduate-cap",
    "infrastructure": "delapouite/crane",
    "welfare": "delapouite/hand-bandage",
    "admin": "delapouite/stamper",
    "calendar": "delapouite/calendar",
    "settings": "lorc/cog",
    "political": "delapouite/earth-africa-europe",
    "wealth": "delapouite/pay-money",
    "income": "delapouite/receive-money",
    "capital": "delapouite/position-marker",
    "media": "delapouite/tv",
    "reform": "lorc/scroll-unfurled",
    "crown": "lorc/crown",
    "trophy": "delapouite/diamond-trophy",
    "bank": "delapouite/bank",
    "speaker": "delapouite/speaker",
    "mute": "delapouite/speaker-off",
    "person": "delapouite/person",
}

FONTS = {
    "BarlowSemiCondensed-Regular.ttf": "barlowsemicondensed/BarlowSemiCondensed-Regular.ttf",
    "BarlowSemiCondensed-Medium.ttf": "barlowsemicondensed/BarlowSemiCondensed-Medium.ttf",
    "BarlowSemiCondensed-SemiBold.ttf": "barlowsemicondensed/BarlowSemiCondensed-SemiBold.ttf",
    "BarlowSemiCondensed-Bold.ttf": "barlowsemicondensed/BarlowSemiCondensed-Bold.ttf",
    "BarlowCondensed-Bold.ttf": "barlowcondensed/BarlowCondensed-Bold.ttf",
    "OFL.txt": "barlowsemicondensed/OFL.txt",
}

# Countries Natural Earth has no ISO code for, or flag-icons has no flag for.
ISO_OVERRIDES = {"KOS": "xk", "SAH": "eh", "PSX": "ps"}


def fetch(url, cache_name):
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, cache_name)
    if not os.path.exists(path):
        with urllib.request.urlopen(url, timeout=60) as r:
            data = r.read()
        with open(path, "wb") as f:
            f.write(data)
    with open(path, "rb") as f:
        return f.read()


def svg_to_image(svg_bytes, width, height):
    png = cairosvg.svg2png(bytestring=svg_bytes, output_width=width, output_height=height)
    return Image.open(io.BytesIO(png)).convert("RGBA")


def fallback_flag(color_hex, width, height):
    """A simple horizontal tricolour in the country's map colour, for places with no flag."""
    rgb = tuple(int(color_hex[i:i + 2], 16) for i in (1, 3, 5))
    dark = tuple(int(c * 0.55) for c in rgb)
    img = Image.new("RGBA", (width, height), rgb + (255,))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, width, height // 3], fill=dark + (255,))
    d.rectangle([0, 2 * height // 3, width, height], fill=dark + (255,))
    return img


def build_flags():
    out = os.path.join(RES, "Flags")
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(ROOT, "Assets", "StreamingAssets", "Data", "Map", "countries.json"), encoding="utf-8") as f:
        countries = json.load(f)["countries"]
    made = fallback = 0
    for c in countries:
        iso = ISO_OVERRIDES.get(c["tag"], (c.get("isoA2") or "").lower())
        img = None
        if len(iso) == 2 and iso.isalpha():
            try:
                img = svg_to_image(fetch(FLAG_URL.format(iso), f"flag_{iso}.svg"), 128, 96)
            except Exception as e:  # missing flag or network error
                print(f"  no flag for {c['tag']} ({iso}): {e}")
        if img is None:
            img = fallback_flag(c["color"], 128, 96)
            fallback += 1
        else:
            made += 1
        img.save(os.path.join(out, f"{c['tag']}.png"), optimize=True)
    print(f"Flags: {made} real, {fallback} generated")


def build_icons():
    out = os.path.join(RES, "Icons")
    os.makedirs(out, exist_ok=True)
    for name, path in ICONS.items():
        svg = fetch(ICON_URL.format(path), path.replace("/", "_") + ".svg").decode("utf-8")
        # game-icons SVGs are a white glyph on a black square: drop the square, keep the glyph white.
        svg = re.sub(r'<path d="M0 0h512v512H0z"\s*/>', "", svg)
        img = svg_to_image(svg.encode("utf-8"), 64, 64)
        img.save(os.path.join(out, f"{name}.png"), optimize=True)
    print(f"Icons: {len(ICONS)}")


def build_fonts():
    out = os.path.join(RES, "Fonts")
    os.makedirs(out, exist_ok=True)
    for name, path in FONTS.items():
        data = fetch(FONT_URL.format(path), name)
        with open(os.path.join(out, name), "wb") as f:
            f.write(data)
    print(f"Fonts: {len(FONTS)} files")


def gradient(top, bottom, height=256, width=8, noise=0.0):
    t = np.linspace(0, 1, height)[:, None, None]
    top = np.array(top, dtype=np.float32)
    bottom = np.array(bottom, dtype=np.float32)
    img = top + (bottom - top) * t
    img = np.repeat(img, width, axis=1)
    if noise:
        rng = np.random.default_rng(3)
        img[..., :3] += rng.normal(0, noise, img[..., :3].shape)
    return Image.fromarray(np.clip(img, 0, 255).astype(np.uint8), "RGBA")


def build_textures():
    out = os.path.join(RES, "UI", "Textures")
    os.makedirs(out, exist_ok=True)
    gradient((24, 31, 44, 244), (12, 16, 24, 248)).save(os.path.join(out, "panel.png"))
    gradient((40, 50, 68, 255), (20, 26, 38, 255), height=64).save(os.path.join(out, "header.png"))
    gradient((46, 56, 74, 255), (30, 37, 50, 255), height=64).save(os.path.join(out, "button.png"))
    gradient((64, 78, 102, 255), (40, 50, 68, 255), height=64).save(os.path.join(out, "button_hover.png"))
    gradient((176, 138, 62, 255), (122, 92, 36, 255), height=64).save(os.path.join(out, "button_primary.png"))
    gradient((206, 166, 82, 255), (150, 114, 46, 255), height=64).save(os.path.join(out, "button_primary_hover.png"))
    gradient((18, 22, 32, 250), (10, 13, 19, 252), height=64).save(os.path.join(out, "topbar.png"))

    # Soft dark vignette for the screen edges: makes the map feel framed.
    w, h = 512, 288
    y, x = np.mgrid[0:h, 0:w]
    dx = (x - w / 2) / (w / 2)
    dy = (y - h / 2) / (h / 2)
    d = np.sqrt(dx ** 2 * 0.8 + dy ** 2)
    alpha = np.clip((d - 0.75) / 0.6, 0, 1) ** 1.6 * 150
    v = np.zeros((h, w, 4), dtype=np.uint8)
    v[..., 3] = alpha.astype(np.uint8)
    Image.fromarray(v, "RGBA").filter(ImageFilter.GaussianBlur(2)).save(os.path.join(out, "vignette.png"))
    print("Textures: 8")


def write_credits():
    text = """Third-party art used in this game
================================

Flags: flag-icons by Panayiotis Lipiridis and contributors - MIT License
  https://github.com/lipis/flag-icons

Icons: game-icons.net by Lorc (https://lorcblog.blogspot.com) and Delapouite (https://delapouite.com)
  Licensed under Creative Commons Attribution 3.0 (CC BY 3.0) - https://creativecommons.org/licenses/by/3.0/
  Recoloured to white for the interface.

Fonts: Barlow by Jeremy Tribby - SIL Open Font License 1.1 (see Fonts/OFL.txt)
  https://github.com/google/fonts/tree/main/ofl/barlowsemicondensed

Map data and terrain: Natural Earth - public domain
  https://www.naturalearthdata.com/
"""
    with open(os.path.join(RES, "Credits.txt"), "w", encoding="utf-8") as f:
        f.write(text)


def main():
    build_flags()
    build_icons()
    build_fonts()
    build_textures()
    write_credits()


if __name__ == "__main__":
    main()
