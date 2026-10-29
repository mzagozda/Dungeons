"""Summarise real frame intervals and renderer counters from the M3 capture CSV."""
import argparse
import csv
import json
import math
import pathlib

parser = argparse.ArgumentParser()
parser.add_argument("csv", type=pathlib.Path)
parser.add_argument("--output", type=pathlib.Path)
parser.add_argument("--mobile", action="store_true")
args = parser.parse_args()
with args.csv.open(encoding="utf-8-sig", newline="") as stream:
    rows = list(csv.DictReader(stream))
if len(rows) < 60:
    raise SystemExit("At least 60 sampled frames are required")
times = sorted(float(row["frameMs"]) for row in rows)
percentile = lambda p: times[math.ceil(len(times) * p) - 1]
result = {"capture": str(args.csv), "frames": len(rows), "seconds": sum(times) / 1000,
          "p50Ms": percentile(.5), "p95Ms": percentile(.95), "p99Ms": percentile(.99),
          "maxDrawCalls": max(int(row["drawCalls"]) for row in rows), "maxTriangles": max(int(row["triangles"]) for row in rows),
          "maxTextureBytes": max(int(row["textureBytes"]) for row in rows), "maxEnabledLights": max(int(row["enabledLights"]) for row in rows)}
result["budgetsPassed"] = result["maxDrawCalls"] <= (150 if args.mobile else 300) and result["maxTriangles"] <= 300000 and result["maxEnabledLights"] <= 8
result["frameBudgetPassed"] = result["p95Ms"] <= 16.6 and result["p99Ms"] <= 33
for field in ("renderCpuMs", "renderGpuMs"):
    if field in rows[0]:
        samples = sorted(float(row[field]) for row in rows)
        result[field + "P95"] = samples[math.ceil(len(samples) * .95) - 1]
if args.mobile:
    result["budgetsPassed"] = result["budgetsPassed"] and result["maxTextureBytes"] <= 96 * 1024 * 1024
output = json.dumps(result, indent=2) + "\n"
if args.output:
    args.output.write_text(output, encoding="utf-8")
print(output, end="")
if not result["budgetsPassed"] or not result["frameBudgetPassed"]:
    raise SystemExit(1)
