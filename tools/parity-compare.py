#!/usr/bin/env python3
"""Key-by-key parity compare: tf-metrics.sh --rollup --json  vs  TfLens tflens.json.

REQ-FN-061 / BRD-69 / ADR-008.

Two independent implementations compute the same metrics from the same files. The reference
(`.tfcore/telemetry/tf-metrics.sh`) is trusted -- the SCHEMA.md §6 provenance rules live in its
code. TfLens is the new, unproven one. Correct implementations must agree exactly, so ANY
disagreement is by definition a bug in TfLens, and the script is never changed to match the app.

This is a KEY-BY-KEY compare, not a text diff: key order, indentation and integer-vs-float
formatting are presentation and never produce a finding. What does produce a finding:

    MISSING   a key the reference emits that tflens.json does not have
    ADDED     a key tflens.json has that the reference does not emit
    DIFF      both have the key, and the values differ
    LENGTH    a list is a different length on the two sides
    TYPE      the same key holds a different kind of thing on the two sides

Exit code is 0 only when there are no findings. A figure the reference refuses to print
("insufficient data (n=4)", null) must be refused by TfLens in the same words, or it is a DIFF.

    usage: parity-compare.py <reference.json> <tflens.json> [options]

Nothing here is skipped silently. The two allow-lists below are the complete, documented set of
places where the two documents are permitted to differ, every one of them is printed as an INFO
line on every run, and the summary counts them. If you find yourself wanting to add an entry,
that is the moment to be sure it is a genuine structural difference and not a TfLens bug.
"""

import argparse
import datetime
import hashlib
import json
import os
import sys


# --------------------------------------------------------------- allow-lists
#
# ADDED_KEYS -- keys TfLens deliberately emits that the reference does not. Reported as INFO,
# never as a failure. These are the ONLY additions tolerated; any other added key fails the run.
#
ADDED_KEYS = {
    "extras":
        "REQ-FN-058 -- harness comparison, routing drift and counterfactual repricing. The "
        "reference does not compute these, so they have no parity oracle; they are spot-checked "
        "by hand against raw JSONL and recorded in DECISIONS.md (REQ-FN-064).",
    "parity":
        "REQ-FN-058 / REQ-FN-060 / REQ-FN-062 -- the parity stamp: parser version, last passing "
        "run, reference script hash and the dataset SHAs the figures were computed from.",
    "per_repo[].framework":
        "ADR-016 -- framework (techieflow | playbook) is a stored provenance axis in TfLens and "
        "figures never pool across it. The reference has no such concept.",
    "per_repo[].events":
        "REQ-FN-065 -- the Playbook events.ndjson record count. The reference reads only the "
        "four TechieFlow streams.",
    "per_repo[].source_sha":
        "REQ-FN-062 -- the commit SHA the streams were read at, so the reference dataset can be "
        "pinned with `git checkout`. The reference reads a working tree and has no equivalent.",
}

# ENVIRONMENT_KEYS -- keys whose VALUE describes where the tool ran rather than what the data
# says. They are compared strictly by default; --allow-environment-keys downgrades a value
# difference on exactly these paths to INFO (both values are always printed either way). Nothing
# here is a figure, and no figure may ever be added to this list.
#
ENVIRONMENT_KEYS = {
    "per_repo[].repo":
        "the reference echoes the filesystem path it was handed on the command line; TfLens "
        "identifies a repository as owner/name. Same repository, different name for it.",
    "per_repo[].commit_hook":
        "whether THIS clone has the commit-telemetry hook installed -- a fact about a .git/hooks "
        "directory. TfLens reads the GitHub REST API and has no clone, so it emits null, which "
        "is the value the reference itself uses for 'cannot tell'.",
}

# Lists matched by an identity key instead of by position, because their order is an artefact of
# how the tool was invoked (the reference lists repositories in argv order) rather than data.
KEYED_LISTS = {"per_repo": "repo"}


def canonical(path):
    """Collapse concrete list indices so a path can be looked up in the allow-lists.

    'per_repo[2].framework' -> 'per_repo[].framework'
    """
    out = []
    for part in path.split("."):
        if "[" in part:
            part = part[: part.index("[")] + "[]"
        out.append(part)
    return ".".join(out)


def join(path, key):
    """Extend a dotted path with one more key."""
    return key if not path else path + "." + key


class Compare(object):
    """Walks two decoded JSON documents and collects findings."""

    def __init__(self, allow_environment):
        self.findings = []   # (kind, path, detail) -- these fail the run
        self.notes = []      # (kind, path, detail) -- informational only
        self.allow_environment = allow_environment

    def fail(self, kind, path, detail):
        self.findings.append((kind, path, detail))

    def note(self, kind, path, detail):
        self.notes.append((kind, path, detail))

    # ------------------------------------------------------------ walking

    def walk(self, reference, actual, path=""):
        if isinstance(reference, dict) and isinstance(actual, dict):
            self.walk_dict(reference, actual, path)
        elif isinstance(reference, list) and isinstance(actual, list):
            self.walk_list(reference, actual, path)
        elif isinstance(reference, (dict, list)) or isinstance(actual, (dict, list)):
            self.fail("TYPE", path or "<root>",
                      "reference is %s, tflens is %s" % (kind_of(reference), kind_of(actual)))
        else:
            self.compare_scalar(reference, actual, path)

    def walk_dict(self, reference, actual, path):
        for key in reference:
            child = join(path, key)
            if key not in actual:
                self.fail("MISSING", child, "the reference emits it; tflens.json does not")
                continue
            self.walk(reference[key], actual[key], child)

        for key in actual:
            if key in reference:
                continue
            # An addition is a failure unless it is on the documented allow-list.
            child = join(path, key)
            reason = ADDED_KEYS.get(canonical(child))
            if reason:
                self.note("ADDED-OK", child, reason)
            else:
                self.fail("ADDED", child, "tflens.json emits it; the reference does not")

    def walk_list(self, reference, actual, path):
        key_name = KEYED_LISTS.get(canonical(path))
        if key_name and self.keyed(reference, actual, path, key_name):
            return

        if len(reference) != len(actual):
            self.fail("LENGTH", path,
                      "reference has %d entries, tflens has %d" % (len(reference), len(actual)))

        for index in range(min(len(reference), len(actual))):
            self.walk(reference[index], actual[index], "%s[%d]" % (path, index))

    def keyed(self, reference, actual, path, key_name):
        """Match two lists of objects on an identity key. Returns False if that is not possible."""
        if not all(isinstance(x, dict) and key_name in x for x in reference + actual):
            return False

        # The identity key itself may be an environment key -- per_repo.repo is one: the reference
        # names a repository by the directory it was rolled up from ("alpha"), TfLens by owner/name
        # ("fixtures/alpha"). Match on the last path segment in that case so the two lists pair up,
        # then let the scalar compare report the name difference on its own line. Nothing is hidden:
        # if the segments do not match either, the entries are still reported unmatched.
        identity_is_environment = canonical(join(path + "[]", key_name)) in ENVIRONMENT_KEYS

        def identity(entry):
            value = entry[key_name]
            if identity_is_environment and isinstance(value, str):
                return value.replace("\\", "/").rstrip("/").split("/")[-1]
            return value

        reference_map = dict((identity(x), x) for x in reference)
        actual_map = dict((identity(x), x) for x in actual)

        for name in sorted(set(reference_map) | set(actual_map)):
            child = "%s[%s]" % (path, name)
            if name not in actual_map:
                self.fail("MISSING", child, "the reference reports this entry; tflens does not")
            elif name not in reference_map:
                self.fail("ADDED", child, "tflens reports this entry; the reference does not")
            else:
                self.walk(reference_map[name], actual_map[name], child)

        self.note("KEYED", path,
                  "matched on '%s' rather than by position -- list order here is an artefact of "
                  "how each tool was invoked, not data" % key_name)
        return True

    # ------------------------------------------------------------ scalars

    def compare_scalar(self, reference, actual, path):
        if equal(reference, actual):
            return

        detail = "reference=%s  tflens=%s" % (render(reference), render(actual))
        reason = ENVIRONMENT_KEYS.get(canonical(path))

        if reason and self.allow_environment:
            self.note("ENV-OK", path, "%s  --  %s" % (detail, reason))
        elif reason:
            self.fail("DIFF", path,
                      "%s  --  environment key (%s); pass --allow-environment-keys to accept it"
                      % (detail, reason))
        else:
            self.fail("DIFF", path, detail)


def kind_of(value):
    if isinstance(value, dict):
        return "an object"
    if isinstance(value, list):
        return "a list"
    return "a scalar (%s)" % render(value)


def render(value):
    return "null" if value is None else json.dumps(value, ensure_ascii=False)


def equal(reference, actual):
    """Value equality that ignores presentation but not meaning.

    3 and 3.0 are the same figure printed two ways -- Python's json emits an int where .NET's
    emits a float, and vice versa. True and 1 are NOT the same thing, so booleans are compared
    only against booleans.
    """
    if isinstance(reference, bool) or isinstance(actual, bool):
        return reference is actual
    if isinstance(reference, (int, float)) and isinstance(actual, (int, float)):
        return abs(float(reference) - float(actual)) <= 1e-9
    return reference == actual


# --------------------------------------------------------------- reporting

def report(compare, reference_path, actual_path, stream):
    stream.write("parity-compare: reference=%s\n" % reference_path)
    stream.write("parity-compare: tflens   =%s\n" % actual_path)
    stream.write("\n")

    for kind, path, detail in compare.notes:
        stream.write("  INFO  %-9s %s\n         %s\n" % (kind, path, detail))
    if compare.notes:
        stream.write("\n")

    for kind, path, detail in compare.findings:
        stream.write("  FAIL  %-9s %s\n         %s\n" % (kind, path, detail))
    if compare.findings:
        stream.write("\n")

    stream.write("parity-compare: %d finding(s), %d allowed difference(s).\n"
                 % (len(compare.findings), len(compare.notes)))
    stream.write("parity-compare: %s\n"
                 % ("PASS -- the two implementations agree key for key."
                    if not compare.findings
                    else "FAIL -- every finding above is a bug in TfLens, not in the reference."))


def sha256_of(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for block in iter(lambda: handle.read(65536), b""):
            digest.update(block)
    return "sha256:" + digest.hexdigest()


def write_record(args, output, argv):
    """Write data/parity-last.json -- only ever called on an empty diff (REQ-FN-063)."""
    shas = {}
    for pair in args.dataset_sha or []:
        if "=" not in pair:
            raise SystemExit("parity-compare: --dataset-sha expects repo=sha, got %r" % pair)
        name, sha = pair.split("=", 1)
        shas[name] = sha

    record = {
        "date": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%d"),
        "passed": True,
        "parser_version": args.parser_version,
        "script_path": args.script,
        "script_hash": sha256_of(args.script) if args.script and os.path.isfile(args.script) else None,
        "dataset_shas": shas,
        "compare_command": " ".join(argv),
        "compare_output": output,
        "recorded_ts": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }

    folder = os.path.dirname(os.path.abspath(args.record))
    if folder and not os.path.isdir(folder):
        os.makedirs(folder)
    with open(args.record, "w", encoding="utf-8") as handle:
        json.dump(record, handle, indent=2, ensure_ascii=False)
        handle.write("\n")


def load(path):
    try:
        with open(path, encoding="utf-8") as handle:
            return json.load(handle)
    except IOError as error:
        raise SystemExit("parity-compare: cannot read %s (%s)" % (path, error))
    except ValueError as error:
        raise SystemExit("parity-compare: %s is not valid JSON (%s)" % (path, error))


def main(argv):
    parser = argparse.ArgumentParser(
        prog="parity-compare.py",
        description="Key-by-key compare of tf-metrics.sh --rollup --json against tflens.json. "
                    "Exits non-zero on any finding.")
    parser.add_argument("reference", help="reference.json from tf-metrics.sh --rollup --json")
    parser.add_argument("tflens", help="tflens.json written by the TfLens export")
    parser.add_argument("--allow-environment-keys", action="store_true",
                        help="downgrade a value difference on the documented environment keys "
                             "(per_repo[].repo, per_repo[].commit_hook) to informational. Both "
                             "values are printed either way. Never affects a figure.")
    parser.add_argument("--record", metavar="PATH",
                        help="on a PASS, write the parity record to PATH (data/parity-last.json). "
                             "Nothing is written on a FAIL (REQ-FN-063).")
    parser.add_argument("--parser-version", metavar="VERSION",
                        help="the TfLens parser version this run validates, for the record.")
    parser.add_argument("--script", metavar="PATH",
                        help="path to tf-metrics.sh; its sha256 goes into the record so reference "
                             "drift invalidates the stamp.")
    parser.add_argument("--dataset-sha", metavar="REPO=SHA", action="append",
                        help="a repository and the commit SHA the comparison ran against. Repeat "
                             "for each repository.")
    args = parser.parse_args(argv[1:])

    compare = Compare(args.allow_environment_keys)
    compare.walk(load(args.reference), load(args.tflens))

    import io
    buffer = io.StringIO()
    report(compare, args.reference, args.tflens, buffer)
    output = buffer.getvalue()
    sys.stdout.write(output)

    if compare.findings:
        return 1

    if args.record:
        write_record(args, output, argv)
        sys.stdout.write("parity-compare: recorded the pass in %s\n" % args.record)

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
