# Frozen recognition quality

Counts are query × template-bank-seed trials. Failures stay in accuracy denominators.
Unsupported records are excluded only from supported accuracy, and remain in all-test accuracy.

## dollar

| Method | Supported accuracy | All-test accuracy | Correct / all trials | Failed | Unsupported |
|---|---:|---:|---:|---:|---:|
| rms | 97.146% | 97.146% | 13989/14400 | 0 | 0 |
| area | 85.319% | 85.319% | 12286/14400 | 0 | 0 |
| combined | 97.146% | 97.146% | 13989/14400 | 0 | 0 |
| protractor | 94.500% | 94.500% | 13608/14400 | 0 | 0 |

| Candidate versus RMS | Corrected errors | Introduced errors | Neither correct (all) | Unsupported pairs | Supported change | All-test change |
|---|---:|---:|---:|---:|---:|---:|
| areaMinusRms | 234 | 1937 | 177 | 0 | -11.826 pp (14400 trials) | -11.826 pp (14400 trials) |
| combinedMinusRms | 0 | 0 | 411 | 0 | +0.000 pp (14400 trials) | +0.000 pp (14400 trials) |

‘Neither correct’ includes unsupported pairs and failed queries; it is not a count of classified errors alone.

Primary combined−RMS writer-mean difference: **+0.000 pp**; paired 95% writer-block bootstrap interval **[+0.000, +0.000] pp**.
Ten writers; 10,000 resamples with seed 424242. Repeated banks remain inside each writer block.

| Writer | RMS | Combined | Difference |
|---|---:|---:|---:|
| s02 | 93.472% | 93.472% | +0.000 pp |
| s03 | 98.264% | 98.264% | +0.000 pp |
| s04 | 97.431% | 97.431% | +0.000 pp |
| s05 | 95.278% | 95.278% | +0.000 pp |
| s06 | 98.681% | 98.681% | +0.000 pp |
| s07 | 98.819% | 98.819% | +0.000 pp |
| s08 | 95.347% | 95.347% | +0.000 pp |
| s09 | 98.611% | 98.611% | +0.000 pp |
| s10 | 98.125% | 98.125% | +0.000 pp |
| s11 | 97.431% | 97.431% | +0.000 pp |

Class/writer/speed confusion counts, exact ties and closest score margins are in the accompanying JSON.

## pendigits

| Method | Supported accuracy | All-test accuracy | Correct / all trials | Failed | Unsupported |
|---|---:|---:|---:|---:|---:|
| rms | 84.329% | 66.152% | 6942/10494 | 0 | 2262 |
| area | 72.631% | 56.975% | 5979/10494 | 0 | 2262 |
| combined | 85.435% | 67.019% | 7033/10494 | 0 | 2262 |
| protractor | 83.005% | 65.113% | 6833/10494 | 0 | 2262 |
| dtw | 89.043% | 69.849% | 7330/10494 | 0 | 2262 |

| Candidate versus RMS | Corrected errors | Introduced errors | Neither correct (all) | Unsupported pairs | Supported change | All-test change |
|---|---:|---:|---:|---:|---:|---:|
| areaMinusRms | 664 | 1627 | 2888 | 2262 | -11.698 pp (8232 trials) | -9.177 pp (10494 trials) |
| combinedMinusRms | 229 | 138 | 3323 | 2262 | +1.105 pp (8232 trials) | +0.867 pp (10494 trials) |

‘Neither correct’ includes unsupported pairs and failed queries; it is not a count of classified errors alone.

Descriptive per-bank results only; no verified per-record writer IDs, no writer bootstrap or independent-seed confidence interval

| Method | Seed | Supported accuracy | All-test accuracy |
|---|---:|---:|---:|
| rms | 1729 | 83.601% | 65.580% |
| rms | 2718 | 85.933% | 67.410% |
| rms | 31415 | 83.455% | 65.466% |
| area | 1729 | 73.324% | 57.519% |
| area | 2718 | 71.538% | 56.118% |
| area | 31415 | 73.032% | 57.290% |
| combined | 1729 | 84.111% | 65.981% |
| combined | 2718 | 86.261% | 67.667% |
| combined | 31415 | 85.933% | 67.410% |
| protractor | 1729 | 80.175% | 62.893% |
| protractor | 2718 | 85.241% | 66.867% |
| protractor | 31415 | 83.601% | 65.580% |
| dtw | 1729 | 88.593% | 69.497% |
| dtw | 2718 | 90.707% | 71.155% |
| dtw | 31415 | 87.828% | 68.897% |

Class/writer/speed confusion counts, exact ties and closest score margins are in the accompanying JSON.

