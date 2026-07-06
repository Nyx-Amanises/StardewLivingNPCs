#!/usr/bin/env python3
"""权属地图：对比当前 ValleyTalk 与上游 dandm1/ValleyTalk 全部历史，
逐文件计算仍属上游表达的行占比，分类 MINE / MIXED / UPSTREAM。

用法:
  python tools/ownership_map.py [--upstream ../upstream-ValleyTalk] [--out RewriteSpec]

语料口径：上游仓库所有提交的所有 .cs/.json/.txt/.md blob 的"实质行"
（去首尾空白后长度>=12，排除纯符号/using/namespace 等样板行）。
当前文件的每一实质行若出现在上游语料中，计为上游行。
这样能捕捉被移动/改名的代码，比按路径 diff 更保守（对我们不利=安全）。
"""
import argparse
import json
import os
import subprocess
import sys
from collections import defaultdict

BOILERPLATE_PREFIXES = (
    "using ", "namespace ", "#region", "#endregion", "#if", "#else", "#endif",
    "// ---", "/*", "*/",
)
EXTS = {".cs", ".json", ".txt", ".md"}
SKIP_DIRS = {"bin", "obj", ".git"}


def substantive(line: str) -> str | None:
    s = line.strip()
    if len(s) < 12:
        return None
    if s.startswith(BOILERPLATE_PREFIXES):
        return None
    # 纯括号/分号/单词行不算表达
    if all(c in "{}();,[] " for c in s):
        return None
    return s


def build_upstream_corpus(upstream: str) -> set[str]:
    out = subprocess.run(
        ["git", "-C", upstream, "rev-list", "--objects", "--all"],
        capture_output=True, text=True, check=True,
    ).stdout
    blobs = []
    for ln in out.splitlines():
        parts = ln.split(" ", 1)
        if len(parts) == 2 and os.path.splitext(parts[1])[1].lower() in EXTS:
            blobs.append(parts[0])
    blobs = list(dict.fromkeys(blobs))
    corpus: set[str] = set()
    BATCH = 200
    for i in range(0, len(blobs), BATCH):
        batch = blobs[i:i + BATCH]
        p = subprocess.run(
            ["git", "-C", upstream, "cat-file", "--batch"],
            input="\n".join(batch), capture_output=True, text=True,
            encoding="utf-8", errors="replace",
        )
        for ln in p.stdout.splitlines():
            # cat-file --batch 头行形如 "<sha> blob <size>"，跳过
            if len(ln) > 45 and ln[:40].isalnum() and " blob " in ln[:52]:
                continue
            s = substantive(ln)
            if s:
                corpus.add(s)
    return corpus


def classify(ratio: float, total: int) -> str:
    if total == 0:
        return "EMPTY"
    if ratio >= 0.60:
        return "UPSTREAM"
    if ratio <= 0.10:
        return "MINE"
    return "MIXED"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--upstream", default=os.path.join("..", "upstream-ValleyTalk"))
    ap.add_argument("--target", default="ValleyTalk")
    ap.add_argument("--out", default="RewriteSpec")
    args = ap.parse_args()

    print("building upstream corpus ...", file=sys.stderr)
    corpus = build_upstream_corpus(args.upstream)
    print(f"corpus lines: {len(corpus)}", file=sys.stderr)

    upstream_paths = set()
    for root, dirs, files in os.walk(args.upstream):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for f in files:
            rel = os.path.relpath(os.path.join(root, f), args.upstream)
            upstream_paths.add(rel.replace("\\", "/"))

    rows = []
    for root, dirs, files in os.walk(args.target):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for f in files:
            if os.path.splitext(f)[1].lower() not in EXTS:
                continue
            path = os.path.join(root, f)
            rel = os.path.relpath(path, args.target).replace("\\", "/")
            try:
                with open(path, encoding="utf-8", errors="replace") as fh:
                    lines = fh.readlines()
            except OSError:
                continue
            subs = [s for s in (substantive(l) for l in lines) if s]
            hit = sum(1 for s in subs if s in corpus)
            total = len(subs)
            ratio = hit / total if total else 0.0
            rows.append({
                "file": rel,
                "lines": total,
                "upstream_lines": hit,
                "upstream_ratio": round(ratio, 3),
                "path_in_upstream": rel in upstream_paths or f"src/{rel}" in upstream_paths,
                "class": classify(ratio, total),
            })

    rows.sort(key=lambda r: (-r["upstream_ratio"], r["file"]))
    os.makedirs(args.out, exist_ok=True)
    with open(os.path.join(args.out, "ownership_map.json"), "w", encoding="utf-8") as fh:
        json.dump(rows, fh, ensure_ascii=False, indent=1)

    by_cls = defaultdict(lambda: [0, 0])
    for r in rows:
        by_cls[r["class"]][0] += 1
        by_cls[r["class"]][1] += r["lines"]

    md = ["# ValleyTalk 权属地图", "",
          f"对照基线：上游全部 git 历史（所有版本的实质行语料，共 {len(corpus)} 行）。",
          "`upstream_ratio` = 当前文件实质行中能在上游任意版本找到的比例。",
          "分类阈值：>=60% UPSTREAM（按重写处理），<=10% MINE（可直接搬运），其余 MIXED（默认重写，个案甄别）。",
          "", "## 汇总", "", "| 分类 | 文件数 | 实质行数 |", "|---|---|---|"]
    for cls in ("UPSTREAM", "MIXED", "MINE", "EMPTY"):
        c, l = by_cls.get(cls, (0, 0))
        md.append(f"| {cls} | {c} | {l} |")
    md += ["", "## 逐文件明细", "",
           "| 文件 | 实质行 | 上游行 | 占比 | 上游有同路径 | 分类 |", "|---|---|---|---|---|---|"]
    for r in rows:
        md.append(f"| {r['file']} | {r['lines']} | {r['upstream_lines']} | "
                  f"{r['upstream_ratio']:.0%} | {'是' if r['path_in_upstream'] else '否'} | {r['class']} |")
    with open(os.path.join(args.out, "02-ownership-map.md"), "w", encoding="utf-8") as fh:
        fh.write("\n".join(md) + "\n")
    print(f"written: {args.out}/02-ownership-map.md, ownership_map.json", file=sys.stderr)


if __name__ == "__main__":
    main()
