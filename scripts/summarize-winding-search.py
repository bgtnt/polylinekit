import json
from pathlib import Path
from statistics import median

root = Path(__file__).resolve().parents[1] / 'results/winding/search'
names = ['baseline', 'sort', 'bvh', 'simd', 'compact', 'compact-scalar']
data = {}
for name in names:
    runs = [json.loads((root / f'{name}-{i}.json').read_text(encoding='utf-8-sig')) for i in range(1, 4)]
    data[name] = runs
base_rows = data['baseline'][0]['Measurements']
table = ['# Bounds and sorting experiment', '', 'Median of three process medians; complete API calls in microseconds.', '',
         '| Workload | N | Baseline | Natural sort | Packed hierarchy | SIMD alone | Combined | Combined scalar | Combined speedup | SIMD contribution |',
         '| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |']
summary = []
for i, base in enumerate(base_rows):
    values = {}
    for name in names:
        rows = [r['Measurements'][i] for r in data[name]]
        for row in rows:
            assert (row['Name'],row['N']) == (base['Name'],base['N'])
            assert abs(row['Value']-base['Value']) <= 1e-10 * max(1,abs(base['Value'])), (name, row)
            assert all(b == 0 for b in row['SamplesBytes']), (name, row)
        values[name] = median(r['MedianUs'] for r in rows)
    b,s,h,v,c,scalar = (values[name] for name in names)
    table.append(f"| {base['Name']} | {base['N']} | {b:.2f} | {s:.2f} | {h:.2f} | {v:.2f} | {c:.2f} | {scalar:.2f} | {b/c:.2f}× | {scalar/c:.2f}× |")
    summary.append({'Name':base['Name'], 'N':base['N'], **values, 'Speedup': b/c, 'SimdContribution':scalar/c})
(root / 'summary.md').write_text('\n'.join(table)+'\n', encoding='utf-8')
(root / 'summary.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
print('\n'.join(table).replace('×', 'x'))
