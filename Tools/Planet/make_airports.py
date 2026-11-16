"""Writes Assets/GeoData/airports.csv: the large airports with scheduled service from OurAirports (public domain),
trimmed to what the flight traffic's airport rings need -- IATA code, city, latitude, longitude.

    python Tools/Planet/make_airports.py
"""
import csv
import io
import pathlib
import urllib.request

SOURCE = "https://davidmegginson.github.io/ourairports-data/airports.csv"
TARGET = pathlib.Path(__file__).resolve().parents[2] / "Assets" / "GeoData" / "airports.csv"


def main():
    with urllib.request.urlopen(SOURCE) as reply:
        rows = list(csv.DictReader(io.StringIO(reply.read().decode("utf-8"))))
    kept = [r for r in rows if r["type"] == "large_airport" and r["scheduled_service"] == "yes" and r["iata_code"]]
    kept.sort(key=lambda r: r["iata_code"])
    with open(TARGET, "w", newline="", encoding="utf-8") as out:
        writer = csv.writer(out, lineterminator="\n")
        writer.writerow(["iata", "city", "latitude", "longitude"])
        for r in kept:
            writer.writerow([r["iata_code"], r["municipality"], f"{float(r['latitude_deg']):.4f}", f"{float(r['longitude_deg']):.4f}"])
    print(f"{len(kept)} airports -> {TARGET}")


if __name__ == "__main__":
    main()
