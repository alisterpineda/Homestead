# Google Drive backup setup

Homestead can mirror your document library into a **personal (non-Workspace) Google account's** My
Drive. Doing that needs one thing config alone cannot provide: an OAuth client you create in your own
Google Cloud project. This guide is the one-time setup for that client.

You need it because a self-hosted, open-source app has no server-side secret it could hide and no
single Google Cloud project Google could verify on everyone's behalf. The client is yours; the files
land in your Drive, owned by your account, against your quota.

> **A service account will not work.** Service accounts have zero storage quota, so every upload into
> a personal Drive fails. Three-legged user OAuth is the only viable path.

---

## Three facts to read before you start

- **The OAuth client is deployment state, like your database volume.** The `drive.file` scope grants
  access only to files *this client* created. Recreate the client and Homestead permanently loses
  sight of every file the old one made: the existing mirror becomes an orphan copy it can no longer
  manage, and the next run uploads a full second one beside it. Back up the client id and secret with
  the same care as your database credentials.
- **The client secret is shown exactly once, at creation.** Google stores only a hash. Copy it then.
- **A client unused for six months is auto-deleted by Google.** An instance that sat paused for a long
  time can come back to find its client gone.

---

## Outcome checklist

Work through the [Google Cloud console](https://console.cloud.google.com/). You are done when all six
are true:

- [ ] **A Google Cloud project exists**, owned by the same Google account whose Drive you want to
      mirror into. (Any project works; a new one is cleanest.)
- [ ] **The Google Drive API is enabled** on that project — *APIs & Services → Library → Google Drive
      API → Enable*.
- [ ] **The OAuth consent screen exists**, User type **External**. (Personal Google accounts have no
      "Internal" option; External is correct and does not mean public.)
- [ ] **The consent screen is published to "In production"**, not left in *Testing*. This one matters
      more than it looks — see below.
- [ ] **An OAuth client of type "TVs and Limited Input devices" exists** — *APIs & Services →
      Credentials → Create credentials → OAuth client ID → Application type: **TVs and Limited Input
      devices***. Not "Web application", not "Desktop app".
- [ ] **The client id and secret are copied** into Homestead's config (below).

### Why "In production" is not optional

A consent screen left in **Testing** status makes Google **auto-revoke every refresh token after seven
days**. For an interactive app that is a mild annoyance; for a scheduled backup it is a silent weekly
death — the mirror simply stops, and looks fine until you check. Homestead detects this specific shape
(an authorization that dies about a week after it was issued) and says so on the card, but the fix is
to publish.

Publishing costs nothing here: `drive.file` is classified **non-sensitive**, so the app goes to "In
production" with **no verification review** and no waiting period. You may see an "unverified app"
interstitial when you consent — that is expected and you can proceed.

### Why "TVs and Limited Input devices"

Homestead connects using the **OAuth device authorization flow**: the card shows a short code, you
approve it at `google.com/device` on any device, and the server polls until the grant lands. There is
no redirect URI and no callback endpoint.

That is deliberate. Google refuses non-HTTPS and raw-IP redirect URIs, which rules out plain-HTTP and
LAN-IP deployments entirely — and a redirect callback would be an unauthenticated cross-site request
needing signed-state and PKCE machinery just to stop someone splicing *their* Google account into your
backup. The device flow has neither problem. A "Web application" client cannot use it, and will be
rejected with an `invalid_scope` error that reads as though the scope were the problem.

---

## Configuring Homestead

`{key}` is any name you choose (`gdrive` below). It identifies the target on `/admin/backup` and is
what backup pairings are stored against — renaming it later orphans them.

**Docker / production** — environment variables:

```bash
Backup__Targets__gdrive__Kind=GoogleDrive
Backup__Targets__gdrive__ClientId=<id>.apps.googleusercontent.com
Backup__Targets__gdrive__ClientSecret=<secret>
# Optional; defaults to "Homestead Backup".
Backup__Targets__gdrive__RootFolderName=Homestead Backup
```

**Local development** — AppHost user-secrets, so nothing is committed:

```bash
cd Homestead.AppHost
dotnet user-secrets set "Backup:Targets:gdrive:Kind" "GoogleDrive"
dotnet user-secrets set "Backup:Targets:gdrive:ClientId" "<id>.apps.googleusercontent.com"
dotnet user-secrets set "Backup:Targets:gdrive:ClientSecret" "<secret>"
```

Then, in the app: **Admin → Backup → Connect Google account** on that target's card, and follow the
code. Config names the client; the grant is minted by a human and stored (encrypted) in the database.

Finally, add a backup on the card — the pairing of *what* to mirror with *where* — and optionally a
schedule.

---

## What the mirror does, and does not, do

- **The root folder is app-managed.** Homestead creates and maintains `Homestead Backup` (or whatever
  you named it) in My Drive. Files *you* put inside it are invisible to Homestead (`drive.file` sees
  only what it created) and live at its mercy — an empty-folder sweep can trash a folder with your
  file inside it. Recoverable from Google's trash, but do not use this folder for anything else.
- **Deletes go to the trash, never permanently.** Homestead never calls `files.delete`. Everything it
  removes is trashed, giving you Google's ~30-day recovery window. The consequence: **trashed files
  still hold your storage quota** until purged, so removing files from the mirror does not immediately
  free space. Empty Google's trash to reclaim it now.
- **The library sits in plaintext in a consumer Google account.** It is readable by anyone who
  compromises that account, and subject to Google's own access. Use a strong password and 2FA on the
  connected account.
- **A database dump contains a working credential.** The stored token is encrypted with ASP.NET Data
  Protection, whose key ring lives in the same PostgreSQL database — so this protects against a casual
  read or a partial disclosure, not against a full dump. Protect backups of the database accordingly.
- **Uploads are counted against Google's 750 GB/day cap**, which is far above any realistic library.

---

## Error decoder

| What you see | What it means | Fix |
|---|---|---|
| `invalid_client` when connecting | The client id or secret does not match a real client | Re-check `ClientId`/`ClientSecret` for a typo or a trailing newline; a secret can only be *replaced*, never re-read |
| `invalid_scope` when connecting | The client is the wrong **type** — a Web application or Desktop client cannot use the device flow | Create a new client of type *TVs and Limited Input devices* |
| `access_denied` while waiting for approval | You declined, or a Workspace admin policy blocks the app | Approve with a personal account; Workspace-managed accounts may be blocked by policy |
| It works, then dies about **7 days** later | The consent screen is still in *Testing* | Publish it to **In production** (no review is needed for `drive.file`), then reconnect |
| *"The stored Google credential could not be decrypted"* | The Data Protection key ring changed — typically a container recreated without persisting it | Reconnect. To prevent recurrence, keep the key ring's database persistent |
| *"Not connected to Google"* despite valid config | Config names the client; nobody has completed the device flow yet | Use **Connect Google account** on the target's card |
| *"Google Drive is out of storage space"* | The account's quota is full. Note that mirror-deletes free nothing until Google's trash is purged | Empty Google's trash, or buy storage. Nothing latches — the next scheduled run just retries |
| The card shows *Connected* but backups never run | Check the **"Last successful run"** line on the backup row, and that both the destination and the backup itself are enabled | — |

---

## Disconnecting and switching accounts

**Disconnect** deletes the stored grant *and* revokes it with Google (best-effort). Both halves
matter: Google does not invalidate old refresh tokens when it issues new ones, so skipping revocation
would strand a live credential in your Google security page forever. The mirrored files stay in Drive
— delete them there if you want them gone.

**Reconnecting the same account** resumes where it left off; nothing is re-uploaded.

**Connecting a different account** requires an explicit confirmation, and **resets what Homestead
believes it has mirrored**. Drive file ids mean nothing in another account's Drive, so keeping them
would make the next run patch ids that do not resolve and skip copies it believes already exist — a
mirror that reports converged and is empty. The old mirror is abandoned where it is, in the old
account, and the next run uploads everything again.
