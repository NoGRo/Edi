import test from 'node:test';
import assert from 'node:assert/strict';
import playerTools from '../wwwroot/js/player-tools.js';

const { calculatePlaylistProgress, calculateProgressLayers, nextProgressMode } = playerTools;

test('persistent progress modes cycle hidden, video, playlist', () => {
    assert.equal(nextProgressMode('none'), 'video');
    assert.equal(nextProgressMode('video'), 'playlist');
    assert.equal(nextProgressMode('playlist'), 'none');
});

test('playlist progress combines prior durations with current video seek', () => {
    assert.equal(calculatePlaylistProgress([10_000, 20_000, 30_000], 1, 5), 0.25);
    assert.equal(calculatePlaylistProgress([10_000, 20_000, 30_000], 2, 15), 0.75);
});

test('playlist progress clamps seek and ignores unavailable durations', () => {
    assert.equal(calculatePlaylistProgress([10_000, null, 30_000], 2, 90), 1);
    assert.equal(calculatePlaylistProgress([], -1, 5), 0);
});

test('combined progress shares overlap and leaves only the leading color beyond it', () => {
    const videoAhead = calculateProgressLayers(0.8, 0.25);
    assert.equal(videoAhead.overlap, 0.25);
    assert.ok(Math.abs(videoAhead.tail - 0.55) < Number.EPSILON);
    assert.equal(videoAhead.tailType, 'video');

    const playlistAhead = calculateProgressLayers(0.2, 0.7);
    assert.equal(playlistAhead.overlap, 0.2);
    assert.ok(Math.abs(playlistAhead.tail - 0.5) < Number.EPSILON);
    assert.equal(playlistAhead.tailType, 'playlist');
});
