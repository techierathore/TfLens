// REQ-NFR-020 / BRD-144 — mockup-parity gate: the structural comparison engine.
//
// Black-box. Nothing here touches application source; the app is measured through the same
// DOM API as the mockup, at the same viewport, and only the two SIGNATURES are compared.
//
// Why signatures and not a pixel diff: the mockups in docs/mockups/ are hand-drawn static HTML.
// A pixel diff over them produces a wall of noise on antialiasing, font metrics and one-pixel
// padding differences, and a gate that cries wolf gets switched off — the most expensive failure
// mode a gate can have (REQ-NFR-018 rationale). So each side is reduced to a small set of
// STRUCTURAL facts per named element — is it a badge, does it carry an icon, what semantic colour
// bucket is it, does its text wrap, is it clipped, is its box narrower than its longest
// unbreakable token — and only those facts are diffed.
import { Page } from '@playwright/test';
import * as path from 'path';

export const MOCKUP_DIR = path.resolve(process.cwd(), 'docs/mockups');

/** The six structural classes BRD-144 names, plus the two structural-report classes. */
export type DriftClass = 'badge' | 'icon' | 'color' | 'wrap' | 'clip' | 'token';

export type Sig = {
  tag: string;
  role: string;
  badge: boolean;
  icon: boolean;
  iconButton: boolean;
  /** Quantised semantic colour: green | amber | red | blue | violet | neutral | none. */
  semantic: string;
  /** Leading ALL-CAPS status token, if the element names its own state ("QUOTABLE", "CHECK — "). */
  status: string;
  /** Line boxes the element's own text occupies; null when the element is not a single-line candidate. */
  lines: number | null;
  clipped: boolean;
  /** Longest unbreakable token that does not fit the content box, measured — never eyeballed. */
  token: { text: string; need: number; have: number } | null;
  w: number;
  h: number;
  text: string;
};

export type Extract = {
  sigs: Record<string, Sig>;
  /** Clause 2: the app-shell scroll-container assertion. */
  doc: { scrollHeight: number; clientHeight: number; delta: number };
  /** How many data-testid anchors the page carried at all. */
  anchors: number;
};

/**
 * Pull the structural signature of every `data-testid` element on the current page, plus a
 * positional signature for the `th`/`tbody td` cells inside each one (the column-clipping and
 * value-cell clauses live in table cells, which carry no testids on either side).
 *
 * Runs entirely in ONE page.evaluate: a per-element CDP round-trip over 40+ controls x 2 widths
 * x 2 sides is minutes of wall clock for measurements the page can do in milliseconds.
 */
export async function extractSignatures(page: Page): Promise<Extract> {
  return page.evaluate(() => {
    // ---- colour quantisation -------------------------------------------------------------
    // The mockup and the app will NEVER agree on a hex value — different token sets, different
    // opacity stacks, a dark shell vs a hand-written palette. What must agree is the MEANING:
    // a passing gate is green on both sides, a failure is red on both sides. So each colour is
    // reduced to a hue bucket and only the bucket is compared.
    //
    // Colours are resolved by PAINTING them, not by parsing the string. The mockups are
    // hand-written `rgb()`, but the app is Tailwind v4 / TrBlazeUI and its computed values come
    // back as `oklch(0.704 0.191 22.216)` — an rgb-only regex returns null for every one of them,
    // which silently disables the colour clause on the app side AND makes every filled control
    // look like plain text. Painting one pixel and reading it back resolves oklch, color-mix and
    // anything else the engine supports, in the engine's own arithmetic.
    const pcvs = document.createElement('canvas');
    pcvs.width = pcvs.height = 1;
    const pctx = pcvs.getContext('2d', { willReadFrequently: true } as any) as CanvasRenderingContext2D | null;
    const pcache: Record<string, { r: number; g: number; b: number; a: number } | null> = {};
    const parse = (s: string) => {
      if (!s) return null;
      if (s in pcache) return pcache[s];
      let out: { r: number; g: number; b: number; a: number } | null = null;
      if (pctx) {
        try {
          pctx.clearRect(0, 0, 1, 1);
          // A sentinel first: if the engine rejects `s`, fillStyle keeps its previous value and we
          // would silently read the sentinel back instead of detecting the failure.
          pctx.fillStyle = '#010203';
          pctx.fillStyle = s;
          const accepted = pctx.fillStyle !== '#010203' || /^#010203$/i.test(s);
          if (accepted) {
            pctx.fillRect(0, 0, 1, 1);
            const d = pctx.getImageData(0, 0, 1, 1).data;
            out = { r: d[0], g: d[1], b: d[2], a: d[3] / 255 };
          }
        } catch { out = null; }
      }
      if (!out) {
        const m = s.match(/rgba?\(([^)]+)\)/);
        if (m) {
          const p = m[1].split(/[,\s/]+/).filter(Boolean).map(Number);
          if (p.length >= 3 && !p.some(n => Number.isNaN(n))) {
            out = { r: p[0], g: p[1], b: p[2], a: p.length > 3 ? p[3] : 1 };
          }
        }
      }
      pcache[s] = out;
      return out;
    };
    const bucket = (c: { r: number; g: number; b: number; a: number } | null): string => {
      if (!c || c.a < 0.05) return 'none';
      const r = c.r / 255, g = c.g / 255, b = c.b / 255;
      const mx = Math.max(r, g, b), mn = Math.min(r, g, b), d = mx - mn;
      const l = (mx + mn) / 2;
      if (d < 0.06) return 'neutral';
      const sat = d / (1 - Math.abs(2 * l - 1) || 1);
      if (sat < 0.18) return 'neutral';
      let h = 0;
      if (mx === r) h = 60 * ((((g - b) / d) % 6 + 6) % 6);
      else if (mx === g) h = 60 * (((b - r) / d) + 2);
      else h = 60 * (((r - g) / d) + 4);
      if (h < 0) h += 360;
      if (h >= 75 && h < 170) return 'green';
      if (h >= 25 && h < 75) return 'amber';
      if (h >= 170 && h < 260) return 'blue';
      if (h >= 260 && h < 330) return 'violet';
      return 'red';
    };
    /** The background a reader actually sees behind an element (walk past transparent ancestors). */
    const effBg = (el: Element | null): string => {
      for (let p: Element | null = el; p; p = p.parentElement) {
        const c = parse(getComputedStyle(p as HTMLElement).backgroundColor);
        if (c && c.a >= 0.05) return getComputedStyle(p as HTMLElement).backgroundColor;
      }
      return 'rgba(0, 0, 0, 0)';
    };

    // ---- icon presence -------------------------------------------------------------------
    // Both sides draw icons as inline <svg> (mockup: a <use> into a sprite; app: Lucide).
    // A sprite <symbol> definition parked in a hidden container is NOT an icon the reader sees,
    // so a zero-size svg does not count.
    //
    // `<i>` counts ONLY when its class actually names an icon set. A bare `i[class]` matcher
    // reported the register mockup's password-strength meter as "carrying an icon" — its four
    // strength segments are `<i class="on">` elements, which are bars, not glyphs. Every screen
    // with a strength meter then failed the icon clause for a control that is identical on both
    // sides, which is precisely the kind of noise that gets a gate switched off.
    const hasIcon = (el: Element): boolean => {
      const cands = el.querySelectorAll(
        'svg, i[class*="icon"], i[class*="fa-"], i[class*="bi-"], i[class*="lucide"], '
        + '[class*="icon"], [class*="lucide"], [data-icon]');
      for (const c of Array.from(cands)) {
        const r = c.getBoundingClientRect();
        if (r.width > 1 && r.height > 1) return true;
      }
      if (el.tagName.toLowerCase() === 'svg') {
        const r = el.getBoundingClientRect();
        if (r.width > 1 && r.height > 1) return true;
      }
      return false;
    };

    // ---- badge / pill detection ----------------------------------------------------------
    // The clause is "a control the mockup renders as a badge/pill rendered as plain text".
    // Plain text is text with NO chrome: no distinct background, no border, no rounding. So the
    // test is for chrome on a short, low element — which is what a badge is on either side,
    // whatever it is called. The class-name path catches the two vocabularies directly; the
    // computed path catches a badge built from utility classes.
    //
    // An INTERACTIVE control is never "plain text", so it is never gradeable under this clause.
    // Without that exclusion the computed path grades every button: the mockup's `.btn` has a 1px
    // border and an 8px radius, the app's TrBlazeUI button has a filled background and no border,
    // and the gate reports "badge rendered as plain text" for a rounded, filled, obviously-not-text
    // submit button on every screen in the product. Buttons still carry the icon and colour
    // clauses, where a real difference IS visible.
    const INTERACTIVE = 'button, a, input, select, textarea, [role="button"], [role="tab"], [role="switch"], [role="link"], [role="menuitem"]';
    const chromeOn = (el: Element, text: string): boolean => {
      const cls = (el.getAttribute('class') || '').toLowerCase();
      if (/(^|[\s-])(badge|chip|pill|tag)([\s-]|$)/.test(cls)) return true;
      const cs = getComputedStyle(el as HTMLElement);
      const r = el.getBoundingClientRect();
      if (r.height <= 0 || r.height > 40) return false;
      if (text.length === 0 || text.length > 40) return false;
      const radius = parseFloat(cs.borderTopLeftRadius) || 0;
      const bw = (parseFloat(cs.borderTopWidth) || 0) + (parseFloat(cs.borderLeftWidth) || 0);
      const own = parse(cs.backgroundColor);
      const distinctBg = !!own && own.a >= 0.05 && cs.backgroundColor !== effBg(el.parentElement);
      const chrome = distinctBg || bw > 0;
      return chrome && radius >= 4;
    };
    // The two sides do not always hang the testid at the same depth. The Three-questions and
    // Routing tab triggers are the worked example: the mockup puts `type-tab-app` on the styled
    // `<span class="tab active">` itself, while the app puts it on an inner layout span and the
    // active-tab pill sits on the parent `<button role="tab">`. Comparing only the anchored node
    // reports "badge rendered as plain text" for a tab that is, in fact, a pill.
    //
    // So the chrome is looked for on the anchored element OR on an ancestor that TIGHTLY wraps it
    // (within 12px on every side) and is not itself another named control. Tightness is what keeps
    // this honest: a genuinely missing track — the app's `framework-switch`, whose only ancestor is
    // a full-width header bar — finds nothing and stays a finding.
    const isBadge = (el: Element, text: string): boolean => {
      if (el.matches(INTERACTIVE)) return false;
      if (chromeOn(el, text)) return true;
      const r0 = el.getBoundingClientRect();
      let p = el.parentElement;
      for (let up = 0; p && up < 3; up++, p = p.parentElement) {
        if (p.hasAttribute('data-testid')) break;
        const pr = p.getBoundingClientRect();
        const tight = Math.abs(pr.left - r0.left) <= 12 && Math.abs(pr.right - r0.right) <= 12
          && Math.abs(pr.top - r0.top) <= 12 && Math.abs(pr.bottom - r0.bottom) <= 12;
        if (!tight) break;
        if (chromeOn(p, text)) return true;
      }
      return false;
    };

    // ---- single-line candidacy ------------------------------------------------------------
    // Line counting is only meaningful for an element whose content is inline. A card with three
    // stacked block children "occupies four lines" and means nothing by it, so those report null
    // and are never diffed on the wrap clause.
    const inlineOnly = (el: Element): boolean => {
      const kids = el.querySelectorAll('*');
      if (kids.length > 24) return false;
      for (const k of Array.from(kids)) {
        const d = getComputedStyle(k as HTMLElement).display;
        if (d === 'block' || d === 'flex' || d === 'grid' || d === 'table' ||
            d === 'list-item' || d === 'table-row' || d === 'table-cell') return false;
      }
      return true;
    };
    const lineCount = (el: Element): number | null => {
      if (!inlineOnly(el)) return null;
      const t = (el.textContent || '').trim();
      if (!t) return null;
      const rg = document.createRange();
      rg.selectNodeContents(el);
      const rects = Array.from(rg.getClientRects()).filter(r => r.width > 0.5 && r.height > 0.5);
      if (rects.length === 0) return null;
      const tops = rects.map(r => r.top).sort((a, b) => a - b);
      let lines = 1;
      for (let i = 1; i < tops.length; i++) if (tops[i] - tops[i - 1] > 4) lines++;
      return lines;
    };

    // ---- longest unbreakable token --------------------------------------------------------
    // "a value cell narrower than its longest unbreakable token (a formatted number must never
    // break mid-digit)". MEASURED with the element's own computed font, not estimated from
    // character counts — a proportional font makes `2,287,975,139` and `WWWWWWWWWWWWW` wildly
    // different widths and a character-count heuristic would flag the wrong one.
    const cvs = document.createElement('canvas');
    const ctx = cvs.getContext('2d');
    const tokenFit = (el: Element): { text: string; need: number; have: number } | null => {
      if (!ctx) return null;
      if (!inlineOnly(el)) return null;
      const raw = (el.textContent || '').trim();
      if (!raw) return null;
      const cs = getComputedStyle(el as HTMLElement);
      ctx.font = `${cs.fontStyle} ${cs.fontWeight} ${cs.fontSize} / ${cs.lineHeight} ${cs.fontFamily}`;
      // Break only where a browser may break: whitespace and the soft-wrap opportunities a
      // hyphen/slash gives. Everything between those is one unbreakable token.
      const toks = raw.split(/[\s ]+/).filter(Boolean).filter(t => t.length >= 4);
      if (toks.length === 0) return null;
      let worst: { text: string; need: number } | null = null;
      for (const t of toks) {
        const w = ctx.measureText(t).width;
        if (!worst || w > worst.need) worst = { text: t, need: w };
      }
      if (!worst) return null;
      // A cell whose padding has eaten ALL of its content width is the WORST case of this defect,
      // not an absence of it — every token overflows a zero-width box. Returning null here (the
      // first cut of this function) made the gate silently pass the most broken column it could
      // ever meet; the catch-proof injection found it by squeezing a column to 18px against 24px
      // of padding. Clamp at zero and let the comparison speak.
      const have = Math.max(0, (el as HTMLElement).clientWidth
        - (parseFloat(cs.paddingLeft) || 0) - (parseFloat(cs.paddingRight) || 0));
      if ((el as HTMLElement).clientWidth <= 0) return null;
      // 1px of slack: sub-pixel text metrics vs an integer clientWidth.
      return worst.need > have + 1 ? { text: worst.text, need: Math.round(worst.need), have: Math.round(have) } : null;
    };

    const sigOf = (el: Element) => {
      const cs = getComputedStyle(el as HTMLElement);
      const r = el.getBoundingClientRect();
      const text = ((el as HTMLElement).innerText || el.textContent || '').trim().replace(/\s+/g, ' ');
      const bg = bucket(parse(cs.backgroundColor));
      const fg = bucket(parse(cs.color));
      // A status alert may carry its meaning in the BORDER rather than the fill — the app's
      // TrBlazeUI Alert does exactly that. Reading only background and text would call an
      // amber-bordered warning "neutral" and then report a difference against a mockup that
      // tints the fill, which is a difference in where the colour lives, not in what it means.
      const bd = bucket(parse(cs.borderTopColor));
      const badge = isBadge(el, text);
      const icon = hasIcon(el);
      const tag = el.tagName.toLowerCase();
      const role = el.getAttribute('role')
        || (tag === 'a' || tag === 'button' ? 'button' : tag === 'input' ? 'input' : tag);
      return {
        tag,
        role,
        badge,
        icon,
        iconButton: icon && (role === 'button' || tag === 'button' || tag === 'a'),
        // The semantic signal is the fill when there is one (a status pill), else the border
        // (a tinted alert), else the ink (a coloured figure on a plain card).
        semantic: bg !== 'none' && bg !== 'neutral' ? bg
          : (parseFloat(cs.borderTopWidth) || 0) > 0 && bd !== 'none' && bd !== 'neutral' ? bd
          : fg,
        // A banner names its own state in capitals ("QUOTABLE", "NOT QUOTABLE", "CHECK — ...").
        // Comparing the colour of two DIFFERENT states is meaningless, so the status token is
        // captured and the colour clause refuses to attribute a difference across a state change.
        status: (text.match(/^([A-Z][A-Z—– -]{2,})/) || [, ''])[1].trim(),
        lines: lineCount(el),
        clipped: (el as HTMLElement).scrollWidth > (el as HTMLElement).clientWidth + 2,
        token: tokenFit(el),
        w: Math.round(r.width),
        h: Math.round(r.height),
        text: text.slice(0, 80),
      };
    };

    const sigs: Record<string, any> = {};
    const anchored = Array.from(document.querySelectorAll('[data-testid]'));
    for (const el of anchored) {
      const id = el.getAttribute('data-testid');
      if (!id) continue;
      const cs = getComputedStyle(el as HTMLElement);
      if (cs.display === 'none' || cs.visibility === 'hidden') continue;
      const r = el.getBoundingClientRect();
      if (r.width <= 0 || r.height <= 0) continue;
      // First occurrence wins, matching every other locator in the suite (`.first()`).
      if (sigs[id]) continue;
      sigs[id] = sigOf(el);
    }

    // ---- positional fallback for the un-anchored cells inside a named table ------------------
    // Neither side puts a testid on a th or a td, and three of BRD-144's six clauses live exactly
    // there (header wraps, column clipped, value cell narrower than its longest token).
    //
    // Each table is graded ONCE, under its CLOSEST data-testid ancestor. Walking down from every
    // testid instead graded the same grid several times over — `misses-page` is a page container
    // whose first descendant table is `miss-detail`'s, so every cell defect in it was reported
    // twice under two different paths. One anchor per table keeps the path stable and the count
    // honest.
    const anchorOf = (t: Element): string | null => {
      const a = t.closest('[data-testid]');
      return a ? a.getAttribute('data-testid') : null;
    };
    const claimed = new Set<string>();
    for (const table of Array.from(document.querySelectorAll('table'))) {
      const id = anchorOf(table);
      if (!id) continue;
      const r = table.getBoundingClientRect();
      if (r.width <= 0 || r.height <= 0) continue;
      // A testid holding more than one table: index them so the paths stay distinct.
      let key = id;
      for (let n = 2; claimed.has(key); n++) key = `${id}#${n}`;
      claimed.add(key);
      Array.from(table.querySelectorAll('thead th')).forEach((th, i) => {
        sigs[`${key} > th[${i}]`] = sigOf(th);
      });
      Array.from(table.querySelectorAll('tbody tr')).slice(0, 3).forEach((tr, ri) => {
        Array.from(tr.querySelectorAll('td')).forEach((td, ci) => {
          sigs[`${key} > tr[${ri}]td[${ci}]`] = sigOf(td);
        });
      });
    }

    const de = document.documentElement;
    return {
      sigs,
      doc: {
        scrollHeight: de.scrollHeight,
        clientHeight: de.clientHeight,
        delta: de.scrollHeight - de.clientHeight,
      },
      anchors: anchored.filter(e => !!e.getAttribute('data-testid')).length,
    };
  }) as Promise<Extract>;
}

// ---------------------------------------------------------------------------------------------
// Allow-list — deliberate, RECORDED deviations from the mockup.
//
// Every entry names one element, one class of drift and the decision that authorised it. Two
// guardrails, enforced by `assertAllowListSane()` below and not by good intentions:
//   * no wildcard testid, so an entry can never silence a whole screen; and
//   * at most MAX_PER_SCREEN entries per screen, so nobody can quietly allow-list a screen into
//     a pass one element at a time.
// A gate whose allow-list can hide a screen is not a gate.
//
// `screen: '*'` waives ONE NAMED ELEMENT across every screen — the shape a shell control needs,
// since the header sits on all seven authenticated routes and seven copies of the same reason is
// how a reason stops being read. It does not weaken either guarantee: the testid is still exactly
// one element, so no screen can be hidden. A wildcard TESTID remains banned outright.
// ---------------------------------------------------------------------------------------------
export type Allow = { screen: string; testid: string; classes: DriftClass[]; reason: string };

const MAX_PER_SCREEN = 6;

export const ALLOW: Allow[] = [
  {
    screen: '*',
    testid: 'theme-toggle',
    classes: ['icon'],
    reason: 'TWO recorded reasons, both verified against the DOM on 2026-08-29. (1) ANCHORING: the '
      + 'mockup hangs `theme-toggle` on the whole `<button class="switch">`, which contains the sun '
      + 'and moon glyphs; the app hangs it on the TrBlazeUI `Switch` itself, and its moon '
      + '`LucideIcon` is a SIBLING one level up (measured: the wrapper is flush on three sides and '
      + '22px wider on the left, which is the moon). The icon is rendered, just outside the anchored '
      + 'node. (2) DELIBERATE: the leading SUN was removed on 2026-08-29 under REQ-UI-010 — at 1280 '
      + 'the header needed 1090px of a 1024px bar and those 22px were part of why the row wrapped. '
      + 'See src/TfLens/Components/Shared/ThemeToggle.razor. RESIDUAL RISK: while this waiver stands '
      + 'the gate cannot see the moon disappear either; lift it by moving the testid onto the '
      + 'wrapper, and the clause becomes live again with no waiver needed.',
  },
  {
    screen: 'coverage',
    testid: 'kpi-newest-age',
    classes: ['icon'],
    reason: 'Sparkline deliberately not built: `Newest record age` has no stored series behind it, '
      + 'and a line drawn through invented points is what BRD §1 forbids. Recorded decision.',
  },
  {
    screen: 'coverage',
    testid: 'kpi-sync-errors',
    classes: ['icon'],
    reason: 'Sparkline deliberately not built: `Sync errors` has no stored series behind it (BRD §1). '
      + 'Recorded decision.',
  },
  {
    screen: 'coverage',
    testid: 'kpi-last-sync',
    classes: ['icon'],
    reason: 'Sparkline deliberately not built: `Last successful sync` has no stored series behind it '
      + '(BRD §1). Recorded decision.',
  },
  {
    screen: 'three-questions',
    testid: 'kpi-escape',
    classes: ['icon'],
    reason: 'Sparkline deliberately not built — no stored series behind the tile (BRD §1). '
      + 'Recorded decision.',
  },
  {
    screen: 'three-questions',
    testid: 'kpi-failures',
    classes: ['icon'],
    reason: 'Sparkline deliberately not built — no stored series behind the tile (BRD §1). '
      + 'Recorded decision.',
  },
  {
    screen: 'coverage',
    testid: 'repo-card-header',
    classes: ['wrap'],
    reason: 'The Coverage repo-card header runs to two rows BY DESIGN: it carries the '
      + '`Synced`/`Imported` source badge the mockup predates (REQ-UI-042 / BRD-136).',
  },
  {
    screen: 'profile',
    testid: 'profile-identity-note',
    classes: ['wrap', 'token', 'color'],
    reason: 'docs/mockups/profile.html is WRONG and the owner has been told: it says passwords are '
      + 'RSA-encrypted "before they leave the browser". This is Blazor Server and '
      + 'AppManagerClient.Encrypt runs RSA-OAEP-SHA256 SERVER-side. The app wording is correct; the '
      + 'mockup must change, not the app.',
  },
];

/** Fail loudly if the allow-list has grown into a way of hiding a screen. */
export function assertAllowListSane(): void {
  const perScreen: Record<string, number> = {};
  for (const a of ALLOW) {
    if (!a.testid || a.testid.includes('*')) {
      throw new Error(`mockup-parity allow-list: wildcard testid "${a.testid}" on ${a.screen} — an `
        + 'entry may never silence more than one element.');
    }
    if (!a.reason || a.reason.length < 40) {
      throw new Error(`mockup-parity allow-list: ${a.screen}/${a.testid} has no recorded reason.`);
    }
    perScreen[a.screen] = (perScreen[a.screen] ?? 0) + 1;
  }
  for (const [screen, n] of Object.entries(perScreen)) {
    if (n > MAX_PER_SCREEN) {
      throw new Error(`mockup-parity allow-list: ${n} entries on "${screen}" exceeds the cap of `
        + `${MAX_PER_SCREEN} — a screen cannot be allow-listed into a pass one element at a time.`);
    }
  }
}

function allowed(screen: string, key: string, cls: DriftClass): Allow | undefined {
  // A positional cell path inherits its table's entry (`repos-table > th[3]` -> `repos-table`).
  const base = key.split(' > ')[0];
  return ALLOW.find(a => (a.screen === screen || a.screen === '*')
    && (a.testid === key || a.testid === base) && a.classes.includes(cls));
}

export type Finding = {
  key: string;
  cls: DriftClass;
  width: number;
  mockup: string;
  app: string;
  detail: string;
  waived?: string;
};

/**
 * Diff two signature sets. Only the six classes BRD-144 names are gradeable, and only on elements
 * BOTH sides carry — a testid the mockup has and the app does not is reported as INFO by the
 * caller, because "control missing" is the §4a render gate's question and duplicating it here
 * would double-report the same defect on a dataset that legitimately differs.
 */
export function diff(screen: string, width: number, m: Record<string, Sig>, a: Record<string, Sig>) {
  const findings: Finding[] = [];
  const waived: Finding[] = [];
  /** Differences the gate can SEE but cannot attribute to drift — reported, never silently dropped. */
  const unattributable: Finding[] = [];
  const push = (f: Finding) => {
    const w = allowed(screen, f.key, f.cls);
    if (w) waived.push({ ...f, waived: w.reason });
    else findings.push(f);
  };

  for (const key of Object.keys(m)) {
    const M = m[key], A = a[key];
    if (!A) continue;

    // 1. badge/pill rendered as plain text.
    if (M.badge && !A.badge) {
      push({ key, cls: 'badge', width, mockup: 'badge/pill', app: 'plain text',
        detail: `mockup renders "${M.text.slice(0, 40)}" as a badge; app renders it with no chrome` });
    }

    // 2. missing icon / icon button.
    if (M.icon && !A.icon) {
      push({ key, cls: 'icon', width, mockup: 'has icon', app: 'no icon',
        detail: `mockup ${M.iconButton ? 'icon button' : 'element'} carries an icon; app renders none` });
    }

    // 3. semantic colour bucket.
    if (M.semantic !== A.semantic && M.semantic !== 'none' && A.semantic !== 'none') {
      // A colour difference is only ATTRIBUTABLE when both sides are showing the same state.
      // /export is the worked example: the mockup depicts the amber NOT QUOTABLE banner, and the
      // app on the current dataset is correctly the green QUOTABLE one. That is the app being
      // right, not the app drifting, and failing it would teach a reader to ignore the clause
      // that catches a genuinely miscoloured status.
      const stateDiff = !!M.status && !!A.status && M.status !== A.status;
      if (stateDiff) {
        unattributable.push({ key, cls: 'color', width, mockup: `${M.semantic} ("${M.status}")`,
          app: `${A.semantic} ("${A.status}")`,
          detail: 'colour differs, but the two sides are showing DIFFERENT states — not attributable '
            + 'to drift. Reported, not failed.' });
      } else if (M.semantic !== 'neutral' || A.semantic !== 'neutral') {
        push({ key, cls: 'color', width, mockup: M.semantic, app: A.semantic,
          detail: `semantic colour bucket differs — mockup "${M.text.slice(0, 40)}" vs app "${A.text.slice(0, 40)}"` });
      }
    }

    // 4. wraps where the mockup is single-line.
    if (M.lines !== null && A.lines !== null && M.lines === 1 && A.lines > 1) {
      push({ key, cls: 'wrap', width, mockup: '1 line', app: `${A.lines} lines`,
        detail: `"${A.text.slice(0, 40)}" wraps to ${A.lines} lines; mockup is single-line` });
    }

    // 5. clipped out of its container.
    if (!M.clipped && A.clipped) {
      push({ key, cls: 'clip', width, mockup: 'not clipped', app: 'clipped',
        detail: `content overflows its box (scrollWidth > clientWidth) where the mockup fits` });
    }

    // 6. value cell narrower than its longest unbreakable token.
    if (!M.token && A.token) {
      push({ key, cls: 'token', width, mockup: 'token fits', app: 'token does not fit',
        detail: `"${A.token.text}" needs ${A.token.need}px, cell gives ${A.token.have}px — it breaks mid-token` });
    }
  }
  return { findings, waived, unattributable };
}
