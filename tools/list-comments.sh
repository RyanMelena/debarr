#!/usr/bin/env bash
# Lists the comments in Debarr and Debarr.Tests, for the comments review.
#   tools/list-comments.sh          every comment block, its file:line and the code line after it
#   tools/list-comments.sh flags    the comment lines a text search flags: document references, history and negatives
#   tools/list-comments.sh count    the number of comment lines in each file
# Covers // and /// in C# files and @* *@ in Razor markup. obj and bin are skipped.
set -euo pipefail
cd "$(dirname "$0")/../src"

files() {
    find Debarr Debarr.Tests -type f \( -name '*.cs' -o -name '*.razor' \) \
        -not -path '*/obj/*' -not -path '*/bin/*' | sort
}

blocks() {
    files | while read -r file; do
        awk -v file="$file" '
            function is_comment(line) { return line ~ /^[ \t]*(\/\/|@\*)/ || in_razor }
            {
                if (is_comment($0)) {
                    if (!open) { start = NR; text = "" }
                    open = 1
                    text = text "    " $0 "\n"
                    if ($0 ~ /@\*/ && $0 !~ /\*@/) in_razor = 1
                    else if ($0 ~ /\*@/) in_razor = 0
                    next
                }
                if (open) { printf "%s:%d\n%s  > %s\n\n", file, start, text, $0; open = 0 }
                else if ($0 ~ /[;{)][ \t]*\/\/ /) printf "%s:%d (trailing)\n    %s\n\n", file, NR, $0
            }
        ' "$file"
    done
}

case "${1:-blocks}" in
    blocks) blocks ;;
    flags)
        files | xargs grep -nE '^\s*(//|@\*)|[;{)]\s*// ' |
            grep -iE '\.md\b|https?:|\bsection\b|\btask\b|\bplan\b|\bv2\b|\bpreviously\b|\bused to\b|\bno longer\b|\bnow\b|\binstead\b|\brather than\b|\bnot\b|\bnever\b|\bno\b|n.t\b|\bwithout\b|\bavoid' || true ;;
    count) files | xargs grep -cE '^\s*(//|@\*)|[;{)]\s*// ' | grep -v ':0$' | sort -t: -k2 -nr ;;
    *) echo "usage: $0 [blocks|flags|count]" >&2; exit 2 ;;
esac
