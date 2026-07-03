"""One-off audit: compare string literals before/after the PromptFragments refactor.

For every file touched by the refactor, extract all double-quoted literal pieces from
the git HEAD version and from the working tree (including the two new files), then diff
the two multisets. Interpolation holes changed shape (this.X -> state.X etc.), so only
the literal text pieces are compared — those are what determine prompt output bytes.
"""
import re
import subprocess
import sys
from collections import Counter
from pathlib import Path

REPO = Path(r"C:\Users\雪\Desktop\星露谷物语\StardewLivingNPCs")

EDITED = [
    "LivingNPCs/Behavior/Models/LivingNpcState.cs",
    "LivingNPCs/Behavior/Models/BehaviorMemoryModels.cs",
    "LivingNPCs/Behavior/Memory/BehaviorPromptContextBuilder.cs",
    "LivingNPCs/Behavior/Memory/MemoryRecallService.cs",
    "LivingNPCs/Behavior/Runtime/ValleyTalkContextService.cs",
    "LivingNPCs/Behavior/Runtime/ConversationStartRecorder.cs",
    "LivingNPCs/Behavior/Runtime/CompanionOutingRuntime.cs",
    "LivingNPCs/Behavior/Runtime/DialogueBehaviorInfluenceRuntime.cs",
    "LivingNPCs/Behavior/BehaviorDiagnostics.cs",
    "LivingNPCs/Behavior/AiBehaviorClient.cs",
]
NEW = [
    "LivingNPCs/Behavior/Prompts/PromptFragments.cs",
    "LivingNPCs/Behavior/Diagnostics/StateDebugLabels.cs",
]

# Quoted piece with backslash escapes; stops at unescaped quote. Interpolation holes
# split pieces naturally because `{expr}` sits between quoted spans in the token stream
# only for holes containing strings; plain holes stay inside the piece, so also split on
# balanced {...} afterwards.
STRING_RE = re.compile(r'"((?:[^"\\]|\\.)*)"')
HOLE_RE = re.compile(r"\{[^{}\"]*\}")


def pieces(source: str) -> Counter:
    out = Counter()
    for match in STRING_RE.finditer(source):
        text = match.group(1)
        for part in HOLE_RE.split(text):
            part = part.strip()
            if len(part) >= 8 and re.search(r"[A-Za-z一-鿿]", part):
                out[part] += 1
    return out


def head_version(rel: str) -> str:
    result = subprocess.run(
        ["git", "show", f"HEAD:{rel}"],
        cwd=REPO, capture_output=True, text=True, encoding="utf-8",
    )
    return result.stdout if result.returncode == 0 else ""


old_counts = Counter()
new_counts = Counter()
for rel in EDITED:
    old_counts += pieces(head_version(rel))
    new_counts += pieces((REPO / rel).read_text(encoding="utf-8"))
for rel in NEW:
    new_counts += pieces((REPO / rel).read_text(encoding="utf-8"))

missing = {t: (old_counts[t], new_counts[t]) for t in old_counts if new_counts[t] < old_counts[t]}
added = {t: (old_counts[t], new_counts[t]) for t in new_counts if old_counts[t] < new_counts[t]}

print(f"old pieces: {sum(old_counts.values())} distinct {len(old_counts)}")
print(f"new pieces: {sum(new_counts.values())} distinct {len(new_counts)}")
print(f"\n--- pieces LOST or reduced ({len(missing)}) ---")
for text, (before, after) in sorted(missing.items()):
    print(f"  [{before}->{after}] {text!r}")
print(f"\n--- pieces ADDED or increased ({len(added)}) ---")
for text, (before, after) in sorted(added.items()):
    print(f"  [{before}->{after}] {text!r}")

sys.exit(0)
