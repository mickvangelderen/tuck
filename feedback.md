# Workshop publishing review

Scope: the uncommitted Workshop publishing changes (`tools/WorkshopPublish/`, `publish.ps1`,
`workshop.json`, `src/ModVersion.targets` and the project file changes). Tests were not reviewed.

No blocking problems. The Steam API usage is right, and moving publishing into one invocation is a
good improvement. Two logic issues are worth fixing before the next release, followed by
maintainability fixes.

## Logic issues

### 1. The recovery file never gets deleted

`tools/WorkshopPublish/WorkshopPublisher.cs:57-67`, `:82`

`artifacts/workshop/publishedfileid.txt` is written when an item is created, and nothing ever
removes it. After the ID has been saved to `workshop.json`, the file has no purpose, but it still
overrides the settings:

- If you set `publishedFileId` back to `0` to create a new item, as `publish.ps1` tells you to, the
  tool quietly reuses the old item. In publish mode it also writes the old ID back into
  `workshop.json`.
- If you point it at a different item, it fails with an ID conflict.

**Fix:** delete the file as soon as `SaveId` succeeds, both after creating an item and in the
recovery branch. Then it only exists after a run that was interrupted.

### 2. A successful upload is reported as a failure when the Workshop agreement isn't accepted

`tools/WorkshopPublish/SteamWorkshop.cs:111`

At that point `SubmitItemUpdate` has already succeeded. The agreement flag only means the item stays
hidden until you accept the agreement. Throwing there causes two problems:

- **After `Publish`:** the script exits with an error and says "then retry", even though the content
  went up. Retrying uploads it again and posts the change note a second time.
- **After `UploadImages`:** the run stops before the content is published, for no real reason.

The design is also inconsistent: `CreateItem` returns the flag, but `Submit` enforces it by calling
back into `WorkshopPublisher.RequireAgreementAccepted`.

**Fix:** have `UploadImages` and `Publish` return the flag too, let `Run` finish, and end with a
warning like "published; hidden until you accept the agreement; no need to retry".

## Fragile or easy to break

### 3. Steam's length limits are only checked at publish time

The current rendered description is 7,272 of the 8,000 bytes Steam allows, which leaves about
700 bytes. Each new image adds about 110 bytes of URL. Title (128 bytes) and change note
(8,000 bytes) aren't checked either. Today an overflow only shows up when the description or title
is set or at submit, after the images have already been uploaded.

**Fix:** check the UTF-8 byte counts in `WorkshopSettings.Load` and on the rendered description
before `Publish`, and give `-PrepareOnly` an estimate. Steamworks.NET has constants for these limits
(`Constants.k_cchPublishedDocument*Max`).

### 4. Changing the working directory is a hidden side effect

`tools/WorkshopPublish/SteamWorkshop.cs:11`

- The constructor changes the process's working directory so Steam can find `steam_appid.txt`.
- The comment at `tools/WorkshopPublish/WorkshopPublisher.cs:38` blames Steam for this, and `Run`
  works around it by converting every input to a full path except `readmePath`. That one is only
  safe because `WorkshopDescription` happens to be created before connecting.

**Fix:** call `Environment.SetEnvironmentVariable("SteamAppId", WorkshopPublisher.AppId.ToString())`
before `InitEx`. Facepunch.Steamworks does the same. That removes `steam_appid.txt`, the duplicated
app ID, the directory change, and the ordering constraint.

### 5. The visibility conversion depends on enum order

`tools/WorkshopPublish/WorkshopSettings.cs:4`, `tools/WorkshopPublish/SteamWorkshop.cs:84`

The cast is correct today: Public, FriendsOnly, Private and Unlisted are 0-3, matching Steam's
`ERemoteStoragePublishedFileVisibility`. But if someone reorders the enum, for example
alphabetically, a private item could be published as public with no error.

**Fix:** use an explicit switch, or give the enum explicit values.

### 6. Old gallery images pile up

`tools/WorkshopPublish/SteamWorkshop.cs:61-77`

- README images are uploaded as extra preview images, which show in the item's public gallery.
- When an image changes (say `shift-drag.gif` is re-encoded), it gets a new name from its content
  hash and is uploaded again, but the old copy is never removed.

The old tool did this too, but now that this tool manages the whole item, it could remove
hash-named images the README no longer uses with `RemoveItemPreview` in the same update. Remove them
from the highest index down, because indexes shift after each removal. This changes what appears on
the Workshop page, so it is a judgment call.

### 7. Rewriting `workshop.json` makes the committed file messy

`tools/WorkshopPublish/WorkshopSettings.cs:8-14`, `:38`

- The JSON serializer's default encoding escapes `'`, `&`, `+`, `<`, `>` and non-ASCII characters,
  so "Don't" is saved as `Don't`.
- Indented output uses Windows line endings (CRLF) while the code appends `"\n"`, so the file ends
  up with mixed line endings.

**Fix:** set `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping` and `NewLine = "\n"`. Adding
`RespectNullableAnnotations = true` also lets the serializer reject `"changeNote": null`, so the
manual null check at `:25` can go.

## Smaller cleanups

- **Mode is passed as a string.** `Run` compares it against `"prepare"`/`"check"` in four places,
  and `Program.cs` validates it separately. Parse it once into an enum in `Program.cs`.
- **Unexpected errors lose detail.** `tools/WorkshopPublish/Program.cs:14` prints only
  `error.Message`. For errors like a missing DLL or a null reference, that drops the error type and
  stack trace. Print `error.ToString()` for anything that isn't one of the expected exceptions.
- **`WorkshopDescription.Render` relies on hidden state** (`:37`, `:82`). It keeps the image URL map
  in a mutable field and treats an empty map as offline mode. A map with some images missing then
  fails with a bare `KeyNotFoundException`. Pass the map in as a parameter and make offline mode an
  explicit argument.

## Checked and fine

- **`src/ModVersion.targets` works:** the Release `Tuck.Mod.dll` is version `1.0.0.0`, read from
  `manifest.xml`, and running before `GetAssemblyVersion` is the standard way to set a version in a
  build step. The ZIP name in `Package` picks it up too.
- **Steamworks calls are correct:**
  - `InitEx` and its error handling.
  - Waiting on results via `RunCallbacks`.
  - Releasing the query handle in `finally`.
  - The app and owner checks before uploading.
  - A null change note for the image-only update.
  - Checking the URLs Steam returns.
- **`WorkshopDescription.cs` is just moved**, with only line endings changed.
