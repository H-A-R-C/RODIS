# Accepted differences between RODIS and Fortran STEDI v1.2

RODIS reproduces Fortran STEDI v1.2 (SKM, 2012) closely, but not identically. Five differences remain and are accepted rather than fixed. Three are deliberate modelling choices in RODIS; two are defects in the Fortran executable, where it contradicts the STEDI user manual and RODIS follows the manual.

This note exists so that anyone comparing RODIS output against a legacy STEDI run can tell an expected difference from a real problem.

Everything here was established by comparing 48 scenarios day by day over records of 3,653 to 3,744 days. The figures cited are from that comparison. The STEDI reference outputs themselves are not redistributed with this repository, as STEDI is third-party software; the values quoted below are given so that the findings can be checked by anyone who has access to STEDI v1.2 and runs the same scenario definitions through it.

---

## Summary

| # | Difference | Type | Typical size |
|---|---|---|---|
| 1 | Spill and bypass labelling | RODIS design choice | Reallocation only; no change to flow at the outlet |
| 2 | Winterfill pumping timing | RODIS design choice | ~0.1–2% of pumped volume |
| 3 | Day-1 demand timing | RODIS design choice | ~0.0006–0.002 ML carried in storage |
| 4 | Monthly demand interpolation | Fortran defect | Up to 0.19 ML/day; 6.7 ML carried in storage |
| 5 | Bypass ignored in distribution mode | Fortran defect | Up to 0.63 ML/day; 32 ML carried in storage |

---

## 1. Spill and bypass are labelled differently

**RODIS design choice.**

Where an upstream dam releases water through its low-flow bypass and that water then flows into a downstream dam, the two models disagree about what to call it once it leaves that downstream dam:

- **RODIS** continues to report it as **bypass**, tracked through the network to the outlet.
- **Fortran** re-labels it as **spill** at the downstream dam.

RODIS's treatment is the more honest representation: water released to satisfy a bypass obligation is bypass water, wherever it later travels.

There is a further consequence in the Fortran output. Its `Q-bypass` column reports the **raw volume released** by any dam's bypass, including water that flows into another dam rather than to the catchment outlet, and it excludes that volume from `Q-WithDams`. With a bypass on an upstream dam, Fortran therefore breaks the water-balance identity printed in its own output header:

```
Q-WithDams = Q-spill + Q-bypass + Q-unimpound
```

RODIS satisfies the equivalent identity exactly, on every day of every scenario.

**Worked example** — scenario 37, bypass on the upstream dam of a two-dam series, 6 October 1952:

```
Fortran: spill 0.226  bypass 0.080  unimpound 3.349  withDams 3.574
         spill + bypass + unimpound = 3.655, which is not withDams
RODIS:   spill 0.146  bypass 0.080  local     3.349  downstream 3.574
         spill + bypass + local     = 3.574, which is downstream

Fortran spill 0.226 == RODIS spill 0.146 + bypass 0.080
```

Across the whole run, Fortran spill totals 90.81 ML and RODIS spill plus bypass totals 90.82 ML.

**When it appears:** only where a bypass sits upstream of another dam. With the bypass on the most downstream dam the two labellings coincide exactly. Scenario 39, which places the bypass on the downstream dam, shows no difference at all; scenario 37, identical but for bypass position, shows the difference on 852 days.

**How the comparison handles it:** spill and bypass are reported for information but not asserted. The assertion is carried by the total flow arriving at the outlet from dams, computed as downstream flow minus local catchment inflow. That quantity is invariant to how either model labels the water, so a genuine change in released volume still fails while a pure relabelling does not.

---

## 2. Winterfill pumping is limited by start-of-step storage

**RODIS design choice.**

- **RODIS** limits pumping to the room available at the **start** of the timestep.
- **Fortran** resolves the water balance within the step, topping the dam up using room created **during** the day by evaporation, demand and seepage.

Two consequences follow:

1. When a full dam begins to draw down, RODIS pumps up to one day's volume less than Fortran, and never recovers the shortfall.
2. RODIS will pump into a dam that is already spilling; Fortran will not, because such a dam has no room at the end of the step.

Fortran's treatment is arguably the more physically faithful, since pumping and losses occur concurrently through a day. RODIS's is explicit and avoids an implicit solver, and the effect is small, so the simpler approach was kept deliberately.

**Worked example** — scenario 21, 50 ML dam, 0.015 ML/day pumping, late November 1952:

| Date | Fortran pumped | Fortran storage | RODIS pumped | RODIS storage |
|---|---|---|---|---|
| 25 Nov | 0.0040 | 50.000 | 0.0000 | 49.996 |
| 26 Nov | 0.0120 | 50.000 | 0.0037 | 49.988 |
| 27 Nov | 0.0150 | 49.996 | 0.0115 | 49.981 |
| 28 Nov | 0.0150 | 49.984 | 0.0150 | 49.969 |

RODIS's pumped volume on each day equals the room available at the start of that day. Once both reach the rate cap the offset is fixed, at one day of pumping.

**Typical size:** the difference in total pumped volume over ten years is 0.16% in scenario 21 and 0.35% in scenario 25, rising to 1.98% and 2.19% in the four-dam scenarios 43 and 44. The cost grows with dam count, because each dam drawing down from full can miss its own day.

Note that winterfill water comes from an independent source in both models and is not debited from the stream.

**How the comparison handles it:** the daily pumped volume is reported for information but not asserted, because both models' daily rates lie between zero and the scheme's rate, so a per-day difference can never exceed one day of pumping and such an assertion could never fail. The assertion is placed on the **total** pumped volume for the scenario, bounded at 5%, which a real defect such as pumping in the wrong season or at the wrong rate would exceed.

---

## 3. Demand starts on day 2

**RODIS design choice.**

Fortran extracts demand on the first day of simulation; RODIS begins on the second. Because storage is cumulative, this leaves a small permanent offset in the storage series for the whole run.

**Typical size:** 0.0006 to 0.0024 ML in the current scenarios. It was materially larger before the February days-in-month defect was fixed, and much of what was originally attributed to day-1 timing turned out to be that instead.

**How the comparison handles it:** storage is compared on its **daily change** rather than its level, so a constant carried offset cancels. The carried level offset is reported separately and bounded, so a large systematic storage divergence cannot hide inside the change-based comparison.

---

## 4. Fortran interpolates monthly demand into a daily curve

**Defect in the Fortran executable. RODIS follows the manual.**

The STEDI user manual, section 6.4, states that monthly demand proportions produce a **step function at each change of month**, and figure 6-2 contrasts that with the varying series produced by a demand time series. The twelve values are proportions of **annual** demand and sum to 1.000.

RODIS implements this: each month's daily rate is that month's share of annual demand divided by the actual number of days in the month.

The Fortran executable instead interpolates the twelve values into a smooth daily curve, with an apparent one-month phase lag. The annual total is preserved, but individual months are not.

**Worked example** — scenario 3, 30 ML annual demand:

| Month | Input specifies | Fortran extracts |
|---|---|---|
| January | 6.90 ML | 6.15 ML |
| February | 5.55 ML | 6.68 ML |
| March | 3.84 ML | 7.09 ML |
| April | 0.69 ML | 5.25 ML |

April is the clearest case: the input allocates 0.69 ML and Fortran extracts 5.25 ML, over seven times more. Fortran's demand also peaks around 22 February when the input peak is January.

The manual is version 1.1 (July 2011) and the executable is version 1.20 (September 2011), so the manual slightly predates the binary. An undocumented change to core demand behaviour in a point release seems unlikely, and nothing in the manual foreshadows one.

**Affected scenario:** 3.

**How the comparison handles it:** scenario 3 is registered as a documented Fortran defect with an envelope recording which quantities are expected to differ and by how much. It passes only while it differs in exactly the documented way, so a new or worsening problem in the same scenario is still detected.

**If you need bit-comparable legacy runs**, this behaviour would have to be reverse-engineered from Fortran output and implemented behind an explicit compatibility flag. It has not been done, because it would mean reproducing behaviour that contradicts its own documentation.

---

## 5. Fortran ignores the bypass override in distribution mode

**Defect in the Fortran executable. RODIS follows the manual.**

Where dams are entered as a **volume distribution** rather than individually, the STEDI user manual section 5.1 lists low-flow bypasses among the available options, and section 4.4 defines the three details required: the season of operation, a capacity in ML/day/km² when calculating from dam catchment area, and a minimum dam size.

Scenario 48 supplies all three: 0.1 ML/day/km², 1 July to 31 October, dams above 10 ML.

RODIS applies the bypass as specified. Over ten years:

| | Days with bypass | Total bypass volume |
|---|---|---|
| Fortran | 0 | 0.00 ML |
| RODIS | 1,230 | 196.80 ML |

RODIS's figure is arithmetically correct for the specification: the volume distribution yields one 9 ML dam and two 50 ML dams, each 50 ML dam draws 0.8 km², and 0.1 ML/day/km² × 0.8 km² × 2 dams is 0.160 ML/day. The 9 ML dam is below the 10 ML threshold and correctly receives none.

Fortran's own water balance closes with `Q-bypass = 0` on every one of 3,653 days, so the release is genuinely absent rather than merely unreported.

**Affected scenario:** 48. Note that Fortran applies bypasses correctly when dams are entered **individually**, so this is specific to distribution mode.

**How the comparison handles it:** as for scenario 3, registered as a documented defect with an envelope.

---

## Reproducing these findings

The scenario definitions in `SimpleTests` are the same files used for the comparison. Running them through STEDI v1.2 and through RODIS, then comparing the daily output, reproduces every figure quoted above. The comparison harness in `Core.Tests` will do this automatically if STEDI `.fdy` outputs are placed in each scenario's `1_OldSTEDI_outputs` directory; see the README for details.

---

## References

- Sinclair Knight Merz (2011) *STEDI: Spatial Tool for the Estimation of Dam Impacts, User Manual*, version 1.1, July 2011.
- Nathan R, Jordan P and Morden R (2005) 'Assessing the impact of farm dams on streamflows, Part I: Development of simulation tools', *Australasian Journal of Water Resources*, 9(1):1–12.
- Fowler K, Morden R, Lowe L and Nathan R (2015) 'Advances in assessing the impact of hillside farm dams on streamflow', *Australasian Journal of Water Resources*, 19(2):96–108.
