# TfLens — what happened in the TrBlazeUI upgrade pass

| | |
|---|---|
| App | TfLens |
| Date | 2026-09-12 |
| Command | `*build-phase TfLens` (with the TrBlazeUI fixes taken up) |
| Written for | the owner |

---

## The short version

You asked me to check the fixes in the TrBlazeUI feedback file and put them into the code. That is
done. The app builds clean, every test passes, and the workarounds are gone.

**The thing worth knowing:** the fixes had been sitting there unreachable. The feedback file said
"TfLens still builds against 2.0.0", and the reason it stayed that way was not that anyone forgot —
**the fixed version of the library is not on the public NuGet feed.** The public feed stops at 2.0.3.
The version that fixes everything, `2.1.0-ci.10`, lives on your private feed, and TfLens had no file
telling it that feed existed. So the upgrade was impossible rather than overlooked.

I added that file, moved TfLens onto the new version, and the whole thing then went through.

---

## What is now better in the app

Thirty-two problems TfLens had been working around are fixed in the library. The workarounds have
been deleted and the real features used instead. The ones you would actually notice on screen:

- **The token chart on the Harness page is readable again.** It used to print raw numbers on the axis
  and had no labels on the bars, so with one bar at 12.1 billion the other two were invisible hairlines
  with no figures anywhere near them. It now prints `12.1B`, `1.6M` and `37.1M` on the bars themselves,
  with no axis and no gridlines, which is what the design draws.
- **Status badges tell the truth again.** "Needs attention" and "broken" had been forced to share one
  colour because the library only had two. There are now three, so amber and red mean different things.
- **The phone menu is the right width** (288px, as designed, was 256px).
- **Column headings over figures line up right** on the Price providers page.
- **The provider dropdown has its arrow back** — it had been rendering as a plain box.
- **The filter box on the Misses page moved into the card header**, where the design puts it.
- **Tables are tighter**, so five columns fit a half-width card without being squeezed.
- One genuine bug was **found and fixed** on the way: on a phone, the repo card title was sitting on
  top of the status pill by 19 pixels.

Also, a lot of hand-written CSS went away — three stylesheet files were deleted entirely because the
library now does what they were compensating for.

---

## What is NOT finished, and it is one thing

**The mockup-comparison check still fails on thirteen screens' worth of rows.** They are marked
"Needs re-verify", not "Verified".

Two honest points about that:

1. **They were already failing before this pass started.** The previous check, run earlier the same
   day, recorded exactly these thirteen rows as failing the same comparison. That is why they were on
   my worklist. This pass did not break them.
2. **This pass was not asked to fix them.** You asked for the library fixes. Those are done and proven.

The comparison is failing for two separate reasons, and they need different answers:

- **A false alarm.** Every one of the thirteen rows trips first on the same complaint: "the sidebar's
  content is cut off". It is not cut off. The checking tool measures the sidebar's width without first
  asking whether the sidebar actually hides anything, and it does not. I have reported this to the
  TechieFlow team as **TF-042**. I deliberately did not add CSS to silence it, because that would mean
  changing a screen that is correct in order to satisfy a check that is wrong.
- **Real differences behind it.** Once you look past the false alarm there are roughly seventy further
  differences from the mockups, mostly of one kind: the mockup draws a small coloured pill and the app
  prints plain text. These are real and pre-existing.

---

## What I need from you

**One decision, and it is not urgent:**

The mockup differences above are a separate piece of work — a day or so, on screens that function
correctly today. Your options are to fix the app to match the mockups, amend the mockups to match what
the app now does, or leave it. I would not start it without you choosing, because "the app is wrong"
and "the drawing is out of date" lead to opposite changes.

**Two things you may simply want to know about:**

1. **Alert colours moved very slightly.** The library now supplies its own greens and reds, and they
   sit a few degrees of hue away from the mockup's. I chose the library's, because using its colours
   means they come with matched backgrounds and text shades that have been contrast-tested, whereas
   the old approach set the main colour and left the other two mismatched — which was producing a red
   border around a subtly different red panel on the sign-in error. Say the word and I will put the
   exact mockup colours back.
2. **One password-strength meter looks slightly different.** The library's own control colours its
   segments by strength level, so "Good" shows blue where the mockup shows green. Same shape, same
   count, same caption. I left the library's behaviour rather than repainting a control the design
   specifically asked for by name.

**Nothing is blocked and nothing needs doing today.**

---

## Problems I reported upstream

Filed against the library, each one checked against the shipped code before I raised it:

| ID | What | Severity |
|---|---|---|
| TR-036 | A dropdown logs an error into the server log every time you navigate away from a page | Low, log noise only |
| TR-037 | The simple way of building a bar chart silently cannot show value labels — and the library's own documentation demonstrates that exact broken combination | Medium |
| TR-038 | One badge setting silently removes the badge's own text colour | Low |

Filed against the TechieFlow framework:

| ID | What | Severity |
|---|---|---|
| TF-043 | When several build agents work at once they share one build folder, and the app can then serve an **empty stylesheet with a success code** — pages look broken when they are not, and measurements taken then are meaningless | Blocker |
| TF-044 | The screen-checking tool cannot sign in to this app: it types the email before the page is ready, the field is wiped, and it then reports every screen as broken — blaming the app | Blocker |

TF-043 is the one with teeth. It bit three different agents during this pass, and in one case it
caused an agent to report a library bug that **did not exist** — it was measuring an app built before
its own change. I caught that and withdrew it before it reached the library team.

I also **rejected three claimed bugs** after checking them, rather than passing them on: two turned
out to be normal browser behaviour, and one was the false report above.

---

## Evidence

- Build: PASS, 0 warnings
- Tests: 975 passed, 0 failed
- The thirteen changed rows: acceptance 13/13 pass, screens render correctly, all stylesheets and
  scripts load, nothing overlaps or runs off screen
- Details: `docs/TfLens-TrBlazeUI-Feedback.md`, `docs/TfLens-TechieFlow-Feedback.md`,
  `docs/TfLens-P3-Checklist.md`
