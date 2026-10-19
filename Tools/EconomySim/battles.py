import csv, sys, os, statistics as st
from collections import defaultdict

d = sys.argv[1]
rows = list(csv.DictReader(open(os.path.join(d, 'battles.csv'), encoding='utf-8')))
runs = list(csv.DictReader(open(os.path.join(d, 'runs.csv'), encoding='utf-8')))


def med(xs):
    return st.median(xs) if xs else float('nan')


def mean(xs):
    return sum(xs) / len(xs) if xs else float('nan')


phases = [(1, 15, 'ходы 1-15'), (16, 30, 'ходы 16-30'), (31, 50, 'ходы 31-50'), (1, 50, 'все')]
groups = defaultdict(list)
for r in rows:
    groups[(r['tempo'], r['slow'], r.get('hp', '1'))].append(r)

print('Забеги:', ', '.join(f"{r['run']}:{r['tempo']}/{r['slow']}:{r['result']}@{r['end_turn']}" for r in runs))
for key in sorted(groups, key=lambda k: (float(k[2]), -float(k[0]))):
    g = groups[key]
    print(f'\n=== темп арены {key[0]}, тактика ×{key[1]}, ХП ×{key[2]} ===')
    print('фаза        | боёв | побед | бой, с (медиана / 90%) | рядов | рядов/мин | суперударов | доля урона от рядов | наших пало | тактика, с | лимит')
    for a, b, name in phases:
        bs = [r for r in g if a <= int(r['turn']) <= b]
        if not bs:
            continue
        secs = sorted(float(r['board_s']) for r in bs)
        p90 = secs[int(len(secs) * 0.9) - 1] if len(secs) >= 10 else max(secs)
        matches = [int(r['matches']) for r in bs]
        per_min = [int(r['matches']) / max(1e-3, float(r['board_s'])) * 60 for r in bs]
        bd = sum(float(r['board_dmg']) for r in bs)
        sd = sum(float(r['self_dmg']) for r in bs)
        share = bd / (bd + sd) if bd + sd > 0 else float('nan')
        lost = [int(r['our_before']) - int(r['our_after']) for r in bs]
        slow = [float(r['slow_s']) for r in bs]
        timeouts = sum(1 for r in bs if float(r['board_s']) >= 149)
        wins = sum(int(r['victory']) for r in bs)
        print(f"{name:<11} | {len(bs):>4} | {wins / len(bs):>5.0%} | {med(secs):>6.1f} / {p90:>5.1f}       | {med(matches):>5} | {mean(per_min):>9.1f} | "
              f"{mean([int(r['supers']) for r in bs]):>11.2f} | {share:>19.0%} | {mean(lost):>10.2f} | {mean(slow):>10.1f} | {timeouts}")
