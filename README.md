# RODIS

**Runoff Dam Impact Simulator** is a model for estimating the effects of runoff dams, commonly called farm dams, on streamflow.

RODIS implements runoff-dam water-balance methods derived from the earlier TEDI, CHEAT and STEDI models in modern object-oriented C#. It supports legacy STEDI scenario files as well as RODIS configuration formats. Results are written in Source result-file format and can also be reviewed using general CSV tools.

> **Status:** the legacy-STEDI compatibility pathway has been validated against Fortran STEDI v1.2 across all 48 available test scenarios. The Fortran reference outputs are not redistributed. See [Validation](#validation) and [Accepted differences](ACCEPTED_DIFFERENCES.md).
---

## What RODIS does

Each dam is represented as a storage that intercepts inflow from its local catchment and modifies downstream flow through a daily water balance:

- inflow from the contributing catchment area
- rainfall directly onto the dam surface
- evaporation from the dam surface
- extraction to meet demand
- seepage losses
- spill when storage capacity is exceeded
- bypassed flow, where a low-flow bypass is present
- pumped inflow (winterfill), where a pumping scheme is present

Dams are connected in a network, so spill from an upstream dam can become inflow to a downstream one. The spatial arrangement therefore affects total catchment impact, not just the aggregate storage volume.

RODIS can also work in reverse: given observed (with-dams) flow at a gauge, it back-calculates the unimpacted flow that would have occurred without the dams.

## Key capabilities

- **Dam networks** — series, parallel and branched topologies, resolved by topological sort
- **Two demand models** — repeating monthly proportions, or a daily demand time series
- **Low-flow bypasses** — fixed capacity per dam, or calculated from dam catchment area
- **Winterfill pumping** — seasonal pumped inflow from an independent source
- **Dam generation from a distribution** — dams synthesised from a volume histogram where individual dam details are unavailable
- **Scenario chaining** — several development levels in one run, with results reported separately
- **Legacy STEDI input** — existing `.scn` scenario files run unchanged

## Requirements

- .NET 8.0 SDK or later
- Windows, Linux or macOS

## Versioning

RODIS uses Semantic Versioning in the form `MAJOR.MINOR.PATCH`.

- `MAJOR` changes may break existing APIs, input files or established model behaviour.
- `MINOR` changes add backward-compatible functionality.
- `PATCH` changes contain backward-compatible corrections.

Prerelease versions use suffixes such as `-rc.1`. Build dates and Git commit identifiers may be recorded as build metadata, but do not form part of the public release sequence.

## Building

```
powershell
git clone <repository-url>
cd RODIS
dotnet build
```

## Running

Run a legacy STEDI scenario file:

```
RODIS.Core -RunSTEDILegacyVersion path/to/Scenario01.scn
```

Output paths are taken from the scenario file. RODIS writes:

| File | Contents |
|---|---|
| `<name>.res.csv` | Whole-catchment daily results, Source result format |
| `<name>_<group>.res.csv` | Daily results per reporting group |
| `<name>_NodeData.csv` | Per-dam properties as resolved by the model |
| `<name>.out` | Detailed calculation output |
| `<name>.fdy` | Flow time series |

## Repository layout

```
Core/
├── src/ RODIS.Core source code
├── tests/ Unit and regression tests
├── SimpleTests/ Synthetic test scenarios and RODIS outputs
│ ├── CommonInputs/ Shared climate, flow and demand inputs
│ ├── RunAllScenarios.bat Runs all available scenarios
│ └── ScenarioNN/
│ ├── RODIS_..._ScenarioNN.scn
│ └── RODISOutputs/
├── LICENSE
├── NOTICE
├── THIRD-PARTY-NOTICES.md
├── README.md
└── ACCEPTED_DIFFERENCES.md
```

## Test scenarios

`SimpleTests` contains 48 scenarios covering single dams, dams in series and parallel, branched Y-shaped networks, dams generated from a volume distribution, low-flow bypasses, winterfill pumping, both demand models, and both impacted and unimpacted solve modes.

Each scenario directory holds the scenario definition and the RODIS outputs it produces. The inputs are synthetic and deliberately simple — constant or sinusoidal flow, rainfall and evaporation series — so that model behaviour can be reasoned about directly rather than obscured by real climate variability.

Run them all:

```
cd SimpleTests
RunAllScenarios.bat
```

Each scenario writes to its own `RODISOutputs` directory. The committed outputs were produced by the version of RODIS tagged in this repository, so re-running should reproduce them; a difference indicates a change in model behaviour.

See SimpleTests/TEST_CASES.md for a detailed catalogue of the 48 synthetic scenarios, their input conditions and the model features they exercise.

## Validation

RODIS was validated against Fortran STEDI v1.2 using 48 scenarios and daily records of 3,653 to 3,744 days. Eleven quantities were compared: impact, unimpacted flow, net rainfall, demand, winterfill, spill, bypass, total dam-released flow, storage, local inflow and downstream flow.

### RODIS defects found and corrected

Three defects were found and fixed in RODIS during that validation:

| Defect | Effect | Scope |
|---|---|---|
| Legacy node ordering treated an outlet-first array as upstream-first | All flows 10× too large | All legacy scenarios |
| Demand time series normalised over the complete input file rather than using the mean pattern total across complete calendar years | Mean annual demand approximately 4.1% low in the tested case | Time-series demand scenarios |
| February demand divided by a nominal 28.25 days rather than the actual month length | Demand 0.9% low in a common year, 2.7% high in a leap year | All monthly-demand scenarios |

### Internal consistency and boundary testing

The external comparison is supplemented by self-contained unit and integration tests that do not require the private Fortran reference outputs. These tests verify:

- exact repeating-monthly demand volumes in common and leap years;
- use of the actual 28- or 29-day February length;
- preservation of configured mean annual demand in multi-year time-series patterns;
- preservation of interannual and seasonal demand-pattern shape;
- invariance of results to uniform rescaling of raw time-series pattern values;
- exclusion of partial end years from complete-year normalisation;
- validation of unique, chronological and regularly spaced demand-pattern timestamps;
- explicit tests for omitted and duplicated leap-day records;
- rejection of omitted or duplicated leap-day records;
- rejection of invalid, negative and non-finite demand inputs;
- validation of annual demand factors, dam capacities and monthly scale factors;
- finite and physically bounded reverse-solve results across 29 February 1960; and
- node-level and catchment-level water-balance closure.

These checks directly protect against the calendar, normalisation and invalid-input problems identified during validation against legacy STEDI.

### Reference outputs are not included

The Fortran STEDI outputs used for validation are not redistributed because STEDI is third-party software. This repository contains the scenario definitions and RODIS-generated outputs. Users with lawful access to STEDI v1.2 may reproduce the comparison by running the same scenarios through both models.

The comparison harness is retained in `tests`. The private reference location is configured through the test-path helper and the `RODIS_SIMPLETESTS_ROOT` environment variable. Public test execution does not require the Fortran reference outputs.

## Tests

```
dotnet test
```

Tests are split by category:

```
dotnet test --filter TestCategory=SelfContained            # runs anywhere
dotnet test --filter TestCategory=RequiresSimpleTestsData  # needs reference outputs
```

**`SelfContained`** tests use small inline fixtures written to a temporary folder. They cover the model itself and the comparison logic, and run anywhere including CI. These are the tests that matter for most users.

**`RequiresSimpleTestsData`** tests compare RODIS output against Fortran STEDI reference outputs. Because those reference outputs are not distributed with this repository, these tests report no cases unless reference data is supplied. To use them, place STEDI `.fdy` outputs in each scenario's private `LegacySTEDIOutputs` directory and point the suite at the tree:

```
set RODIS_SIMPLETESTS_ROOT=D:\path\to\SimpleTests
```

A run then writes `LegacyStediRollUp_<timestamp>.csv` beside the scenario root: one row per scenario, with an outcome and a maximum absolute difference for every compared quantity.

## Input data validation

RODIS validates demand time-series inputs during model initialisation. Demand-pattern records must:
- be in chronological order;
- use a consistent, positive timestep;
- contain no duplicate timestamps;
- contain no gaps within the input series, including omitted leap days;
- be marked as valid;
- contain only finite values; and
- contain only zero or positive pattern values.

RODIS also requires annual demand factors, dam storage capacities and monthly demand scale factors to be finite and non-negative. Monthly scale-factor arrays must contain exactly 12 values.

Invalid input is rejected with an `InvalidDataException` that identifies the affected demand group and, where applicable, the date and invalid value. RODIS does not silently replace invalid demand-pattern values.

This differs from the legacy STEDI documentation, which describes negative time-series values as missing and replaces them with the mean for the relevant calendar month. RODIS deliberately uses explicit rejection because it makes input-data problems visible and prevents invalid values from propagating into demand, storage and streamflow calculations.

Zero pattern values and zero monthly scale factors remain valid. A zero monthly scale factor suppresses demand for that month. Where every time-series pattern value is zero, RODIS applies its uniform-demand fallback.

## Citing

If you use RODIS in published work, please cite it alongside the underlying method:

> Nathan R, Jordan P and Morden R (2005) 'Assessing the impact of farm dams on streamflows, Part I: Development of simulation tools', *Australasian Journal of Water Resources*, 9(1):1–12, doi:10.1080/13241583.2005.11465259.

## Licence

RODIS is copyright © 2026 HARC Services Pty Ltd and is licensed under the GNU General Public License version 3 only.

See:

- [`LICENSE`](LICENSE) for the full GPL-3.0 licence terms;
- NOTICE for the RODIS copyright notice; and
- [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) for third-party software notices.

RODIS-generated example outputs are distributed on the same basis. Third-party STEDI software, documentation and reference outputs are not included in this repository.

### Disclaimer

RODIS is provided as-is, without warranty of any kind, to the extent permitted by applicable law.

RODIS is a hydrological modelling and analysis tool. Model results depend on the quality and completeness of the input data and on assumptions about dam characteristics, water demand, catchment inflows, climate inputs and dam connectivity. The synthetic test scenarios and comparisons with legacy STEDI demonstrate the behaviour of defined model configurations but do not establish that RODIS is suitable for every catchment, application or decision.

Users are responsible for assessing whether RODIS is suitable for their intended purpose and for independently checking model inputs, assumptions, configuration and results. RODIS results should not be relied on as the sole basis for engineering design, regulatory, operational, investment or public safety decisions.

Nothing in this disclaimer excludes, restricts or modifies any right or remedy, or any guarantee, warranty or other term, that cannot lawfully be excluded, restricted or modified under applicable law.

## Acknowledgements

RODIS builds on the STEDI model developed by Sinclair Knight Merz and on the farm dam simulation methods described by Nathan et al. (2005) and Fowler et al. (2015).

Development and validation of RODIS were supported by the Murray–Darling Basin Authority through the Runoff Dams Pilot Study and Business Case. The associated modelling approach and outputs were reviewed through the Runoff Dams Working Group.
