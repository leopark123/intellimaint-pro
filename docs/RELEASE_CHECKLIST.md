# Release checklist

Proposed initial series: **v0.x.y**, with `0.1.0-dev` as current development metadata. There are no tags
at the audit baseline. Do not reinterpret historical document labels as published releases or claim
1.0/production readiness. No release, tag, package or image was published by this work.

## Candidate preparation

- [ ] Review the diff and unresolved items in OSS_READINESS_REPORT.md; select a specific candidate commit.
- [ ] Confirm source/contribution ownership and MIT licensing; review third-party license notices.
- [ ] Align Directory.Build.props, intellimaint-ui/package.json and lockfile, and Swagger version metadata.
- [ ] Update the root CHANGELOG.md with actual changes, migration/security notes and candidate date.
- [ ] Run restore/build/all backend tests, frontend clean install/type-check/lint/unit tests/build/audit.
- [ ] Confirm GitHub Hosted CI and CodeQL on this exact commit, including Docker build and smoke results.
- [ ] Exercise a fresh local demo and container demo; confirm visible synthetic labels and no shared password.
- [ ] Validate all Markdown relative links, YAML and Mermaid; follow the README from a fresh checkout.
- [ ] Verify no secrets, local state, generated UI files or private certificates are tracked.
- [ ] Rotate any actually deployed legacy credentials/tokens; removal from Git does not revoke them.
- [ ] Verify bootstrap on an empty DB and preservation of existing accounts/data; test backup/restore.
- [ ] For any TimescaleDB/protocol support claim, attach provider/lab evidence and exact environment versions.
- [ ] Enable private vulnerability reporting, publish an accountable maintainer contact and review branch protections.
- [ ] Document known limitations, supported runtime versions and the absence of safety certification.

## Human release action

The manual `Release preparation` workflow validates a proposed pre-1.0 version and uploads an API build
artifact. It does not tag, publish a GitHub Release or push a container. It does not replace the full CI
workflow; the input is a proposed version, not an automatic metadata rewrite.

After review, an authorized maintainer may create a signed tag/release, publish tested artifacts with
checksums and appropriate dependency notices, and link exact CI evidence. Check installations from the
published artifact and record rollback/upgrade instructions. Retain reproducible evidence; do not fill
missing release/adoption metrics with estimates. Equipment/safety-affecting deployment requires a
separate qualified review and onsite validation.
