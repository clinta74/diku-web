#!/usr/bin/env python3
"""Merges several WorldBundle files into one, so a whole world imports in a single upload.

    python tools/merge-bundles.py content -o build/the-reaches.json

Takes files, directories, or both; a directory is searched recursively for `*.json`. Writes one
bundle scoped `{"kind": "all", "key": null}` — the same shape a no-parameter
`GET /api/builder/export` already produces, which is why this needs no server change and no
format bump. `WorldBundle.Worlds` has always been a list.

**Merging is worth more than saving five uploads.** The importer's order is dependency order over
the whole bundle — worlds, zones, templates, rooms, *then* exits, then spawners and quests. Import
six files one at a time and the first realm's exits are applied before the next realm's rooms
exist, which is why `content/README.md` has to name a realm order and warn about dangling exits.
Merged, every room exists before any exit is set and that entire class of warning goes away.

It also gives `check-bundle.py` a whole-world view. That script is per-file, so its reciprocity and
connectivity passes only see edges whose target is in the same file — which makes the four
attunement gates, each naming a room in the *next* realm, the one part of the world its checks
cannot verify at all. Run it on the merged file and they can be.

Two things this deliberately does not do:

  - **It does not resolve conflicts.** A key carried by two files with two different bodies is an
    error naming both, never a last-one-wins. Today the only repeated keys are the `ossara` world
    row and the `epic-smith-vesh` mob template, and every copy is byte-identical — so silent
    last-wins would work now and would go on appearing to work right up until somebody retuned the
    smith in one file, which is the failure this repo keeps getting bitten by.
  - **It does not make the import atomic.** One entity is still one loop round trip and one
    transaction, so a merged import that fails part way through leaves the entities before it
    applied. What merging changes is that every intermediate state is *valid*, and that one dry run
    covers the whole world instead of six.

Output is deterministic: collections are sorted by identity and `exportedAt` is the newest of the
inputs rather than the time this ran, so re-merging unchanged content produces an identical file.
That is what makes it safe to leave the result out of git — it can be rebuilt exactly, and a
committed merged file would be a second source of truth that drifts from the six.

Exit status is 1 if anything is reported as an error.
"""
import argparse
import json
import sys
from pathlib import Path

# Emitted in this order to match the C# record, so a merged file and a server export read the
# same way to anyone diffing them.
COLLECTIONS = (
    ('worlds', 'key'),
    ('zones', 'key'),
    ('itemTemplates', 'key'),
    ('mobTemplates', 'key'),
    ('abilities', 'key'),
    ('rooms', 'key'),
    ('spawners', 'id'),
    ('quests', 'key'),
    ('configurations', 'key'),
)


def bundle_paths(arguments):
    """Every bundle named, with directories expanded and the result sorted for determinism."""
    paths = []

    for argument in arguments:
        path = Path(argument)

        if path.is_dir():
            paths.extend(sorted(path.rglob('*.json')))
        else:
            paths.append(path)

    return paths


def merge(paths):
    """One bundle from many, or a list of errors explaining why not."""
    errors = []

    # identity -> (body-as-json, file it came from), so a conflict can name both sides.
    seen = {name: {} for name, _ in COLLECTIONS}
    versions = {}
    exported = []

    for path in paths:
        try:
            bundle = json.loads(path.read_text(encoding='utf-8'))
        except (OSError, ValueError) as failure:
            errors.append('%s could not be read: %s' % (path, failure))
            continue

        if not isinstance(bundle, dict) or 'formatVersion' not in bundle:
            errors.append('%s is not a WorldBundle: no formatVersion' % path)
            continue

        versions.setdefault(bundle['formatVersion'], []).append(path)

        if bundle.get('exportedAt'):
            exported.append(bundle['exportedAt'])

        for name, identity in COLLECTIONS:
            for entity in bundle.get(name) or []:
                if identity not in entity:
                    errors.append('%s: a %s entry has no %r' % (path, name, identity))
                    continue

                key = entity[identity]
                body = json.dumps(entity, sort_keys=True)
                held = seen[name].get(key)

                if held is None:
                    seen[name][key] = (body, path)
                elif held[0] != body:
                    # Named on both sides, because "which file wins" is the question the reader
                    # is about to ask and the whole reason this is not resolved silently.
                    # ASCII only in anything printed: this runs on a cp1252 console, where an
                    # em-dash arrives as a replacement character and makes the tool look broken
                    # at the exact moment it is reporting a real problem.
                    errors.append(
                        '%s %r differs between %s and %s. Resolve it in the content, '
                        'because there is no right answer to pick here.'
                        % (name, key, held[1], path))

    # A single hard refusal, mirroring the import path's own: bundles of two shapes cannot be
    # meaningfully combined, since the merged file can only claim one version and would be lying
    # about half its contents.
    if len(versions) > 1:
        errors.append(
            'these bundles are not all the same formatVersion: '
            + '; '.join(
                '%d in %s' % (version, ', '.join(str(p) for p in files))
                for version, files in sorted(versions.items())))

    if errors:
        return None, errors

    if not versions:
        return None, ['no bundles were given']

    merged = {
        'formatVersion': next(iter(versions)),
        # The newest input rather than now, so the output is reproducible.
        'exportedAt': max(exported) if exported else None,
        'scope': {'kind': 'all', 'key': None},
    }

    for name, _ in COLLECTIONS:
        # Sorted by identity, not by the order the files happened to be read in. Every collection
        # is applied wholesale before the one that depends on it, so ordering within one carries no
        # meaning — and sorting is what makes a re-merge byte-identical.
        merged[name] = [
            json.loads(body) for _, (body, _) in sorted(seen[name].items())]

    return merged, []


def main():
    parser = argparse.ArgumentParser(
        description='Merge WorldBundle files into one.',
        epilog='Then check it: python tools/check-bundle.py <output>')
    parser.add_argument('inputs', nargs='+', help='bundle files, or directories of them')
    parser.add_argument('-o', '--out', required=True, help='where to write the merged bundle')
    arguments = parser.parse_args()

    paths = bundle_paths(arguments.inputs)

    if not paths:
        print('ERROR  nothing to merge in %s' % ', '.join(arguments.inputs))
        return 1

    for path in paths:
        print('  read   %s' % path)

    merged, errors = merge(paths)

    if errors:
        for message in errors:
            print('  ERROR  ' + message)
        print('FAILED')
        return 1

    out = Path(arguments.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    # Trailing newline and two-space indent to match the authored files, so the merged output is
    # readable by the same eyes.
    out.write_text(json.dumps(merged, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')

    print('  wrote  %s (formatVersion %d, scope all)' % (out, merged['formatVersion']))
    print('         ' + ', '.join(
        '%d %s' % (len(merged[name]), name) for name, _ in COLLECTIONS if merged[name]))
    print('OK')
    return 0


if __name__ == '__main__':
    sys.exit(main())
