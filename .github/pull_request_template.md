## Problem and change

Describe the concrete trigger and resulting behavior. Link an existing issue if one exists.

## Validation

List commands actually run, results and untested boundaries. Label synthetic results and protocol lab results separately.

## Review checklist

- [ ] Scope is focused; configuration/documentation and meaningful regression tests match the change.
- [ ] No secrets, customer data or unsupported capability/performance claims are added.
- [ ] Authentication and negative authorization cases are covered when relevant.
- [ ] Hardware/PLC writes, machine control, interlocks, emergency stops and protection logic are unchanged,
      or their impact and required qualified human review/onsite validation are explicitly documented.
- [ ] Known failures and skipped/unsupported environments are reported, not hidden.
