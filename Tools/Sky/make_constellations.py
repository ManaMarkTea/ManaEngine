"""Writes ManaGraphics/Assets/Shaders/WebGpu/include/constellations.wgsl: a catalogue of bright stars (J2000 right
ascension h/m, declination degrees/arcminutes, apparent magnitude, warmth 0 blue .. 1 red) and the line figures
joining them. Positions are good to about an arcminute, plenty for a sky drawn at a pixel per 3.5 arcminutes.
Run from the repo root: python Tools/Sky/make_constellations.py
"""
import math
from pathlib import Path

# name: (ra_h, ra_m, dec_deg, dec_min, mag, warmth). A southern declination has both its parts negative.
STARS = {
    # Orion
    "Betelgeuse": (5, 55.2, 7, 24, 0.5, 1.0), "Rigel": (5, 14.5, -8, -12, 0.13, 0.0), "Bellatrix": (5, 25.1, 6, 21, 1.64, 0.1),
    "Mintaka": (5, 32.0, 0, -18, 2.2, 0.0), "Alnilam": (5, 36.2, -1, -12, 1.7, 0.0), "Alnitak": (5, 40.8, -1, -57, 1.77, 0.0),
    "Saiph": (5, 47.8, -9, -40, 2.06, 0.0), "Meissa": (5, 35.1, 9, 56, 3.5, 0.1),
    # Ursa Major
    "Dubhe": (11, 3.7, 61, 45, 1.79, 0.8), "Merak": (11, 1.8, 56, 23, 2.37, 0.4), "Phecda": (11, 53.8, 53, 41, 2.44, 0.4),
    "Megrez": (12, 15.4, 57, 2, 3.31, 0.4), "Alioth": (12, 54.0, 55, 58, 1.77, 0.3), "Mizar": (13, 23.9, 54, 56, 2.27, 0.4),
    "Alkaid": (13, 47.5, 49, 19, 1.86, 0.0),
    # Ursa Minor
    "Polaris": (2, 31.8, 89, 16, 1.98, 0.5), "Kochab": (14, 50.7, 74, 9, 2.08, 0.8), "Pherkad": (15, 20.7, 71, 50, 3.0, 0.3),
    "Yildun": (17, 32.2, 86, 35, 4.35, 0.4), "EpsUMi": (16, 45.9, 82, 2, 4.2, 0.5), "ZetUMi": (15, 44.1, 77, 47, 4.3, 0.4),
    "EtaUMi": (16, 17.5, 75, 45, 4.95, 0.5),
    # Cassiopeia
    "Schedar": (0, 40.5, 56, 32, 2.24, 0.8), "Caph": (0, 9.2, 59, 9, 2.28, 0.5), "GamCas": (0, 56.7, 60, 43, 2.47, 0.0),
    "Ruchbah": (1, 25.8, 60, 14, 2.68, 0.3), "Segin": (1, 54.4, 63, 40, 3.37, 0.0),
    # Cygnus
    "Deneb": (20, 41.4, 45, 17, 1.25, 0.4), "Sadr": (20, 22.2, 40, 15, 2.2, 0.5), "Gienah": (20, 46.2, 33, 58, 2.46, 0.7),
    "DelCyg": (19, 45.0, 45, 8, 2.87, 0.1), "Albireo": (19, 30.7, 27, 58, 3.1, 0.7),
    # Lyra
    "Vega": (18, 36.9, 38, 47, 0.03, 0.1), "Sheliak": (18, 50.1, 33, 22, 3.5, 0.2), "Sulafat": (18, 58.9, 32, 41, 3.25, 0.1),
    "DelLyr": (18, 54.5, 36, 54, 4.3, 0.9), "ZetLyr": (18, 44.8, 37, 36, 4.3, 0.3),
    # Aquila
    "Altair": (19, 50.8, 8, 52, 0.77, 0.3), "Tarazed": (19, 46.3, 10, 37, 2.72, 0.9), "Alshain": (19, 55.3, 6, 24, 3.71, 0.6),
    # Scorpius
    "Antares": (16, 29.4, -26, -26, 1.06, 1.0), "Dschubba": (16, 0.3, -22, -37, 2.32, 0.0), "Graffias": (16, 5.4, -19, -48, 2.6, 0.1),
    "PiSco": (15, 58.9, -26, -7, 2.89, 0.0), "SigSco": (16, 21.2, -25, -36, 2.9, 0.1), "EpsSco": (16, 50.2, -34, -18, 2.3, 0.8),
    "MuSco": (16, 51.9, -38, -3, 3.0, 0.0), "ZetSco": (16, 54.6, -42, -22, 3.6, 0.8), "EtaSco": (17, 12.2, -43, -14, 3.3, 0.4),
    "Sargas": (17, 37.3, -42, -59, 1.87, 0.4), "IotSco": (17, 47.6, -40, -7, 3.0, 0.4), "KapSco": (17, 42.5, -39, -1, 2.4, 0.0),
    "Shaula": (17, 33.6, -37, -6, 1.63, 0.0), "Lesath": (17, 30.8, -37, -18, 2.7, 0.0),
    # Leo
    "Regulus": (10, 8.4, 11, 58, 1.35, 0.0), "Denebola": (11, 49.1, 14, 34, 2.14, 0.2), "Algieba": (10, 20.0, 19, 51, 2.08, 0.7),
    "Zosma": (11, 14.1, 20, 31, 2.56, 0.2), "Adhafera": (10, 16.7, 23, 25, 3.4, 0.4), "Rasalas": (9, 52.8, 26, 0, 3.9, 0.7),
    "Algenubi": (9, 45.9, 23, 46, 2.98, 0.6), "Chertan": (11, 14.2, 15, 26, 3.3, 0.1),
    # Taurus
    "Aldebaran": (4, 35.9, 16, 31, 0.85, 0.9), "Elnath": (5, 26.3, 28, 36, 1.65, 0.0), "ZetTau": (5, 37.6, 21, 9, 3.0, 0.0),
    "Ain": (4, 28.6, 19, 11, 3.5, 0.8), "GamTau": (4, 19.8, 15, 38, 3.65, 0.6), "Alcyone": (3, 47.0, 24, 7, 2.87, 0.0),
    "TheTau": (4, 28.7, 15, 52, 3.4, 0.5),
    # Gemini
    "Castor": (7, 34.6, 31, 53, 1.58, 0.1), "Pollux": (7, 45.3, 28, 2, 1.14, 0.8), "Alhena": (6, 37.7, 16, 24, 1.93, 0.2),
    "Tejat": (6, 22.9, 22, 30, 2.88, 1.0), "Mebsuta": (6, 43.9, 25, 8, 3.06, 0.5), "Wasat": (7, 20.1, 21, 59, 3.53, 0.3),
    "Propus": (6, 14.9, 22, 30, 3.3, 1.0),
    # Canis Major / Minor
    "Sirius": (6, 45.1, -16, -43, -1.46, 0.1), "Mirzam": (6, 22.7, -17, -57, 1.98, 0.0), "Adhara": (6, 58.6, -28, -58, 1.5, 0.0),
    "Wezen": (7, 8.4, -26, -23, 1.84, 0.4), "Aludra": (7, 24.1, -29, -18, 2.4, 0.0), "Furud": (6, 20.3, -30, -3, 3.0, 0.0),
    "Procyon": (7, 39.3, 5, 13, 0.34, 0.3), "Gomeisa": (7, 27.2, 8, 17, 2.9, 0.0),
    # Crux, Centaurus
    "Acrux": (12, 26.6, -63, -6, 0.76, 0.0), "Mimosa": (12, 47.7, -59, -41, 1.25, 0.0), "Gacrux": (12, 31.2, -57, -7, 1.6, 1.0),
    "DelCru": (12, 15.1, -58, -45, 2.8, 0.0), "RigilKent": (14, 39.6, -60, -50, -0.27, 0.3), "Hadar": (14, 3.8, -60, -22, 0.61, 0.0),
    # Bootes
    "Arcturus": (14, 15.7, 19, 11, -0.05, 0.8), "Izar": (14, 45.0, 27, 4, 2.37, 0.7), "Seginus": (14, 32.1, 38, 19, 3.0, 0.2),
    "Nekkar": (15, 1.9, 40, 23, 3.5, 0.4), "Muphrid": (13, 54.7, 18, 24, 2.7, 0.4),
    # Pegasus, Andromeda
    "Markab": (23, 4.8, 15, 13, 2.49, 0.0), "Scheat": (23, 3.8, 28, 5, 2.42, 1.0), "Algenib": (0, 13.2, 15, 11, 2.83, 0.0),
    "Alpheratz": (0, 8.4, 29, 5, 2.06, 0.0), "Mirach": (1, 9.7, 35, 37, 2.06, 0.9), "Almach": (2, 3.9, 42, 20, 2.1, 0.6),
    # Sagittarius
    "KausAustralis": (18, 24.2, -34, -23, 1.85, 0.0), "Nunki": (18, 55.3, -26, -18, 2.05, 0.0), "Ascella": (19, 2.6, -29, -53, 2.6, 0.2),
    "KausMedia": (18, 21.0, -29, -50, 2.7, 0.8), "KausBorealis": (18, 28.0, -25, -25, 2.8, 0.5), "PhiSgr": (18, 45.7, -26, -59, 3.2, 0.0),
    "TauSgr": (19, 6.9, -27, -40, 3.3, 0.7),
    # Auriga, Perseus
    "Capella": (5, 16.7, 46, 0, 0.08, 0.5), "Menkalinan": (5, 59.5, 44, 57, 1.9, 0.2), "Hassaleh": (4, 57.0, 33, 10, 2.7, 0.9),
    "Mirfak": (3, 24.3, 49, 52, 1.8, 0.4), "Algol": (3, 8.2, 40, 57, 2.1, 0.0),
    # Singles worth having
    "Spica": (13, 25.2, -11, -10, 0.97, 0.0), "Fomalhaut": (22, 57.7, -29, -37, 1.16, 0.1), "Achernar": (1, 37.7, -57, -14, 0.46, 0.0),
    "Canopus": (6, 24.0, -52, -42, -0.72, 0.4), "Alphecca": (15, 34.8, 26, 43, 2.2, 0.1), "Rasalhague": (17, 34.9, 12, 34, 2.08, 0.2),
    "Alphard": (9, 27.6, -8, -40, 2.0, 0.9), "Alderamin": (21, 18.6, 62, 35, 2.5, 0.2), "Eltanin": (17, 56.6, 51, 29, 2.2, 0.9),
    "Zubenelgenubi": (14, 50.9, -16, -2, 2.75, 0.2), "Zubeneschamali": (15, 17.0, -9, -23, 2.6, 0.0), "Hamal": (2, 7.2, 23, 28, 2.0, 0.8),
    "Diphda": (0, 43.6, -17, -59, 2.0, 0.8), "DenebAlgedi": (21, 47.0, -16, -8, 2.9, 0.2), "Sadalsuud": (21, 31.6, -5, -34, 2.9, 0.3),
}

# Each figure is a list of paths; a path joins consecutive stars.
FIGURES = {
    "Orion": [["Meissa", "Betelgeuse", "Alnitak", "Saiph"], ["Meissa", "Bellatrix", "Mintaka", "Rigel"], ["Mintaka", "Alnilam", "Alnitak"]],
    "Ursa Major": [["Dubhe", "Merak", "Phecda", "Megrez", "Dubhe"], ["Megrez", "Alioth", "Mizar", "Alkaid"]],
    "Ursa Minor": [["Polaris", "Yildun", "EpsUMi", "ZetUMi", "Kochab", "Pherkad", "EtaUMi", "ZetUMi"]],
    "Cassiopeia": [["Caph", "Schedar", "GamCas", "Ruchbah", "Segin"]],
    "Cygnus": [["Deneb", "Sadr", "Albireo"], ["DelCyg", "Sadr", "Gienah"]],
    "Lyra": [["Vega", "ZetLyr", "DelLyr", "Sulafat", "Sheliak", "ZetLyr"]],
    "Aquila": [["Tarazed", "Altair", "Alshain"]],
    "Scorpius": [["Graffias", "Dschubba", "PiSco"], ["Dschubba", "SigSco", "Antares", "EpsSco", "MuSco", "ZetSco", "EtaSco", "Sargas", "IotSco", "KapSco", "Shaula", "Lesath"]],
    "Leo": [["Regulus", "Algieba", "Adhafera", "Rasalas", "Algenubi"], ["Algieba", "Zosma", "Denebola", "Chertan", "Regulus"], ["Zosma", "Chertan"]],
    "Taurus": [["GamTau", "TheTau", "Aldebaran", "ZetTau"], ["Ain", "TheTau"], ["Ain", "Elnath"]],
    "Gemini": [["Castor", "Mebsuta", "Tejat", "Propus"], ["Pollux", "Wasat", "Alhena"]],
    "Canis Major": [["Mirzam", "Sirius", "Wezen", "Adhara", "Furud"], ["Wezen", "Aludra"]],
    "Canis Minor": [["Procyon", "Gomeisa"]],
    "Crux": [["Acrux", "Gacrux"], ["Mimosa", "DelCru"]],
    "Centaurus": [["RigilKent", "Hadar"]],
    "Bootes": [["Arcturus", "Izar", "Seginus", "Nekkar"], ["Arcturus", "Muphrid"]],
    "Pegasus": [["Markab", "Scheat", "Alpheratz", "Algenib", "Markab"], ["Alpheratz", "Mirach", "Almach"]],
    "Sagittarius": [["KausAustralis", "KausMedia", "KausBorealis", "PhiSgr", "Nunki", "TauSgr", "Ascella", "KausAustralis"], ["Ascella", "PhiSgr"]],
    "Auriga": [["Capella", "Menkalinan", "Elnath", "Hassaleh", "Capella"]],
    "Perseus": [["Mirfak", "Algol"]],
}

names = list(STARS)
index = {name: i for i, name in enumerate(names)}
segments = []
for figure, paths in FIGURES.items():
    for path in paths:
        for a, b in zip(path, path[1:]):
            segments.append((index[a], index[b]))


def direction(star):
    ra_h, ra_m, dec_d, dec_m, _, _ = star
    ra = math.radians((ra_h + ra_m / 60.0) * 15.0)
    sign = -1.0 if dec_d < 0 or (dec_d == 0 and dec_m < 0) else 1.0
    dec = math.radians(sign * (abs(dec_d) + abs(dec_m) / 60.0))
    return (math.cos(dec) * math.cos(ra), math.cos(dec) * math.sin(ra), math.sin(dec))


out = [
    "// Generated by Tools/Sky/make_constellations.py -- do not edit by hand.",
    "// Bright stars as equatorial unit vectors (x toward RA 0h, z toward the north celestial pole) with",
    "// their magnitude in w, the warmth of each (0 blue-white .. 1 red), and the line figures joining them.",
    f"const STAR_COUNT : u32 = {len(names)}u;",
    f"const SEGMENT_COUNT : u32 = {len(segments)}u;",
    f"var<private> STAR_DIRECTIONS : array<vec4<f32>, {len(names)}> = array<vec4<f32>, {len(names)}>(",
    ",\n".join("    vec4<f32>(%.5f, %.5f, %.5f, %.2f)" % (*direction(STARS[n]), STARS[n][4]) for n in names),
    ");",
    f"var<private> STAR_WARMTH : array<f32, {len(names)}> = array<f32, {len(names)}>(",
    ", ".join("%.2f" % STARS[n][5] for n in names),
    ");",
    f"var<private> SEGMENTS : array<vec2<u32>, {len(segments)}> = array<vec2<u32>, {len(segments)}>(",
    ",\n".join(f"    vec2<u32>({a}u, {b}u)" for a, b in segments),
    ");",
]
target = Path(__file__).resolve().parents[2] / "ManaGraphics/Assets/Shaders/WebGpu/include/constellations.wgsl"
target.write_text("\n".join(out) + "\n", encoding="utf-8")
print(f"{len(names)} stars, {len(segments)} segments -> {target}")
