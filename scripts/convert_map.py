#!/usr/bin/env python3
"""
Convert old .txt map format to the new .json format.

Old format (two-layer):
  - Layer 1: ground/terrain encoded as characters
  - "---" separator
  - Layer 2: unit placement encoded as characters
  If no separator, a single layer encodes both ground/terrain and units.

  Characters:
    Ground:  f=Boot  o=Overload  p=Proto  .=Normal
    Terrain: #=Terrain  ~=BreakableWall  ==FragileWall
    Units:   b/B=Builder  w/W=Wall  s/S=Soldier  n/N=Stunner
             lowercase=slot 0, uppercase=slot 1

Usage:
  python convert_map.py file1.txt [file2.txt ...]
  python convert_map.py --dir path/to/maps/
  python convert_map.py --all          # converts every .txt in maps/ and godot/Maps/
"""

import json
import os
import sys
import glob


def parse_ground(c):
    return {"f": "boot", "o": "overload", "p": "proto"}.get(c)


def parse_terrain(c):
    return {"#": "terrain", "~": "breakableWall", "=": "fragileWall"}.get(c)


def parse_block(c):
    # Mobile blocks: lowercase=slot 0, uppercase=slot 1
    result = {
        "b": ("builder", 0, False), "B": ("builder", 1, False),
        "w": ("wall", 0, False),    "W": ("wall", 1, False),
        "s": ("soldier", 0, False), "S": ("soldier", 1, False),
        "n": ("stunner", 0, False), "N": ("stunner", 1, False),
    }.get(c)
    if result:
        return result

    # Rooted blocks: 4 types per player, sequential digits
    # Player 1 (slot 0): 1=builder 2=wall 3=soldier 4=stunner
    # Player 2 (slot 1): 5=builder 6=wall 7=soldier 8=stunner
    if c.isdigit() and "1" <= c <= "8":
        idx = int(c) - 1  # 0-7
        slot = idx // 4
        type_idx = idx % 4
        block_types = ["builder", "wall", "soldier", "stunner"]
        return (block_types[type_idx], slot, True)

    return None


def convert(txt_path):
    with open(txt_path, "r") as f:
        text = f.read()

    lines = [l.rstrip("\r") for l in text.split("\n")]

    # Find "---" separator
    sep = -1
    for i, line in enumerate(lines):
        if line.strip() == "---":
            sep = i
            break

    if sep >= 0:
        ground_lines = [l for l in lines[:sep] if l.strip()]
        unit_lines = [l for l in lines[sep + 1 :] if l.strip()]
    else:
        ground_lines = [l for l in lines if l.strip()]
        unit_lines = ground_lines  # single-layer: same lines for both

    height = len(ground_lines)
    width = max((len(l) for l in ground_lines), default=0)

    ground = []
    terrain = []
    units = []
    max_slot = -1

    # Parse ground + terrain from the ground layer
    for y in range(height):
        line = ground_lines[y]
        for x in range(width):
            c = line[x] if x < len(line) else "."

            g = parse_ground(c)
            if g:
                ground.append({"x": x, "y": y, "type": g})

            t = parse_terrain(c)
            if t:
                terrain.append({"x": x, "y": y, "type": t})

    # Parse units from the unit layer
    for y in range(min(len(unit_lines), height)):
        line = unit_lines[y]
        for x in range(min(len(line), width)):
            c = line[x]
            block = parse_block(c)
            if block:
                btype, slot, rooted = block
                entry = {"x": x, "y": y, "type": btype, "slot": slot}
                if rooted:
                    entry["rooted"] = True
                units.append(entry)
                if slot > max_slot:
                    max_slot = slot

    name = os.path.splitext(os.path.basename(txt_path))[0]
    slot_count = max(max_slot + 1, 2)

    return {
        "meta": {
            "name": name,
            "version": 1,
            "width": width,
            "height": height,
            "slots": slot_count,
        },
        "ground": ground,
        "terrain": terrain,
        "units": units,
    }


def main():
    if len(sys.argv) < 2:
        print(__doc__.strip())
        sys.exit(1)

    files = []
    i = 1
    while i < len(sys.argv):
        arg = sys.argv[i]
        if arg == "--dir":
            if i + 1 >= len(sys.argv):
                print("Error: --dir requires a path", file=sys.stderr)
                sys.exit(1)
            files.extend(sorted(glob.glob(os.path.join(sys.argv[i + 1], "*.txt"))))
            i += 2
        elif arg == "--all":
            # Search common map directories relative to repo root
            repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
            for d in ["maps", "godot/Maps", "godot/Assets/Maps"]:
                files.extend(sorted(glob.glob(os.path.join(repo, d, "*.txt"))))
            i += 1
        elif arg in ("--help", "-h"):
            print(__doc__.strip())
            sys.exit(0)
        else:
            files.append(arg)
            i += 1

    if not files:
        print("No .txt map files found.", file=sys.stderr)
        sys.exit(1)

    for txt_path in files:
        json_path = os.path.splitext(txt_path)[0] + ".json"

        data = convert(txt_path)

        with open(json_path, "w") as f:
            json.dump(data, f, indent=2)
            f.write("\n")

        m = data["meta"]
        print(
            f"{txt_path} -> {json_path}  "
            f"({m['width']}x{m['height']}, "
            f"{len(data['ground'])} ground, {len(data['terrain'])} terrain, "
            f"{len(data['units'])} units, {m['slots']} slots)"
        )

    print(f"\nConverted {len(files)} map(s).")


if __name__ == "__main__":
    main()
