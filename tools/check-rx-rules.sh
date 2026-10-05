#!/usr/bin/env bash
# Checks the app's C# against the rules in docs/reactive-extensions.md that a text scan can decide,
# and prints one line per breach as path:line: rule. Exits 1 when it finds any.
#
# Every subject needs the operator's agreement, so the agreed subjects are listed below by file and
# field. Add a line only once the operator has agreed to that subject.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo/src/Debarr"

agreed_subjects='
Appearance/UISettingsService.cs _subject
Detecting/DetectionOrchestrator.cs _subject
Detecting/DetectionSettingsService.cs _subject
Detecting/OverrideService.cs _subject
EventStore/ReadModelChangeListener.cs _subject
Hosting/HostSettingsFile.cs _subject
Notifying/NotifierSettingsService.cs _subject
Playing/PlaybackHandler.cs _subject
Playing/PlayerSettingsService.cs _subject
Scanning/LibraryScanner.cs _subject
Scanning/LibrarySettingsService.cs _subject
'

git ls-files --cached --others --exclude-standard -- '*.cs' ':!:Internal/Generated/*' | AGREED_SUBJECTS="$agreed_subjects" perl -e '
use strict;
use warnings;

my %agreed = map { $_ => 1 } grep { length } map { s/^\s+|\s+$//gr } split /\n/, $ENV{AGREED_SUBJECTS};
my $breaches = 0;

sub report {
    my ($file, $text, $offset, $rule) = @_;
    my $line = 1 + (() = substr($text, 0, $offset) =~ /\n/g);
    print "$file:$line: $rule\n";
    $breaches++;
}

# A balanced run of parentheses, used to read a call'"'"'s whole argument list across lines.
my $parens;
$parens = qr/\((?:[^()]++|(??{ $parens }))*\)/;

while (my $file = <STDIN>) {
    chomp $file;
    open my $handle, "<", $file or die "$file: $!";
    my $text = do { local $/; <$handle> };
    close $handle;

    # Each line that declares or creates a subject names an agreed field.
    while ($text =~ /^.*\b(?:I?Subject|BehaviorSubject|ReplaySubject|AsyncSubject)<.*$/mg) {
        my ($offset, $line) = ($-[0], $&);
        my ($field) = $line =~ /(\w+)\s*=/;
        next if $line =~ /^\s*using\b/;
        next if defined $field && $agreed{"$file $field"};
        report($file, $text, $offset, "a subject the operator has not agreed to");
    }

    while ($text =~ /\.(?:ObserveOn|SubscribeOn)\s*\(/g) {
        report($file, $text, $-[0], "ObserveOn or SubscribeOn outside a component") unless $file =~ m{^Components/};
    }

    while ($text =~ /\.Subscribe\s*($parens)/g) {
        my ($offset, $arguments) = ($-[0], $1);
        report($file, $text, $offset, "an async lambda passed to Subscribe") if $arguments =~ /^\(\s*async\b/;

        # Blank out nested brackets, so the commas left are the ones between arguments.
        my $top = substr($arguments, 1, -1);
        1 while $top =~ s/\([^()]*\)|\{[^{}]*\}|\[[^\[\]]*\]|<[^<>]*>/_/g;
        my $guarded = substr($text, 0, $offset) =~ /\.CompleteOnError\s*$parens\s*\z/;
        report($file, $text, $offset, "a Subscribe with no onError or CompleteOnError before it") unless $top =~ /,/ || $guarded;
    }

    while ($text =~ /(?:(?<!Task)\.(?:Sample|Throttle|Buffer|Window|Timeout|Delay)|Observable\.(?:Timer|Interval))\s*($parens)/g) {
        my ($offset, $arguments) = ($-[0], $1);
        next if $arguments =~ /^\(\s*\d+\s*\)$/;
        report($file, $text, $offset, "a time-based operator without the injected scheduler") unless $arguments =~ /cheduler/;
    }

    while ($text =~ /\.Replay\s*\(\s*\)|new\s+ReplaySubject<[^>]*>\s*\(\s*\)/g) {
        report($file, $text, $-[0], "an unbounded replay buffer");
    }

    while ($text =~ /(?:class|record|struct)\s+\w+[^{;]*?:[^{;]*\bI(?:Observable|Observer)</g) {
        report($file, $text, $-[0], "a type that implements IObservable<T> or IObserver<T>");
    }

    while ($text =~ /private\s+readonly\s+CompositeDisposable\s+(\w+)/g) {
        report($file, $text, $-[0], "a CompositeDisposable field not named _disposables") unless $1 eq "_disposables";
    }
}

exit($breaches ? 1 : 0);
'
