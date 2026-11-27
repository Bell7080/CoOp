"""Procedural sound effects for ElecMole (original, generated here; no third-party samples).

    python tools/audio/synth_sfx.py

Writes mono 16-bit 44.1 kHz WAVs into Assets/_Game/Resources/Audio/ElevenLabs/ (the folder ProtoAudio
loads every ProtoSound from):
  Jump.wav            soft, wet slime hop: a short rising "bloop" with a jelly wobble, low-passed
  TransmitterFull.wav quest-complete chime: rising soft bell arpeggio over a warm pad, small room reverb
  Charge.wav          light charging tick for the nose drinking/pumping: a soft, short rising sine "pwip"
                      with a faint shimmer, quiet and clean (replaces the old low gulp and buzz while draining)

    python tools/audio/synth_sfx.py Charge.wav   # write only the named file(s)
"""
import sys
import os
import wave

import numpy as np
from scipy.signal import butter, sosfilt

RATE = 44100
OUT = os.path.join(os.path.dirname(__file__), '..', '..', 'Assets', '_Game', 'Resources', 'Audio', 'ElevenLabs')
rng = np.random.default_rng(7)


def lowpass(x, hz, order=4):
    return sosfilt(butter(order, hz, 'low', fs=RATE, output='sos'), x)


def bandpass(x, lo, hi, order=2):
    return sosfilt(butter(order, [lo, hi], 'band', fs=RATE, output='sos'), x)


def env(n, attack, decay_tau):
    t = np.arange(n) / RATE
    a = np.clip(t / max(attack, 1e-4), 0, 1)
    return a * np.exp(-np.maximum(t - attack, 0) / decay_tau)


def reverb(x, mix=0.25):
    out = np.copy(x)
    for delay, gain in ((0.029, 0.45), (0.037, 0.4), (0.053, 0.33), (0.071, 0.28), (0.097, 0.22)):
        d = int(delay * RATE)
        y = np.zeros_like(x)
        for k in range(1, 7):  # a few feedback taps per comb
            if d * k >= len(x):
                break
            y[d * k:] += x[:-d * k] * gain ** k
        out += y * mix
    return out


def normalize(x, peak_db, rms_db=None):
    x = x - np.mean(x)
    if rms_db is not None:
        rms = np.sqrt(np.mean(x ** 2)) + 1e-9
        x *= 10 ** (rms_db / 20) / rms
    peak = np.max(np.abs(x)) + 1e-9
    limit = 10 ** (peak_db / 20)
    if peak > limit:
        x *= limit / peak
    return x


def write(name, x):
    os.makedirs(OUT, exist_ok=True)
    data = (np.clip(x, -1, 1) * 32767).astype('<i2')
    with wave.open(os.path.join(OUT, name), 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(data.tobytes())
    print(name, f'{len(x) / RATE:.2f}s')


def slime_jump():
    n = int(0.34 * RATE)
    t = np.arange(n) / RATE
    # pitch: a quick wet rise (the squish lifting off), then a lazy sag; wobble like jelly settling
    glide = 150 + 230 * (1 - np.exp(-t / 0.035)) - 90 * np.clip((t - 0.06) / 0.25, 0, 1)
    wobble = 1 + 0.06 * np.sin(2 * np.pi * 24 * t) * np.exp(-t / 0.09)
    freq = glide * wobble
    phase = 2 * np.pi * np.cumsum(freq) / RATE
    body = np.sin(phase) + 0.35 * np.sin(2 * phase + 0.6) + 0.12 * np.sin(3 * phase)
    body *= env(n, 0.006, 0.075)
    # the "blub": a second, smaller bubble just after
    t2 = np.clip(t - 0.055, 0, None)
    bub_phase = 2 * np.pi * np.cumsum(260 + 160 * (1 - np.exp(-t2 / 0.02))) / RATE
    bubble = np.sin(bub_phase) * env(n, 0.004, 0.04) * (t > 0.055)
    # wet squelch at lift-off
    squelch = bandpass(rng.standard_normal(n), 500, 2200) * env(n, 0.002, 0.018) * 0.9
    x = body + 0.45 * bubble + squelch
    x = lowpass(x, 2600)
    return normalize(x, -3.0, -19.0)


def bell(freq, n, start, tau, gain=1.0):
    t = np.arange(n) / RATE - start
    on = t >= 0
    t = np.where(on, t, 0)
    tone = (np.sin(2 * np.pi * freq * t) + 0.42 * np.sin(2 * np.pi * freq * 2.0 * t) * np.exp(-t / (tau * 0.4))
            + 0.18 * np.sin(2 * np.pi * freq * 3.01 * t) * np.exp(-t / (tau * 0.25))
            + 0.5 * np.sin(2 * np.pi * freq * 1.003 * t))
    a = np.clip(t / 0.006, 0, 1)
    return tone * a * np.exp(-t / tau) * on * gain


def transmitter_full():
    n = int(2.1 * RATE)
    t = np.arange(n) / RATE
    notes = [(523.25, 0.00), (659.25, 0.10), (783.99, 0.20), (1046.5, 0.32)]  # C5 E5 G5 C6
    x = np.zeros(n)
    for i, (f, s) in enumerate(notes):
        x += bell(f, n, s, 0.35 if i < 3 else 0.9, 0.8 if i < 3 else 1.0)
    # resolving chord shimmer under the last note
    for f in (523.25, 659.25, 783.99):
        x += bell(f, n, 0.32, 1.1, 0.35)
    # warm pad swell (C3 + G3), a little detuned, keeps it in the ship's low-tech world
    pad = sum(np.sin(2 * np.pi * f * t) + np.sin(2 * np.pi * f * 1.004 * t) for f in (130.81, 196.0))
    pad_env = np.clip(t / 0.25, 0, 1) * np.exp(-np.maximum(t - 0.3, 0) / 0.7)
    x += 0.18 * pad * pad_env
    x = lowpass(x, 6000)
    x = reverb(x, 0.3)
    fade = np.clip((n / RATE - t) / 0.25, 0, 1)
    return normalize(x * fade, -2.0, -18.0)


def charge():
    n = int(0.26 * RATE)
    t = np.arange(n) / RATE
    # a gentle upward glide, like a small battery indicator filling
    freq = 560 + 340 * (1 - np.exp(-t / 0.07))
    phase = 2 * np.pi * np.cumsum(freq) / RATE
    tone = np.sin(phase) + 0.22 * np.sin(2 * phase) + 0.06 * np.sin(3 * phase)
    shimmer = 1 + 0.12 * np.sin(2 * np.pi * 32 * t)
    x = tone * shimmer * env(n, 0.018, 0.075)
    # a faint airy electric fizz on top, very low
    x += 0.05 * bandpass(rng.standard_normal(n), 2500, 6000) * env(n, 0.01, 0.05)
    x = lowpass(x, 4200)
    fade = np.clip((n / RATE - t) / 0.04, 0, 1)
    return normalize(x * fade, -6.0, -24.0)


SOUNDS = {
    'Jump.wav': slime_jump,
    'TransmitterFull.wav': transmitter_full,
    'Charge.wav': charge,
}

if __name__ == '__main__':
    for name in (sys.argv[1:] or SOUNDS):
        write(name, SOUNDS[name]())
