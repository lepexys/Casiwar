import csv, sys, os, statistics as st
from collections import defaultdict

d = sys.argv[1]
out = open(os.path.join(d, 'report.txt'), 'w', encoding='utf-8')
def P(*a):
    print(*a, file=out)

def rows(name):
    with open(os.path.join(d, name), encoding='utf-8') as f:
        return list(csv.DictReader(f))

runs = rows('runs.csv')
turns = rows('turns.csv')
flows = rows('flows.csv')
buys = rows('buys.csv')
avail = rows('available.csv')
battles = rows('battles.csv')
cat = rows('catalog.csv')
wants = rows('wants.csv')
N = len(runs)
P(f'Забегов: {N}')
for r in runs: P('  ', r)

def med(xs): return st.median(xs) if xs else float('nan')
def mean(xs): return sum(xs)/len(xs) if xs else float('nan')

# ---------- доход по ходам и источникам ----------
# runs alive per turn
alive = defaultdict(set)
for t in turns: alive[int(t['turn'])].add(t['run'])
inc = defaultdict(lambda: defaultdict(float))  # (turn)->(label,res)->sum
for f in flows:
    t = int(f['turn'])
    if f['label'] == 'start' or t > 50: continue
    for res in ('gold', 'prod', 'food', 'know'):
        inc[t][(f['label'], res, 'in')] += int(f[res + '_in'])
        inc[t][(f['label'], res, 'out')] += int(f[res + '_out'])

buckets = [(1, 5), (6, 10), (11, 15), (16, 20), (21, 30), (31, 40), (41, 50)]
labels = ['rain', 'battle', 'loot', 'endturn', 'recruit', 'army', 'spend']
P('\n=== Доход за ход (среднее по живым забегам), по источникам ===')
for res in ('gold', 'prod', 'food', 'know'):
    P(f'\n[{res}]  ход | ' + ' | '.join(labels) + ' | итого_in | траты(spend)')
    for a, b in buckets:
        n = sum(len(alive[t]) for t in range(a, b + 1))
        if n == 0: continue
        vals = [sum(inc[t][(l, res, 'in')] for t in range(a, b + 1)) / n for l in labels]
        spend = sum(inc[t][('spend', res, 'out')] for t in range(a, b + 1)) / n
        P(f'  {a:>2}-{b:<2} | ' + ' | '.join(f'{v:5.1f}' for v in vals) + f' | {sum(vals):5.1f} | {spend:5.1f}')

# ---------- балансы и население ----------
P('\n=== Конец хода: медианы по забегам ===')
P('ход | жит | 🪙 | ⚒ | 🍎 | 📖 | 📖/ход | бойцов | зданий | наук | улучш.')
byturn = defaultdict(list)
for t in turns: byturn[int(t['turn'])].append(t)
for t in [1, 3, 5, 8, 10, 12, 15, 20, 25, 30, 35, 40, 45, 50]:
    rs = byturn.get(t, [])
    if not rs: continue
    g = lambda k: med([int(r[k]) for r in rs])
    P(f'{t:>3} | {g("pop"):>3} | {g("gold"):>4} | {g("prod"):>4} | {g("food"):>3} | {g("know"):>3} | {g("kpd"):>3} | {g("units"):>3} | {g("buildings"):>3} | {g("techs"):>3} | {g("upgrades"):>3}  (n={len(rs)})')

# ---------- накопленный доход vs цены ----------
P('\n=== Накопленный доход (среднее) против цены всего каталога ===')
cum = defaultdict(lambda: defaultdict(float))
for f in flows:
    t = int(f['turn'])
    if f['label'] in ('start',): continue
    for res in ('gold', 'prod', 'know'):
        cum[f['run']][(t, res)] += int(f[res + '_in'])
for res, kinds in (('gold', ('guild', 'cityup')), ('prod', ('build', 'wonder')), ('know', ('tech',))):
    total = sum(int(c['cost']) for c in cat if c['kind'] in kinds)
    line = []
    for T in (10, 20, 30, 40, 50):
        vals = [sum(v for (t, r), v in cum[run].items() if r == res and t <= T) for run in cum if any(int(x['turn']) >= T for x in turns if x['run'] == run)]
        line.append(f'к {T}: {mean(vals):.0f}')
    P(f'  {res}: каталог = {total}; заработано ' + ', '.join(line))
for kind in ('guild', 'cityup', 'build', 'wonder', 'tech'):
    cs = [int(c['cost']) for c in cat if c['kind'] == kind]
    P(f'  {kind}: {len(cs)} шт., сумма {sum(cs)}, мин {min(cs)}, макс {max(cs)}')

# ---------- по предметам ----------
title = {c['key']: c['title'] for c in cat}
cost = {c['key']: int(c['cost']) for c in cat}
kindOf = {c['key']: c['kind'] for c in cat}
av = defaultdict(dict)
for a in avail: av[a['key']][a['run']] = int(a['turn'])
bought = defaultdict(dict)
waits = defaultdict(list)
for b in buys:
    if b['key'] not in bought or b['run'] not in bought[b['key']]:
        bought[b['key']][b['run']] = int(b['turn'])
    waits[b['key']].append(int(b['turn']) - int(b['picked']))
# доступно мог отметиться позже покупки (открылось и куплено в тот же ход) — берём минимум
for k, runs_b in bought.items():
    for r, t in runs_b.items():
        if r not in av[k] or av[k][r] > t: av[k][r] = t
# средний доход золота за ход около хода t
def gold_rate(t):
    lo, hi = max(1, t - 2), min(50, t + 2)
    n = sum(len(alive[x]) for x in range(lo, hi + 1))
    s = sum(inc[x][(l, 'gold', 'in')] for x in range(lo, hi + 1) for l in labels)
    return s / n if n else float('nan')
def res_rate(t, res):
    lo, hi = max(1, t - 2), min(50, t + 2)
    n = sum(len(alive[x]) for x in range(lo, hi + 1))
    s = sum(inc[x][(l, res, 'in')] for x in range(lo, hi + 1) for l in labels)
    return s / n if n else float('nan')

for kinds, res, name in ((('guild', 'cityup'), 'gold', 'Улучшения за 🪙'), (('build', 'wonder'), 'prod', 'Постройки за ⚒'), (('tech',), 'know', 'Наука за 📖')):
    P(f'\n=== {name}: доступно (забегов, медиана хода) → куплено (забегов, медиана хода), ждали после выбора, цена в ходах дохода ===')
    items = [c for c in cat if c['kind'] in kinds]
    items.sort(key=lambda c: (med(list(av[c['key']].values())) if av[c['key']] else 999))
    for c in items:
        k = c['key']
        a = av[k]; b = bought[k]
        at = med(list(a.values())) if a else None
        bt = med(list(b.values())) if b else None
        rate = res_rate(int(at), res) if at else float('nan')
        P(f"  {c['title'][:22]:<22} {c['kind']:<6} {int(c['cost']):>3} | дост {len(a):>2}/{N} ход {at if at is not None else '-':>4} | купл {len(b):>2}/{N} ход {bt if bt is not None else '-':>4} | ждали {med(waits[k]) if waits[k] else '-':>4} | = {int(c['cost'])/rate if rate == rate and rate > 0 else float('nan'):4.1f} ходов дохода")

# ---------- сводка по забегам ----------
P('\n=== Сводка по забегам ===')
techs_total = sum(1 for c in cat if c['kind'] == 'tech')
done_turn = []
for r in runs:
    ts = [int(t['turn']) for t in turns if t['run'] == r['run'] and int(t['techs']) >= techs_total]
    done_turn.append(min(ts) if ts else None)
P(f"  вся наука изучена к ходу: {sorted(x for x in done_turn if x)} (не изучена в {sum(1 for x in done_turn if not x)} забегах)")
for T in (10, 20, 30, 40, 50):
    up = [sum(1 for b in buys if b['run'] == r['run'] and b['kind'] in ('guild', 'cityup') and int(b['turn']) <= T) for r in runs]
    bl = [sum(1 for b in buys if b['run'] == r['run'] and b['kind'] in ('build', 'wonder', 'booster') and int(b['turn']) <= T) for r in runs]
    P(f"  к ходу {T}: улучшений за 🪙 куплено медиана {med(up)} (мин {min(up)}, макс {max(up)}) из 47; построек и чудес {med(bl)}")
last = {}
for t in turns:
    if int(t['turn']) == 50: last[t['run']] = t
if last:
    P(f"  на 50-м ходу в запасе: 🪙 {med([int(t['gold']) for t in last.values()])}, ⚒ {med([int(t['prod']) for t in last.values()])}, 📖 {med([int(t['know']) for t in last.values()])}, жителей {med([int(t['pop']) for t in last.values()])}")

# ---------- повторяемые ----------
P('\n=== Прочие траты (всего по всем забегам) ===')
other = defaultdict(lambda: [0, 0])
for b in buys:
    if b['kind'] in ('caravan', 'borders', 'cell', 'repair', 'booster'):
        other[b['kind'] + ' ' + b['key']][0] += 1
        other[b['kind'] + ' ' + b['key']][1] += int(b['cost'])
for k, (n, s) in sorted(other.items()): P(f'  {k}: {n} раз, {s} всего ({n / N:.1f}/забег)')

# ---------- бои ----------
P('\n=== Бои ===')
P(f'  всего {len(battles)}, побед {sum(int(b["victory"]) for b in battles)}, на забег {len(battles)/N:.1f}')
for a, b in buckets:
    bs = [x for x in battles if a <= int(x['turn']) <= b]
    if bs: P(f'  ходы {a}-{b}: {len(bs)/N:.2f} боёв/забег, побед {sum(int(x["victory"]) for x in bs)/len(bs):.0%}, врагов {mean([int(x["enemies"]) for x in bs]):.1f}, ★ {mean([int(x["enemy_stars"]) for x in bs]):.1f}, наших до {mean([int(x["our_before"]) for x in bs]):.1f} после {mean([int(x["our_after"]) for x in bs]):.1f}, {mean([float(x["seconds"]) for x in bs]):.0f} с')

# ---------- на что копили (не хватало) ----------
P('\n=== Цели, на которые копили дольше всего (ходов ожидания, все забеги) ===')
wt = defaultdict(int)
for w in wants: wt[(w['res'], w['key'])] += 1
for (res, k), n in sorted(wt.items(), key=lambda x: -x[1])[:25]:
    P(f'  {res} {title.get(k, k)[:26]:<26} {cost.get(k, "?")}: {n / N:.1f} ходов/забег')
out.close()
print(open(os.path.join(d, 'report.txt'), encoding='utf-8').read())
