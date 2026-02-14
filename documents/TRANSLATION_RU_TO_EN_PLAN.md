# Translation Plan: Russian → English

**Branch:** `feature/translate-ru-to-en`  
**Goal:** Translate all user-facing and internal Russian text to English for consistency and wider audience.

---

## 1. Volume Estimate

| Category | Files | Approx. lines with RU | Priority |
|----------|-------|------------------------|----------|
| **Release notes (user-facing)** | RELEASE_NOTES_v1.5.0.md, v1.4.0?, v1.3.1, v1.3.0 | ~100+ | High |
| **Docs (linked / user-facing)** | Release-Pipeline-Universal-Package.md, README_DEPLOYMENT_FIX.md, PAC_AUTH_ISOLATION.md | ~170 | High |
| **Docs (internal / planning)** | TESTING_AND_QUALITY.md, PLAN_PACKAGE_MODELS_IMPLEMENTATION.md, CONTINUATION_PACKAGE_MODELS.md | ~190 | Medium |
| **Root analysis / post-mortem** | PROBLEM_FOUND_SUMMARY.md, FINAL_ANALYSIS_WITH_DB_DATA.md, ANALYSIS_DEPLOYMENT_ISSUE.md | ~190 | Low (or archive) |
| **Code** | CredentialParser.cs (regex patterns for RU PAC CLI output) | 2 | Keep RU (see below) |
| **Scripts / comments** | New-LcsTemplateFromPackage.ps1, run-template-from-package.ps1 | 2 | Low |
| **Cursor rules** | .cursor/rules/english-only.mdc | 2 | Optional |

**Rough total:** ~450–500 lines containing Russian text across ~15 files.  
**Estimated effort:** 2–4 hours for full pass (depending on how much is technical vs. narrative).

---

## 2. Scope by Phase

### Phase 1 — User-facing (High priority)

- **RELEASE_NOTES_v1.5.0.md** — Mixed EN/RU: make body fully English (SOLID, Unit tests, E2E, Documentation sections).
- **RELEASE_NOTES_v1.3.0.md** — Full translation (82 lines).
- **RELEASE_NOTES_v1.3.1.md** — Single line.
- **docs/Release-Pipeline-Universal-Package.md** — Instruction for Release Pipeline; translate headings and body (~55 lines).
- **docs/README_DEPLOYMENT_FIX.md** — Translate (~46 lines).
- **docs/PAC_AUTH_ISOLATION.md** — Translate (~66 lines).

**Deliverable:** All release notes and main user/ops docs in English.

### Phase 2 — Internal docs (Medium priority)

- **docs/TESTING_AND_QUALITY.md** — Testing, coverage, SOLID, E2E; translate fully (~82 lines).
- **docs/PLAN_PACKAGE_MODELS_IMPLEMENTATION.md** — Implementation plan; translate table headers and task descriptions (~42 lines).
- **docs/CONTINUATION_PACKAGE_MODELS.md** — Continuation/status doc; translate (~64 lines).

**Deliverable:** All documents in English.

### Phase 3 — Root-level analysis docs (Low priority / optional)

- **PROBLEM_FOUND_SUMMARY.md**, **FINAL_ANALYSIS_WITH_DB_DATA.md**, **ANALYSIS_DEPLOYMENT_ISSUE.md** — Post-mortem / analysis; translate or move to `documents/archive/` and add a short English summary at the top.

**Deliverable:** Either translated or archived with EN summary.

### Phase 4 — Code and scripts (Special handling)

- **CredentialParser.cs** — Contains regex for Russian PAC CLI output (`Срок действия секрета`, `Секрет действует до`, `до`). **Do not remove:** keep Russian patterns for compatibility with Russian locale PAC output; add a short comment that these are for Russian locale.
- **New-LcsTemplateFromPackage.ps1** — Example path with Cyrillic "ф2"; replace with a Latin example path and keep a comment if needed.
- **run-template-from-package.ps1** — Comment about Cyrillic folder; rephrase in English.

**Deliverable:** No user-visible Russian; Russian kept only where required for parsing.

---

## 3. Execution Plan

1. **Phase 1** — Translate release notes (v1.5.0, v1.3.0, v1.3.1) and the three user-facing docs in `documents/`.
2. **Phase 2** — Translate TESTING_AND_QUALITY, PLAN_PACKAGE_MODELS_IMPLEMENTATION, CONTINUATION_PACKAGE_MODELS.
3. **Phase 3** — Translate or archive root analysis docs; add EN summary if archived.
4. **Phase 4** — Add comment in CredentialParser; fix .ps1 examples/comments.
5. **Final** — Run a repo-wide search for any remaining `[а-яА-ЯёЁ]`, fix if found. Commit by phase or in one PR.

---

## 4. Checklist (for implementer)

- [ ] Phase 1: RELEASE_NOTES (v1.5.0, v1.3.0, v1.3.1) + Release-Pipeline-Universal-Package, README_DEPLOYMENT_FIX, PAC_AUTH_ISOLATION
- [ ] Phase 2: TESTING_AND_QUALITY, PLAN_PACKAGE_MODELS_IMPLEMENTATION, CONTINUATION_PACKAGE_MODELS
- [ ] Phase 3: PROBLEM_FOUND_SUMMARY, FINAL_ANALYSIS_WITH_DB_DATA, ANALYSIS_DEPLOYMENT_ISSUE (translate or archive)
- [ ] Phase 4: CredentialParser.cs comment; .ps1 examples/comments
- [ ] Grep for remaining Cyrillic; update this plan if new files appear

---

## 5. Notes

- **Razor/UI:** No Russian found in `.razor` files; UI is already in English.
- **RELEASE_NOTES_v1.4.0.md:** No Russian (already EN); no action.
- **.cursor/rules:** Optional; translate or leave as-is for local rules.

*Last updated: when branch created.*
