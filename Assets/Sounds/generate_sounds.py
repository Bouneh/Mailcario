"""Create the original Mailcario Cristal new-mail chime.

Run `python Assets/Sounds/generate_sounds.py` from the repository root.
The sounds are original and use no third-party samples or dependencies.
"""

from __future__ import annotations

import math
import struct
import wave
from pathlib import Path


SAMPLE_RATE = 48_000
OUTPUT = Path(__file__).parent


# Each event is (start, pitch, length, strength, attack, decay, pan).
SOUNDS = {
    "mailcario-cristal.wav": {
        "notes": [(0.025, 392.00, 0.83, 1.00, 0.014, 0.22, -0.13),
                  (0.145, 587.33, 0.72, 0.48, 0.012, 0.20, 0.13)],
        "partials": (1.0, 0.21, 0.030),
        "peak": 0.165,
        "reflections": ((62, 0.085), (113, 0.035)),
    },
}


def render(spec: dict) -> bytes:
    total = max(note[0] + note[2] for note in spec["notes"]) + 0.19
    count = math.ceil(total * SAMPLE_RATE)
    left = [0.0] * count
    right = [0.0] * count
    for start, pitch, length, strength, attack, decay, pan in spec["notes"]:
        first = round(start * SAMPLE_RATE)
        size = round(length * SAMPLE_RATE)
        left_gain = math.sqrt((1.0 - pan) / 2.0)
        right_gain = math.sqrt((1.0 + pan) / 2.0)
        for index in range(size):
            time = index / SAMPLE_RATE
            rise = min(1.0, time / attack)
            rise = rise * rise * (3.0 - 2.0 * rise)
            finish = min(1.0, (length - time) / 0.13)
            envelope = rise * finish * math.exp(-time / decay)
            phase = 2.0 * math.pi * pitch * time
            # Quiet overtones give a rounded chime instead of an electronic beep.
            tone = sum(weight * math.sin(phase * harmonic)
                       for harmonic, weight in enumerate(spec["partials"], start=1))
            value = strength * envelope * tone
            left[first + index] += left_gain * value
            right[first + index] += right_gain * value

    dry_left, dry_right = left[:], right[:]
    for milliseconds, gain in spec["reflections"]:
        delay = round(milliseconds * SAMPLE_RATE / 1000)
        for index in range(delay, count):
            left[index] += gain * dry_right[index - delay]
            right[index] += gain * dry_left[index - delay]

    peak = max(max(map(abs, left)), max(map(abs, right)))
    scale = spec["peak"] / peak
    frames = bytearray(count * 4)
    for index, (sample_left, sample_right) in enumerate(zip(left, right)):
        struct.pack_into("<hh", frames, index * 4,
                         round(sample_left * scale * 32767),
                         round(sample_right * scale * 32767))
    return bytes(frames)


for name, sound in SOUNDS.items():
    with wave.open(str(OUTPUT / name), "wb") as wav:
        wav.setnchannels(2)
        wav.setsampwidth(2)
        wav.setframerate(SAMPLE_RATE)
        wav.writeframes(render(sound))
    print(OUTPUT / name)
