# Offline audit for companion-outing anchors: parses the installed SVE maps (TMX + TBin)
# and checks every authored anchor for out-of-bounds / void / water / blocked tiles.
# Usage: python tools/verify_anchors.py   (set SVE_MAPS_DIR to override the SVE assets path)
# Keep the coordinate lists in sync with CompanionOutingAnchorSelector when anchors change.
import base64
import io
import os
import struct
import sys
import xml.etree.ElementTree as ET
import zlib

SVE = r"D:\SteamLibrary\steamapps\common\Stardew Valley\Mods\Stardew Valley Expanded\[CP] Stardew Valley Expanded\assets\Maps"


class MapData:
    def __init__(self, width, height):
        self.width = width
        self.height = height
        self.back = {}       # (x,y) -> [tile prop dicts]
        self.buildings = {}  # (x,y) -> [tile prop dicts]


def parse_tmx(path):
    tree = ET.parse(path)
    root = tree.getroot()
    width, height = int(root.get("width")), int(root.get("height"))
    data = MapData(width, height)

    # per-gid properties from embedded tilesets
    gid_props = {}
    tilesets = []
    for ts in root.findall("tileset"):
        firstgid = int(ts.get("firstgid"))
        count = int(ts.get("tilecount", "0"))
        tilesets.append((firstgid, count))
        for tile in ts.findall("tile"):
            tid = firstgid + int(tile.get("id"))
            props = {}
            pr = tile.find("properties")
            if pr is not None:
                for p in pr.findall("property"):
                    props[p.get("name")] = p.get("value", "")
            if props:
                gid_props[tid] = props

    for layer in root.findall("layer"):
        name = layer.get("name", "")
        target = None
        if name.startswith("Back"):
            target = data.back
        elif name.startswith("Buildings"):
            target = data.buildings
        else:
            continue
        d = layer.find("data")
        enc, comp = d.get("encoding"), d.get("compression")
        if enc == "base64":
            raw = base64.b64decode(d.text.strip())
            if comp == "zlib":
                raw = zlib.decompress(raw)
            elif comp == "gzip":
                import gzip
                raw = gzip.decompress(raw)
            gids = struct.unpack("<%di" % (len(raw) // 4), raw)
        elif enc == "csv":
            gids = [int(v) for v in d.text.replace("\n", "").split(",")]
        else:
            raise ValueError(f"encoding {enc}")
        lw = int(layer.get("width"))
        for i, gid in enumerate(gids):
            gid &= 0x0FFFFFFF
            if gid == 0:
                continue
            x, y = i % lw, i // lw
            target.setdefault((x, y), []).append(gid_props.get(gid, {}))
    return data


class TbinReader:
    def __init__(self, blob):
        self.b = blob
        self.i = 0

    def s(self):
        n = struct.unpack_from("<i", self.b, self.i)[0]
        self.i += 4
        v = self.b[self.i:self.i + n].decode("utf-8", "replace")
        self.i += n
        return v

    def i32(self):
        v = struct.unpack_from("<i", self.b, self.i)[0]
        self.i += 4
        return v

    def f32(self):
        v = struct.unpack_from("<f", self.b, self.i)[0]
        self.i += 4
        return v

    def byte(self):
        v = self.b[self.i]
        self.i += 1
        return v

    def props(self):
        out = {}
        for _ in range(self.i32()):
            key = self.s()
            t = self.byte()
            if t == 0:
                out[key] = bool(self.byte())
            elif t == 1:
                out[key] = self.i32()
            elif t == 2:
                out[key] = self.f32()
            elif t == 3:
                out[key] = self.s()
            else:
                raise ValueError(f"prop type {t}")
        return out


def parse_tbin(path):
    blob = open(path, "rb").read()
    if not blob.startswith(b"tBIN10"):
        raise ValueError("not tbin")
    r = TbinReader(blob)
    r.i = 6
    r.s()          # map id
    r.s()          # description
    r.props()      # map properties

    # tilesheets; per-tile props are encoded as "@TileIndex@{n}@{key}" in sheet props
    sheet_tile_props = {}
    for _ in range(r.i32()):
        sid = r.s()
        r.s()  # description
        r.s()  # image source
        r.i32(); r.i32()  # sheet size
        r.i32(); r.i32()  # tile size
        r.i32(); r.i32()  # margin
        r.i32(); r.i32()  # spacing
        p = r.props()
        tp = {}
        for k, v in p.items():
            parts = k.split("@")
            if len(parts) == 4 and parts[1] == "TileIndex":
                tp.setdefault(int(parts[2]), {})[parts[3]] = v
        sheet_tile_props[sid] = tp

    data = None
    layer_count = r.i32()
    for _ in range(layer_count):
        lid = r.s()
        r.byte()  # visible
        r.s()     # description
        w, h = r.i32(), r.i32()
        r.i32(); r.i32()  # tile size px
        r.props()
        if data is None:
            data = MapData(w, h)
        target = None
        if lid.startswith("Back"):
            target = data.back
        elif lid.startswith("Buildings"):
            target = data.buildings

        cur_sheet = ""
        cell = 0
        total = w * h

        def put(idx, sheet, tprops, cell_index):
            if target is None:
                return
            x, y = cell_index % w, cell_index // w
            props = dict(sheet_tile_props.get(sheet, {}).get(idx, {}))
            props.update(tprops)
            target.setdefault((x, y), []).append(props)

        while cell < total:
            m = r.byte()
            if m == 0x54:  # 'T' switch sheet
                cur_sheet = r.s()
            elif m == 0x4E:  # 'N' nulls
                cell += r.i32()
            elif m == 0x53:  # 'S' static
                idx = r.i32()
                r.byte()  # blend
                tp = {k: v for k, v in r.props().items()}
                put(idx, cur_sheet, tp, cell)
                cell += 1
            elif m == 0x41:  # 'A' animated
                r.i32()  # interval
                fc = r.i32()
                first = None
                fsheet = cur_sheet
                for _ in range(fc):
                    fm = r.byte()
                    while fm == 0x54:
                        fsheet = r.s()
                        fm = r.byte()
                    if fm != 0x53:
                        raise ValueError(f"anim frame marker {fm}")
                    fidx = r.i32()
                    r.byte()   # frame blend mode
                    r.props()  # frame properties
                    if first is None:
                        first = (fidx, fsheet)
                tp = r.props()  # the animated tile's own properties
                put(first[0], first[1], tp, cell)
                cell += 1
            else:
                raise ValueError(f"marker {m} at {r.i-1} layer {lid} cell {cell}")
    return data


def truthy(v):
    return v is True or (isinstance(v, str) and v.upper() in ("T", "TRUE"))


def verdict(m, x, y):
    if not (0 <= x < m.width and 0 <= y < m.height):
        return "OOB"
    back = m.back.get((x, y))
    if not back:
        return "VOID"
    notes = []
    if any(truthy(t.get("Water")) for t in back):
        notes.append("WATER")
    blocking = [t for t in m.buildings.get((x, y), []) if not truthy(t.get("Passable")) and not truthy(t.get("Shadow"))]
    if blocking:
        notes.append("BLOCK")
    return "+".join(notes) if notes else "OK"


ANCHORS = {
    # SVE override table -> SVE outdoor tmx
    ("SVE:Town", "Locations/Town.tmx"): [(66, 60), (72, 54), (76, 54), (59, 47), (96, 65), (53, 52)],
    ("SVE:Beach", "Locations/Beach.tmx"): [(43, 35), (88, 34), (70, 24), (63, 14), (56, 10)],
    ("SVE:Forest", "Locations/Forest.tmx"): [(88, 47), (85, 50), (54, 54), (51, 97), (70, 99)],
    ("SVE:Mountain", "Locations/Mountain.tmx"): [(31, 20), (42, 13), (57, 31), (78, 12), (15, 10)],
    ("SVE:BusStop", "Locations/BusStop.tmx"): [(24, 12), (31, 18), (16, 10), (13, 11)],
    # SVE override table -> SVE-replaced interiors
    ("SVE:Saloon", "Locations/Saloon.tbin"): [(11, 20), (13, 18), (20, 17), (26, 18), (12, 18)],
    ("SVE:SeedShop", "Locations/SeedShop.tbin"): [(10, 17), (12, 17), (6, 14)],
    ("SVE:Blacksmith", "Locations/Blacksmith.tbin"): [(7, 14), (12, 13), (5, 17)],
    ("SVE:FishShop", "Locations/FishShop.tbin"): [(5, 4), (7, 8), (2, 8)],
    ("SVE:WizardHouse", "Locations/WizardHouse.tbin"): [(7, 13), (8, 15)],
    ("SVE:Hospital", "Locations/Hospital.tbin"): [(9, 17), (13, 16)],
    ("SVE:JoshHouse", "Locations/JoshHouse.tbin"): [(10, 14), (15, 17)],
    ("SVE:SamHouse", "Locations/SamHouse.tbin"): [(17, 18), (9, 18)],
    ("SVE:ScienceHouse", "Locations/ScienceHouse.tbin"): [(16, 21), (12, 16), (22, 19)],
    ("SVE:LeahHouse", "Locations/LeahHouse.tmx"): [(6, 8), (10, 9)],
    ("SVE:Trailer", "Locations/Trailer.tbin"): [(10, 8), (12, 9), (6, 11)],
    ("SVE:Trailer_big", "Locations/Trailer_big.tbin"): [(10, 8), (12, 9), (6, 11)],
    ("SVE:Mine", "Locations/Mine.tbin"): [(8, 10), (12, 11), (6, 12)],
    # vanilla-table interiors whose coordinates were verified fine on the SVE layouts (no SVE entry)
    ("van:ArchaeologyHouse", "Locations/ArchaeologyHouse.tbin"): [(18, 14), (20, 14), (19, 16), (13, 14), (11, 9), (17, 9)],
    ("van:AnimalShop", "Locations/AnimalShop.tbin"): [(12, 16), (7, 15)],
    ("van:HaleyHouse", "Locations/HaleyHouse.tbin"): [(13, 17), (8, 16)],
    # custom SVE locations (vanilla table, SVE-exclusive maps)
    ("cus:GrampletonCoast", "NewLocations/GrampletonCoast.tmx"): [(38, 18), (56, 30)],
    ("cus:BlueMoonVineyard", "NewLocations/BlueMoonVineyard.tmx"): [(25, 55), (30, 48), (21, 32)],
    ("cus:AuroraVineyard", "NewLocations/AuroraVineyard.tbin"): [(13, 17), (20, 18), (11, 8)],
    ("cus:AuroraVineyardRefurbished", "NewLocations/AuroraVineyardRefurbished.tbin"): [(13, 17), (20, 18), (11, 8)],
    ("cus:ForestWest", "NewLocations/ForestWest.tbin"): [(57, 18), (53, 28), (54, 117)],
    ("cus:SVESummit", "Locations/SVESummit.tmx"): [(11, 13), (14, 17), (23, 23)],
    ("cus:GrandpasShedOutside", "GrandpasShed/GrandpasShedOutside.tbin"): [(8, 8), (12, 12), (6, 14)],
    ("cus:JunimoWoods", "NewLocations/JunimoWoods.tbin"): [(32, 95), (36, 99), (47, 101)],
    ("cus:EnchantedGrove", "NewLocations/EnchantedGrove.tbin"): [(19, 11), (20, 14), (14, 24)],
    # festival anchors on the SVE flower-dance map
    ("fest:FlowerDance", "FestivalMaps/Forest-SVE-FlowerFestival.tmx"): [(15, 33), (20, 35), (32, 38)],
}

for (label, rel), points in ANCHORS.items():
    path = os.path.join(SVE, rel.replace("/", os.sep))
    if not os.path.exists(path):
        print(f"{label:28} MISSING FILE {rel}")
        continue
    try:
        m = parse_tmx(path) if path.endswith(".tmx") else parse_tbin(path)
    except Exception as ex:
        print(f"{label:28} PARSE FAIL: {type(ex).__name__} {ex}")
        continue
    results = [f"({x},{y})={verdict(m, x, y)}" for x, y in points]
    flag = "" if all(r.endswith("=OK") for r in results) else "   <-- CHECK"
    print(f"{label:28} {m.width}x{m.height:<4} {' '.join(results)}{flag}")
