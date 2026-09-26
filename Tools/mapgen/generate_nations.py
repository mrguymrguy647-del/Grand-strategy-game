#!/usr/bin/env python3
"""Writes the starting political, economic and diplomatic situation of every country.

Outputs (Assets/StreamingAssets/Data/World/):
  nations.json    government, taxes, spending, debt, growth, stability, approval, elections
  diplomacy.json  alliances and blocs, notable relations, sanctions in force

Figures are rounded approximations of the situation around 2025 (IMF / SIPRI / EIU style
numbers from general knowledge). Major countries are listed explicitly; every other country
gets defaults for its income group and government type. Edit the tables and re-run.

Usage: python3 Tools/mapgen/generate_nations.py   (after generate_map.py)
"""

import hashlib
import json
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MAP_DIR = os.path.join(ROOT, "Assets", "StreamingAssets", "Data", "Map")
OUT_DIR = os.path.join(ROOT, "Assets", "StreamingAssets", "Data", "World")

# ----------------------------------------------------------------------------- governments

FULL = "FullDemocracy"
FLAWED = "FlawedDemocracy"
HYBRID = "HybridRegime"
AUTH = "Authoritarian"
MONARCHY = "AbsoluteMonarchy"

GOVERNMENTS = {
    FULL: "NOR NZL SWE ISL CHE FIN DNK IRL NLD AUS TWN URY LUX DEU CAN JPN AUT CRI GBR GRC EST MUS ESP CZE PRT "
          "AND LIE SMR BRB",
    FLAWED: "USA FRA BEL ITA ISR KOR CHL SVN MLT CYP LTU LVA SVK POL IND BRA ARG IDN PHL ZAF COL PAN DOM JAM TTO "
            "BWA CPV GHA NAM SUR MNG TLS MYS THA HRV HUN ROU BGR GUY PRY LKA MKD MNE MDA KOS CYN MCO PNG SLB VUT "
            "WSM TON KIR TUV FSM MHL PLW BHS ATG DMA GRD KNA LCA VCT SYC BLZ ALB SEN",
    HYBRID: "TUR PAK NGA KEN UKR GEO ARM MAR BOL HND GTM SLV BIH MEX PER ECU BGD NPL BTN MWI ZMB TZA UGA MDG LBR "
            "SLE GMB SRB FJI MDV CIV BEN LSO SOL TUN KGZ",
    AUTH: "CHN RUS IRN PRK SYR BLR TKM TJK UZB KAZ AZE VNM LAO CUB VEN NIC ERI ETH SDN SDS EGY MMR AFG TCD CAF COD "
          "BDI GNQ CMR RWA ZWE DJI YEM LBY IRQ DZA MRT GIN MLI BFA NER TGO GAB COG AGO MOZ KHM SOM JOR BHR PSX "
          "SAH HTI COM GNB STP",
    MONARCHY: "SAU OMN BRN SWZ QAT ARE KWT",
}
GOVERNMENT_OF = {tag: gov for gov, tags in GOVERNMENTS.items() for tag in tags.split()}

# ----------------------------------------------------------------------------- economy defaults

# Keyed by the first character of Natural Earth's income group ("1".."5").
INCOME = {
    #        tax   welfare edu    infra  mil    growth infl  rate   safe  popgrowth
    "1": dict(tax=0.36, welfare=0.17, edu=0.050, infra=0.030, mil=0.018, growth=1.5, infl=2.3, rate=0.030, safe=1.00, pop=0.003),
    "2": dict(tax=0.22, welfare=0.08, edu=0.045, infra=0.040, mil=0.030, growth=2.5, infl=2.5, rate=0.040, safe=0.70, pop=0.010),
    "3": dict(tax=0.24, welfare=0.09, edu=0.045, infra=0.040, mil=0.017, growth=3.0, infl=4.0, rate=0.065, safe=0.60, pop=0.008),
    "4": dict(tax=0.17, welfare=0.05, edu=0.040, infra=0.035, mil=0.015, growth=4.2, infl=5.0, rate=0.080, safe=0.50, pop=0.015),
    "5": dict(tax=0.13, welfare=0.03, edu=0.035, infra=0.030, mil=0.015, growth=4.5, infl=6.0, rate=0.090, safe=0.40, pop=0.025),
}
DEFAULT_DEBT = {"1": 0.80, "2": 0.40, "3": 0.55, "4": 0.55, "5": 0.50}
# How civilian spending splits into welfare / education / infrastructure / administration.
CIVILIAN_SHARES = {
    "1": (0.55, 0.15, 0.12, 0.18),
    "2": (0.35, 0.20, 0.20, 0.25),
    "3": (0.40, 0.18, 0.17, 0.25),
    "4": (0.30, 0.20, 0.20, 0.30),
    "5": (0.25, 0.20, 0.20, 0.35),
}
DEFAULT_BALANCE = -0.03  # typical budget balance (incl. interest), share of GDP

# tax: government revenue / GDP, debt: gross debt / GDP, mil: military spending / GDP,
# growth: real growth %/yr, infl: inflation %/yr, rate: average interest on debt,
# safe: debt ratio markets tolerate without a risk premium, bal: budget balance / GDP,
# aid: foreign grants / GDP.
MAJOR = {
    "USA": dict(tax=0.30, debt=1.22, mil=0.034, growth=2.0, infl=2.7, rate=0.033, safe=1.4, bal=-0.065),
    "CHN": dict(tax=0.26, debt=0.88, mil=0.017, growth=4.6, infl=0.5, rate=0.030, safe=1.2, bal=-0.07),
    "JPN": dict(tax=0.37, debt=2.35, mil=0.014, growth=0.8, infl=2.5, rate=0.007, safe=2.8, bal=-0.03),
    "DEU": dict(tax=0.46, debt=0.63, mil=0.021, growth=0.8, infl=2.2, rate=0.018, safe=1.2, bal=-0.025),
    "IND": dict(tax=0.19, debt=0.82, mil=0.023, growth=6.4, infl=4.5, rate=0.075, safe=1.0, bal=-0.075),
    "GBR": dict(tax=0.39, debt=1.01, mil=0.023, growth=1.3, infl=3.0, rate=0.035, safe=1.3, bal=-0.045),
    "FRA": dict(tax=0.51, debt=1.13, mil=0.021, growth=0.9, infl=1.5, rate=0.020, safe=1.3, bal=-0.055),
    "ITA": dict(tax=0.47, debt=1.35, mil=0.016, growth=0.7, infl=1.8, rate=0.030, safe=1.5, bal=-0.035),
    "BRA": dict(tax=0.40, debt=0.87, mil=0.011, growth=2.2, infl=4.8, rate=0.100, safe=1.0, bal=-0.075),
    "CAN": dict(tax=0.41, debt=1.07, mil=0.014, growth=1.6, infl=2.2, rate=0.030, safe=1.3, bal=-0.02),
    "RUS": dict(tax=0.35, debt=0.20, mil=0.071, growth=1.0, infl=8.0, rate=0.080, safe=0.6, bal=-0.025),
    "KOR": dict(tax=0.23, debt=0.53, mil=0.026, growth=2.0, infl=2.0, rate=0.025, safe=1.0, bal=-0.02),
    "AUS": dict(tax=0.36, debt=0.49, mil=0.020, growth=2.0, infl=2.8, rate=0.035, safe=1.0, bal=-0.02),
    "ESP": dict(tax=0.42, debt=1.02, mil=0.013, growth=2.4, infl=2.4, rate=0.025, safe=1.3, bal=-0.03),
    "MEX": dict(tax=0.25, debt=0.58, mil=0.007, growth=1.2, infl=3.8, rate=0.085, safe=0.8, bal=-0.04),
    "IDN": dict(tax=0.15, debt=0.40, mil=0.007, growth=5.0, infl=2.0, rate=0.065, safe=0.8, bal=-0.025),
    "TUR": dict(tax=0.30, debt=0.25, mil=0.022, growth=3.0, infl=30.0, rate=0.200, safe=0.6, bal=-0.03),
    "NLD": dict(tax=0.43, debt=0.44, mil=0.020, growth=1.3, infl=2.8, rate=0.020, safe=1.2, bal=-0.02),
    "SAU": dict(tax=0.30, debt=0.30, mil=0.071, growth=3.5, infl=2.0, rate=0.040, safe=1.0, bal=-0.03),
    "CHE": dict(tax=0.33, debt=0.37, mil=0.007, growth=1.3, infl=0.5, rate=0.008, safe=1.2, bal=0.0),
    "TWN": dict(tax=0.17, debt=0.26, mil=0.025, growth=3.0, infl=2.0, rate=0.015, safe=1.0, bal=0.0),
    "POL": dict(tax=0.41, debt=0.55, mil=0.042, growth=3.2, infl=4.0, rate=0.045, safe=1.0, bal=-0.06),
    "BEL": dict(tax=0.50, debt=1.05, mil=0.013, growth=1.1, infl=2.8, rate=0.022, safe=1.3, bal=-0.045),
    "SWE": dict(tax=0.48, debt=0.34, mil=0.022, growth=1.8, infl=1.5, rate=0.020, safe=1.0, bal=-0.01),
    "IRL": dict(tax=0.24, debt=0.43, mil=0.002, growth=3.0, infl=2.0, rate=0.020, safe=1.0, bal=0.02),
    "ARG": dict(tax=0.35, debt=0.83, mil=0.005, growth=4.0, infl=30.0, rate=0.100, safe=0.6, bal=0.0),
    "NOR": dict(tax=0.55, debt=0.43, mil=0.022, growth=1.5, infl=3.0, rate=0.030, safe=1.2, bal=0.10),
    "ISR": dict(tax=0.36, debt=0.69, mil=0.088, growth=2.5, infl=3.0, rate=0.035, safe=1.0, bal=-0.06),
    "ARE": dict(tax=0.30, debt=0.30, mil=0.050, growth=4.0, infl=2.0, rate=0.035, safe=1.0, bal=0.04),
    "EGY": dict(tax=0.20, debt=0.90, mil=0.012, growth=4.0, infl=15.0, rate=0.095, safe=0.8, bal=-0.07),
    "PAK": dict(tax=0.13, debt=0.70, mil=0.028, growth=3.0, infl=6.0, rate=0.100, safe=0.7, bal=-0.06),
    "NGA": dict(tax=0.10, debt=0.45, mil=0.006, growth=3.2, infl=20.0, rate=0.110, safe=0.6, bal=-0.04),
    "ZAF": dict(tax=0.28, debt=0.75, mil=0.007, growth=1.0, infl=3.5, rate=0.090, safe=0.8, bal=-0.05),
    "UKR": dict(tax=0.40, debt=0.95, mil=0.260, growth=2.5, infl=12.0, rate=0.040, safe=1.2, bal=-0.20, aid=0.20),
    "IRN": dict(tax=0.12, debt=0.35, mil=0.021, growth=1.5, infl=35.0, rate=0.180, safe=0.5, bal=-0.04),
    "VNM": dict(tax=0.19, debt=0.35, mil=0.023, growth=6.5, infl=3.5, rate=0.045, safe=0.8, bal=-0.03),
    "THA": dict(tax=0.20, debt=0.63, mil=0.013, growth=2.5, infl=0.5, rate=0.030, safe=1.0, bal=-0.03),
    "MYS": dict(tax=0.17, debt=0.64, mil=0.010, growth=4.5, infl=2.0, rate=0.040, safe=1.0, bal=-0.04),
    "PHL": dict(tax=0.16, debt=0.60, mil=0.013, growth=5.8, infl=3.0, rate=0.050, safe=0.9, bal=-0.055),
    "SGP": dict(tax=0.18, debt=1.70, mil=0.028, growth=2.5, infl=2.0, rate=0.020, safe=3.0, bal=0.03),
    "GRC": dict(tax=0.49, debt=1.54, mil=0.031, growth=2.2, infl=2.8, rate=0.017, safe=1.8, bal=0.0),
    "PRT": dict(tax=0.44, debt=0.95, mil=0.015, growth=2.0, infl=2.3, rate=0.025, safe=1.3, bal=0.005),
    "CHL": dict(tax=0.25, debt=0.42, mil=0.015, growth=2.5, infl=4.0, rate=0.050, safe=0.9, bal=-0.025),
    "COL": dict(tax=0.28, debt=0.60, mil=0.030, growth=2.0, infl=5.0, rate=0.080, safe=0.8, bal=-0.06),
    "DZA": dict(tax=0.30, debt=0.50, mil=0.080, growth=3.0, infl=4.0, rate=0.050, safe=0.8, bal=-0.08),
    "KAZ": dict(tax=0.20, debt=0.24, mil=0.007, growth=4.5, infl=8.0, rate=0.080, safe=0.6, bal=-0.03),
    "QAT": dict(tax=0.35, debt=0.42, mil=0.040, growth=2.0, infl=1.5, rate=0.035, safe=1.0, bal=0.03),
    "KWT": dict(tax=0.45, debt=0.08, mil=0.048, growth=2.0, infl=3.0, rate=0.030, safe=1.0, bal=-0.03),
    "IRQ": dict(tax=0.35, debt=0.45, mil=0.035, growth=2.5, infl=3.5, rate=0.050, safe=0.8, bal=-0.05),
    "PRK": dict(tax=0.30, debt=0.20, mil=0.200, growth=1.0, infl=5.0, rate=0.050, safe=0.5, bal=-0.02),
    "SYR": dict(tax=0.10, debt=0.80, mil=0.050, growth=1.0, infl=40.0, rate=0.050, safe=1.0, bal=-0.03, aid=0.03),
    "LBN": dict(tax=0.10, debt=1.60, mil=0.030, growth=1.0, infl=45.0, rate=0.010, safe=2.0, bal=-0.01),
    "VEN": dict(tax=0.12, debt=1.50, mil=0.005, growth=2.0, infl=60.0, rate=0.010, safe=2.0, bal=-0.05),
    "ETH": dict(tax=0.08, debt=0.40, mil=0.008, growth=6.5, infl=20.0, rate=0.050, safe=0.8, bal=-0.02),
    "BGD": dict(tax=0.09, debt=0.37, mil=0.010, growth=4.5, infl=9.0, rate=0.070, safe=0.8, bal=-0.04),
    "HUN": dict(tax=0.43, debt=0.74, mil=0.021, growth=1.0, infl=4.5, rate=0.045, safe=1.0, bal=-0.05),
    "ROU": dict(tax=0.33, debt=0.55, mil=0.023, growth=1.0, infl=5.0, rate=0.050, safe=0.9, bal=-0.085),
    "CZE": dict(tax=0.41, debt=0.43, mil=0.021, growth=2.0, infl=2.5, rate=0.035, safe=1.0, bal=-0.02),
    "FIN": dict(tax=0.52, debt=0.82, mil=0.024, growth=1.0, infl=1.5, rate=0.025, safe=1.2, bal=-0.04),
    "DNK": dict(tax=0.52, debt=0.31, mil=0.024, growth=2.5, infl=2.0, rate=0.020, safe=1.0, bal=0.02),
    "AUT": dict(tax=0.50, debt=0.81, mil=0.010, growth=0.8, infl=3.0, rate=0.025, safe=1.2, bal=-0.04),
    "NZL": dict(tax=0.38, debt=0.46, mil=0.013, growth=1.0, infl=2.5, rate=0.040, safe=1.0, bal=-0.03),
}

# Starting stability / approval where the default would be clearly wrong.
MOOD = {
    "USA": (55, 42), "CHN": (70, 70), "RUS": (60, 68), "IND": (62, 60), "JPN": (62, 30), "DEU": (62, 35),
    "GBR": (58, 32), "FRA": (50, 30), "ITA": (58, 42), "BRA": (52, 45), "CAN": (66, 48), "KOR": (50, 40),
    "TUR": (50, 45), "ARG": (45, 45), "MEX": (50, 65), "ISR": (45, 40), "IRN": (40, 35), "PRK": (70, 60),
    "UKR": (40, 55), "SAU": (70, 65), "EGY": (50, 45), "PAK": (38, 38), "NGA": (40, 35), "ZAF": (52, 42),
    "SDN": (18, 30), "SDS": (20, 30), "YEM": (18, 30), "SYR": (28, 50), "AFG": (32, 40), "MMR": (20, 25),
    "HTI": (20, 25), "LBY": (26, 40), "SOM": (24, 40), "MLI": (28, 45), "BFA": (26, 50), "NER": (30, 50),
    "COD": (30, 40), "CAF": (26, 40), "ETH": (38, 45), "VEN": (30, 35), "LBN": (30, 30), "IRQ": (40, 45),
    "PSX": (22, 35), "SAH": (30, 50), "SOL": (45, 55), "BLR": (60, 60), "CUB": (40, 40), "NIC": (45, 45),
    "BGD": (40, 45), "LKA": (50, 50), "MOZ": (35, 40), "CMR": (38, 45), "TCD": (30, 45), "ERI": (45, 50),
}

# Next national election (year, month) and interval in years, where known.
ELECTIONS = {
    "USA": (2028, 11, 4), "GBR": (2029, 8, 5), "FRA": (2027, 4, 5), "DEU": (2029, 2, 4), "IND": (2029, 5, 5),
    "BRA": (2026, 10, 4), "JPN": (2028, 7, 4), "CAN": (2029, 10, 4), "AUS": (2028, 5, 3), "ITA": (2027, 9, 5),
    "ESP": (2027, 8, 4), "KOR": (2030, 6, 5), "MEX": (2030, 6, 6), "TUR": (2028, 5, 5), "ZAF": (2029, 5, 5),
    "IDN": (2029, 2, 5), "POL": (2027, 10, 4), "ARG": (2027, 10, 4), "ISR": (2026, 10, 4), "SWE": (2026, 9, 4),
    "COL": (2026, 5, 4), "PER": (2026, 4, 5), "HUN": (2026, 4, 4), "NLD": (2029, 10, 4), "NOR": (2029, 9, 4),
    "CHL": (2029, 11, 4), "PHL": (2028, 5, 6), "TWN": (2028, 1, 4), "UKR": (2027, 3, 5), "PAK": (2029, 2, 5),
    "NGA": (2027, 2, 4), "KEN": (2027, 8, 5), "BGD": (2026, 2, 5), "GRC": (2027, 6, 4), "PRT": (2029, 5, 4),
}

# ----------------------------------------------------------------------------- diplomacy

EU = "AUT BEL BGR HRV CYP CZE DNK EST FIN FRA DEU GRC HUN IRL ITA LVA LTU LUX MLT NLD POL PRT ROU SVK SVN ESP SWE"
NATO = ("USA CAN GBR FRA DEU ITA ESP PRT NLD BEL LUX DNK NOR ISL TUR GRC POL CZE SVK HUN ROU BGR HRV SVN ALB MNE "
        "MKD EST LVA LTU FIN SWE")
AFRICA = ("DZA AGO BEN BWA BFA BDI CMR CPV CAF TCD COM COD COG CIV DJI EGY GNQ ERI SWZ ETH GAB GMB GHA GIN GNB KEN "
          "LSO LBR LBY MDG MWI MLI MRT MUS MAR MOZ NAM NER NGA RWA STP SEN SYC SLE SOM ZAF SDS SDN TZA TGO TUN UGA "
          "ZMB ZWE SAH")

BLOCS = [
    # id, name, kind, members.  kind: alliance (mutual defence), union, trade (free-trade area), forum.
    ("NATO", "NATO", "alliance", NATO),
    ("CSTO", "CSTO", "alliance", "RUS BLR KAZ KGZ TJK"),
    ("US-JPN", "US-Japan Security Treaty", "alliance", "USA JPN"),
    ("US-KOR", "US-Korea Mutual Defense Treaty", "alliance", "USA KOR"),
    ("ANZUS", "ANZUS", "alliance", "USA AUS NZL"),
    ("US-PHL", "US-Philippines Mutual Defense Treaty", "alliance", "USA PHL"),
    ("CHN-PRK", "Sino-North Korean Treaty", "alliance", "CHN PRK"),
    ("RUS-PRK", "Russia-North Korea Partnership", "alliance", "RUS PRK"),
    ("EU", "European Union", "union", EU),
    ("USMCA", "USMCA", "trade", "USA CAN MEX"),
    ("MERCOSUR", "Mercosur", "trade", "ARG BRA PRY URY BOL"),
    ("EAEU", "Eurasian Economic Union", "trade", "RUS BLR KAZ KGZ ARM"),
    ("ASEAN", "ASEAN", "trade", "IDN THA MYS SGP PHL VNM BRN KHM LAO MMR TLS"),
    ("GCC", "Gulf Cooperation Council", "trade", "SAU ARE QAT KWT BHR OMN"),
    ("G7", "G7", "forum", "USA JPN DEU GBR FRA ITA CAN"),
    ("BRICS", "BRICS", "forum", "BRA RUS IND CHN ZAF EGY ETH IRN ARE IDN"),
    ("SCO", "Shanghai Cooperation Organisation", "forum", "CHN RUS IND PAK KAZ KGZ TJK UZB IRN BLR"),
    ("ARAB", "Arab League", "forum", "DZA BHR COM DJI EGY IRQ JOR KWT LBN LBY MRT MAR OMN PSX QAT SAU SOM SDN SYR TUN ARE YEM"),
    ("AU", "African Union", "forum", AFRICA),
]

# Notable relations (-100 hostile .. 100 close). Everything else starts from bloc/government baselines.
RELATIONS = """
USA RUS -60; USA CHN -35; USA IRN -80; USA PRK -90; USA CUB -50; USA VEN -60; USA GBR 80; USA ISR 70; USA JPN 75;
USA KOR 70; USA CAN 65; USA AUS 75; USA TWN 60; USA SAU 45; USA IND 40; USA UKR 55; USA MEX 40; USA PHL 55;
RUS UKR -100; RUS BLR 85; RUS CHN 60; RUS IRN 50; RUS PRK 60; RUS SYR 10; RUS IND 40; RUS GBR -65; RUS POL -75;
RUS LTU -75; RUS LVA -75; RUS EST -75; RUS FIN -60; RUS DEU -55; RUS FRA -55; RUS SWE -55; RUS GEO -45; RUS KAZ 45;
CHN TWN -70; CHN JPN -30; CHN IND -35; CHN PAK 75; CHN PRK 55; CHN PHL -40; CHN VNM -15; CHN KOR 0; CHN AUS -15;
ISR IRN -100; ISR PSX -90; ISR LBN -70; ISR SYR -60; ISR YEM -80; ISR EGY 10; ISR ARE 20; ISR JOR 0; ISR TUR -40;
IND PAK -70; KOR PRK -90; JPN PRK -80; JPN KOR 20; SAU IRN -30; SAU ARE 70; ARM AZE -70; GRC TUR -20; CYP TUR -50;
CYP CYN -60; TUR CYN 80; UKR BLR -60; MAR DZA -55; MAR SAH -80; DZA SAH 50; ETH ERI -40; RWA COD -55; VEN GUY -45;
KOS SRB -70; SRB RUS 45; SDN SDS -40; FRA DEU 80; GBR IRL 55; AUS NZL 85; USA NZL 55; CAN GBR 75; GBR FRA 60;
IRN IRQ 40; IRN SYR 20; PAK AFG -30; IND BGD 30; SOM SOL -50; ETH SOL 20; QAT SAU 20; TUR AZE 80; RUS ARM 10;
UKR POL 55; UKR GBR 65; UKR DEU 50; UKR FRA 50; UKR LTU 70; HUN UKR -20; VEN CUB 70; NIC CUB 60; NIC RUS 40;
"""

# Sanctions regimes in force: (list of sanctioning countries, list of targets).
WEST = "USA CAN GBR AUS JPN NZL NOR CHE ISL KOR " + EU
SANCTIONS = [
    (WEST, "RUS BLR PRK"),
    ("USA CAN GBR AUS " + EU, "IRN"),
    ("USA", "CUB VEN"),
]


# ----------------------------------------------------------------------------- build

def stable_fraction(tag, salt):
    h = hashlib.md5(f"{tag}:{salt}".encode()).hexdigest()
    return int(h[:8], 16) / 0xFFFFFFFF


def build_nation(country):
    tag = country["tag"]
    income = (country.get("incomeGroup") or "4")[0]
    if income not in INCOME:
        income = "4"
    d = INCOME[income]
    m = MAJOR.get(tag, {})
    gov = GOVERNMENT_OF.get(tag, FLAWED if income in "123" else HYBRID)

    tax = m.get("tax", d["tax"])
    debt = m.get("debt", DEFAULT_DEBT[income])
    mil = m.get("mil", d["mil"])
    rate = m.get("rate", d["rate"])
    aid = m.get("aid", 0.0)
    balance = m.get("bal", DEFAULT_BALANCE)

    # Spend what the budget balance implies: revenue + aid - interest - balance = programme spending,
    # then split the civilian part the way countries of this income level typically do.
    programme = tax + aid - debt * rate - balance
    civilian = max(0.03, programme - mil)
    shares = CIVILIAN_SHARES[income]
    welfare, edu, infra, admin = (civilian * s for s in shares)

    base_stab = {FULL: 72, FLAWED: 62, HYBRID: 52, AUTH: 58, MONARCHY: 68}[gov]
    base_app = {FULL: 45, FLAWED: 45, HYBRID: 48, AUTH: 58, MONARCHY: 62}[gov]
    income_adj = {"1": 5, "2": 5, "3": 0, "4": -4, "5": -8}[income]
    stability, approval = MOOD.get(tag, (base_stab + income_adj, base_app))

    record = {
        "tag": tag,
        "government": gov,
        "taxRate": round(tax, 4),
        "debtToGdp": round(debt, 4),
        "interestRate": round(rate, 4),
        "safeDebtToGdp": round(m.get("safe", d["safe"]), 3),
        "foreignAid": round(aid, 4),
        "militarySpending": round(mil, 4),
        "welfareSpending": round(welfare, 4),
        "educationSpending": round(edu, 4),
        "infrastructureSpending": round(infra, 4),
        "adminSpending": round(admin, 4),
        "realGrowth": m.get("growth", d["growth"]),
        "inflation": m.get("infl", d["infl"]),
        "populationGrowth": d["pop"],
        "stability": stability,
        "approval": approval,
        "electionYear": 0,
        "electionMonth": 0,
        "electionInterval": 0,
    }
    if gov in (FULL, FLAWED, HYBRID):
        if tag in ELECTIONS:
            y, mth, interval = ELECTIONS[tag]
        else:
            interval = 4 if stable_fraction(tag, "i") < 0.6 else 5
            y = 2026 + int(stable_fraction(tag, "y") * interval)
            mth = 1 + int(stable_fraction(tag, "m") * 12)
        record.update(electionYear=y, electionMonth=mth, electionInterval=interval)
    return record


def main():
    with open(os.path.join(MAP_DIR, "countries.json"), encoding="utf-8") as f:
        countries = json.load(f)["countries"]
    tags = {c["tag"] for c in countries}

    nations = [build_nation(c) for c in countries]

    blocs = []
    for bloc_id, name, kind, members in BLOCS:
        present = [t for t in members.split() if t in tags]
        missing = [t for t in members.split() if t not in tags]
        if missing:
            print(f"  {bloc_id}: not on the map: {' '.join(missing)}")
        blocs.append({"id": bloc_id, "name": name, "kind": kind, "members": present})

    relations = []
    for item in RELATIONS.replace("\n", " ").split(";"):
        parts = item.split()
        if len(parts) != 3:
            continue
        a, b, v = parts
        if a in tags and b in tags:
            relations.append({"a": a, "b": b, "value": int(v)})
        else:
            print(f"  relation {a}-{b}: not on the map")

    sanctions = []
    for by, targets in SANCTIONS:
        by_tags = [t for t in dict.fromkeys(by.split()) if t in tags]
        for target in targets.split():
            if target in tags:
                sanctions.append({"target": target, "by": [t for t in by_tags if t != target]})

    os.makedirs(OUT_DIR, exist_ok=True)

    def dump(name, key, items, note):
        with open(os.path.join(OUT_DIR, name), "w", encoding="utf-8") as f:
            f.write("{\n")
            f.write(f'  "note": {json.dumps(note)},\n')
            f.write(f'  "{key}": [\n')
            f.write(",\n".join("    " + json.dumps(i, ensure_ascii=False) for i in items))
            f.write("\n  ]\n}\n")

    note = "Approximate situation around 2025, generated by Tools/mapgen/generate_nations.py"
    dump("nations.json", "nations", nations, note)
    with open(os.path.join(OUT_DIR, "diplomacy.json"), "w", encoding="utf-8") as f:
        json.dump({"note": note, "blocs": blocs, "relations": relations, "sanctions": sanctions}, f, indent=1, ensure_ascii=False)
        f.write("\n")
    print(f"Wrote {len(nations)} nations, {len(blocs)} blocs, {len(relations)} relations, {len(sanctions)} sanction regimes")


if __name__ == "__main__":
    main()
