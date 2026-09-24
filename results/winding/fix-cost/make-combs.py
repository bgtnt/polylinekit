import json
from pathlib import Path

rows=[]
for teeth in (32, 128):
    width=2*teeth
    height=width+1
    first=[{'X':0,'Y':0},{'X':width,'Y':0},{'X':width,'Y':height}]
    y=height
    for x in range(width-1,-1,-1):
        first.append({'X':x,'Y':y})
        if x:
            y=1 if y==height else height
            first.append({'X':x,'Y':y})
    second=[{'X':p['Y']+.25,'Y':p['X']+.25} for p in first]
    rows.append({'Name':'filled-regions','Vertices':len(first),'First':first,'Second':second,
                 'Definition':'Two simple perpendicular combs offset by one quarter; no collinear overlap',
                 'ExpectedOwnArea':width+teeth*(height-1)})
(Path(__file__).resolve().parent/'combs.json').write_text(json.dumps(rows,indent=2)+'\n',encoding='utf-8')
