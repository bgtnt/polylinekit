"""Validate and summarize the two own-area prototypes; standard library only."""
import json
from pathlib import Path
from statistics import median

root = Path(__file__).resolve().parents[1] / 'results/winding/fix-cost'
lines = ['# Own-area specialization experiments', '',
         'Each row is the median of three fresh-process medians, in microseconds.', '',
         '| Variant / input suite | Workload | N | Combined baseline | Own-area prototype | Baseline / prototype |',
         '| --- | --- | ---: | ---: | ---: | ---: |']
for suite in ('always', 'always-combs', 'adaptive', 'adaptive-combs'):
    runs = {name: [json.loads((root / suite / f'{name}-{i}.json').read_text(encoding='utf-8-sig'))
                   for i in range(1,4)] for name in ('compact','own-area')}
    for i, row in enumerate(runs['compact'][0]['Measurements']):
        pair = []
        for name in ('compact','own-area'):
            rows = [r['Measurements'][i] for r in runs[name]]
            for r in rows:
                assert (r['Name'],r['N']) == (row['Name'],row['N'])
                assert abs(r['Value']-row['Value']) <= 1e-10*max(1,abs(row['Value']))
                assert all(v==0 for v in r['SamplesBytes'])
            pair.append(median(r['MedianUs'] for r in rows))
        a,b=pair
        lines.append(f"| {suite} | {row['Name']} | {row['N']} | {a:.2f} | {b:.2f} | {a/b:.3f}x |")
(root/'summary.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print(root/'summary.md')
