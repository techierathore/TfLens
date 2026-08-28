# TfLens — AppManager feedback

Defects found in the **AppManager service** (`https://appmgrapi.techierathore.com`, Application Id 1)
while integrating TfLens against it. AppManager is a separate service with its own codebase and admin UI;
nothing here is fixable from TfLens. Each entry is reproducible against the live API.

Same schema as the other per-owner feedback files (Severity / Repro / Expected / Actual / Encountered in /
Workaround / Suggested fix).

Reference: `docs/AppManager-api-usage-guide.md` (v1.4).

---

## Summary

- **2 blockers, 0 majors, 0 minors, 0 nice-to-haves** — 2 entries, **both ✅ RESOLVED 2026-08-28**.
  **Nothing is open.**
- Last consolidated: 2026-08-28

**Severity words used in the entries map to those counts as:** `High` = blocker · `Medium` = major ·
`Low` = minor. Both entries are filed `High`. Entry bodies keep their original wording, so no recorded
severity was reinterpreted.

| Band | Count | Entries | State |
|---|---|---|---|
| **Blocker** (High) | 2 | AM-001 · AM-002 | both ✅ resolved 2026-08-28, verified live |
| Major / Minor / Nice-to-have | 0 | — | — |

Both entries are **kept in full rather than deleted**. Each carries a dated resolution note with the
live evidence, which is the part the AppManager team will want back: what was wrong, what the fix
changed, and how it was confirmed.

### Resolution status (2026-08-28) — both verified live against `https://appmgrapi.techierathore.com`

| ID | Was | Now | Evidence |
|----|-----|-----|----------|
| **AM-001** | `POST /AuthSvc/register` accepted `applicationRoleCode: "Manager"` with `applicationId: 1` and silently substituted the application default `"User"`, returning `200` either way. | Returns **`applicationRole: "Manager"`** — the code asked for is the code assigned. | Both documented test accounts re-registered on their original addresses: `tflensdemo@techierathore.com` → `userId 2`, `tflenstest2@techierathore.com` → `userId 3`, both `applicationRole: "Manager"`. Login returns the same value, so the empty `applicationRole` previously reported for users 2 and 3 is gone too. |
| **AM-002** | `GET /UserSvc/profile` returned **`403 NO_APP_ACCESS`** whenever the `X-Api-Key`/`X-Api-Secret` pair was sent, for users that did have a role row. | Returns **`200`** with the scoped `applicationRole`. | Measured for `userId 2` with the pair → `200`, `applicationRole: "Manager"`. |

**Consequence for consumers, and it is a reversal — read this before copying the old workaround.**
TfLens had been withholding the key pair from `/UserSvc/*` and sending it only on `/AuthSvc/*`, which
was the documented workaround for AM-002. **That has now been reversed.** Without the pair the endpoint
still answers `200`, but the application scope is unresolved and `applicationRole` comes back as an
**empty string** — so the pair is now the *requirement* rather than the hazard. In TfLens,
`AppManagerClient.SendsApiKeyHeaders` sends the pair on `/UserSvc/*` as well, the test that pinned the
exclusion now pins the inclusion, and `DECISIONS.md` **D-006** carries a dated amendment.

Nothing in this file was merged or renumbered: `AM-001` and `AM-002` are two distinct defects — though,
as AM-002's *Related* note predicted at the time, they shared a root cause and were fixed together.

---

## Entries

| ID | Severity | Endpoint | Status | Summary |
|----|----------|----------|--------|---------|
| [AM-001](#am-001--applicationrolecode-manager-is-silently-ignored-application-1-has-no-manager-role) | **High** | `POST /AuthSvc/register` | ✅ **RESOLVED 2026-08-28** | `applicationRoleCode: "Manager"` was accepted and silently downgraded to `"User"`. Application 1 now defines the `Manager` role and registration returns it. |
| [AM-002](#am-002--get-usersvcprofile-returns-403-no_app_access-for-users-that-do-have-an-application-role) | **High** | `GET /UserSvc/profile` | ✅ **RESOLVED 2026-08-28** | Returned `403 NO_APP_ACCESS` whenever an app context was resolved. Now returns `200` with the scoped `applicationRole`. |

**No open entries.**

---

## AM-001 — `applicationRoleCode: "Manager"` is silently ignored; Application 1 has no `Manager` role

**Severity:** High — the role a caller asks for is not the role it gets, and nothing signals the
substitution.

**Endpoint:** `POST /AuthSvc/register`
**Found:** 2026-08-27, verifying the TfLens requirement that every user maps to AppManager's `Manager`
role (BRD-95 → REQ-FN-002).
**Resolved:** ✅ **2026-08-28 by the owner, verified live from TfLens the same day.** `POST /AuthSvc/register`
with the Application 1 API-key pair, `applicationId: 1` and `applicationRoleCode: "Manager"` now answers
`200` with **`applicationRole: "Manager"`** — the code asked for is the code assigned. Proven by
re-registering both documented test accounts on their original addresses: `tflensdemo@techierathore.com`
→ `userId 2`, `applicationRole: "Manager"`; `tflenstest2@techierathore.com` → `userId 3`,
`applicationRole: "Manager"`. Login returns the same value, so the empty `applicationRole` reported below
for users 2 and 3 is gone as well. The `tflensrole@techierathore.com` reproduction account (`userId 4`)
was deleted with the fix and its row removed from `docs/TfLens-UsageGuide.md`; nothing in TfLens changed,
because the client was always sending the documented request.

### Repro

Register a new user sending exactly what the guide's §"POST /AuthSvc/register" table specifies — the
API-key pair for app resolution, `applicationId: 1` in the body, and `applicationRoleCode: "Manager"`:

```
POST /AuthSvc/register
X-Api-Key / X-Api-Secret: <Application 1 pair>
{ "email": "...", "encryptedPassword": "...", "firstName": "...", "lastName": "...",
  "applicationId": 1, "applicationRoleCode": "Manager" }
```

### Expected

The user is created with the `Manager` application role — or, if `Manager` is not a valid role code for
this application, the call is **rejected** with a clear error naming the unknown role code.

### Actual

```
HTTP 200  userId=4  applicationRole='User'  appManagerRole='ApplicationUser'
```

The request succeeds and the role is silently downgraded to `User`. This matches the guide's stated
fallback — *"`applicationRoleCode` | No | Application role to assign (**defaults to application's default
role**)"* — which implies **Application 1 has no role whose code is `Manager`**, so the requested code is
unknown and the default is substituted.

The same is true of the two accounts registered on 2026-08-26; both report an **empty** `applicationRole`
at login:

| User | `applicationRole` at login | `appManagerRole` |
|---|---|---|
| `tflensdemo@techierathore.com` (userId 2) | `''` | `ApplicationUser` |
| `tflenstest2@techierathore.com` (userId 3) | `''` | `ApplicationUser` |
| `tflensrole@techierathore.com` (userId 4, registered today) | `'User'` | `ApplicationUser` |

### Encountered in

TfLens sends the role code as a hard-coded constant with no caller override
(`AppManagerClient.ManagerRoleCode = "Manager"`, asserted by `ForbiddenServiceTests`), because BRD-95
requires every TfLens user to be a `Manager`. The client is doing exactly what the guide documents; the
server does not honour it.

### Workaround

TfLens ignores the server's `applicationRole` entirely and issues its own constant `Manager` claim
(`AuthService.ManagerRole`), so in-app behaviour is unaffected. That is a deliberate design decision
(BRD-95), not a patch for this bug — but it does mean the discrepancy is invisible from inside the app,
which is why it went unnoticed until the API was probed directly.

### Suggested fix

Either of these, but not silence:

- **Create a `Manager` role for Application 1** in the AppManager admin UI, so the documented request
  produces the documented result. This is the fix TfLens needs.
- **Reject an unknown `applicationRoleCode`** with a distinct error (e.g. `UNKNOWN_ROLE_CODE`) rather
  than silently substituting the default. A caller that asks for a specific role and is given a
  different one, with a `200`, has no way to detect it — the response field is easy to overlook, and
  every downstream authorisation decision is then made on an assumption that was never true.

---

## AM-002 — `GET /UserSvc/profile` returns `403 NO_APP_ACCESS` for users that DO have an application role

**Severity:** High — a working endpoint breaks the moment API-key headers are configured.

**Endpoint:** `GET /UserSvc/profile`
**Found:** 2026-08-27, immediately after configuring the Application 1 API-key pair.
**Resolved:** ✅ **2026-08-28 by the owner, verified live from TfLens the same day.** `GET /UserSvc/profile`
**with** the `X-Api-Key` / `X-Api-Secret` pair now answers **`200`** with `applicationRole: "Manager"`
(measured for `userId 2`). **Without** the pair it still answers `200`, but the application scope is
unresolved and `applicationRole` comes back as an **empty string** — so the pair is now the requirement
rather than the hazard. The TfLens workaround recorded below has been **reversed**:
`AppManagerClient.SendsApiKeyHeaders` sends the pair on `/UserSvc/*` as well as `/AuthSvc/*`, the test
that pinned the exclusion now pins the inclusion, and `DECISIONS.md` D-006 carries a dated amendment.
Likely resolved together with AM-001, as the "Related" note below predicted.

### Repro

Authenticate any of the three users above, then call the profile endpoint twice — once with the API-key
pair attached, once without. The bearer token is identical in both calls:

```
GET /UserSvc/profile   Authorization: Bearer <token>                                 -> 200 OK
GET /UserSvc/profile   Authorization: Bearer <token>  + X-Api-Key / X-Api-Secret     -> 403 NO_APP_ACCESS
```

Reproduced for userId 2, userId 3 and userId 4, and independently of whether the *login* that produced
the token carried the key pair.

### Expected

Per the guide: *"when the caller has a resolvable ApplicationId (via `X-Api-Key`), the returned
`applicationRole` is scoped to that application only… If the user has no `UserApplicationRole` row for
the calling app, the endpoint returns `403 NO_APP_ACCESS`."*

So a `403` is correct **only** for a user with no role row for Application 1. userId 4 was registered
against `applicationId: 1` minutes earlier and the registration response reported
`applicationRole: 'User'` — which implies a role row exists.

### Actual

`403 NO_APP_ACCESS` for all three users, including the one whose registration just reported a role for
this exact application. Either the role row is not actually written at registration (and the
`applicationRole` in the register response is computed rather than stored), or the profile endpoint's
lookup does not match what registration writes.

### Encountered in

TfLens's `/profile` screen (REQ-UI-005), which reads live values via `AuthService.GetProfileAsync`. It
worked for the whole project until the API-key pair was configured, then began failing with an
unhandled-looking error on that page only.

### Workaround

TfLens now sends the API-key pair on `/AuthSvc/*` **only**, never on `/UserSvc/*`
(`AppManagerClient.SendsApiKeyHeaders`, pinned by two tests). This is defensible on its own terms — an
application credential does not belong on a request the bearer token already scopes — and it restores
the profile screen. But it is a workaround: any integrator following the guide's advice to send the pair
on all requests (*"X-Api-Key … (optional but recommended)"*, §"Protected endpoints") will hit this.

### Suggested fix

- Confirm whether `POST /AuthSvc/register` actually writes a `UserApplicationRole` row, and whether the
  `applicationRole` it returns is read from that row or computed from the application default.
- If the row is written, align the profile lookup with it. If it is not, registration should either
  write it or stop reporting a role it did not persist.
- Consider whether `403` is the right answer at all for a user who authenticated successfully and is
  simply asking for their own profile; degrading to the unscoped profile (the documented "no app context"
  behaviour) would be gentler and is already implemented for that case.

### Related

AM-001 is likely the same underlying area: if Application 1 has no roles defined, there may be no
`UserApplicationRole` rows to find. Fixing AM-001 may resolve AM-002 — worth testing together.
