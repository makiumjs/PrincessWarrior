"""Trims a fade-in off a WAV and crossfades its end into its start.

A track that fades in starts quiet and ends loud, so looping it drops and
swells once per cycle -- measured here at up to 127 dB, which is a file that
opens on digital silence. Two operations fix it:

  trim     cut the fade-in, so the file starts at its own body level
  xfade    blend the last L seconds over the first L, so the last sample
           before the wrap flows into the first one after it

Only 2L frames are ever decoded: the middle of the file is copied as bytes.
On a 156-second 24-bit stereo track that is the difference between seconds
and minutes.
"""
import struct, math, os, sys

def parse(path):
    d = open(path, 'rb').read()
    assert d[:4] == b'RIFF' and d[8:12] == b'WAVE', path
    p, fmt, data = 12, None, None
    while p + 8 <= len(d):
        cid, csz = d[p:p+4], struct.unpack('<I', d[p+4:p+8])[0]
        if cid == b'fmt ':
            fmt = struct.unpack('<HHIIHH', d[p+8:p+24])      # tag ch sr br ba bits
        elif cid == b'data':
            data = (p + 8, csz)
            break
        p += 8 + csz + (csz & 1)
    return d, fmt, data

def dec(raw, i, bits, tag):
    if bits == 16: return struct.unpack_from('<h', raw, i)[0] / 32768.0
    if bits == 24: return int.from_bytes(raw[i:i+3], 'little', signed=True) / 8388608.0
    return struct.unpack_from('<f', raw, i)[0]

def enc(v, bits, tag):
    if bits == 16:
        return struct.pack('<h', max(-32768, min(32767, int(round(v * 32768)))))
    if bits == 24:
        return max(-8388608, min(8388607, int(round(v * 8388608)))).to_bytes(3, 'little', signed=True)
    return struct.pack('<f', v)

def rms_at(raw, start_frame, n_frames, ba, ch, bits, tag, sw):
    s = 0.0
    for f in range(n_frames):
        s += dec(raw, start_frame * ba + f * ba, bits, tag) ** 2
    return math.sqrt(s / n_frames) if n_frames else 0.0

def loopify(path, xfade_sec, out_path):
    d, fmt, (doff, dsz) = parse(path)
    tag, ch, sr, br, ba, bits = fmt
    sw = bits // 8
    raw = d[doff:doff+dsz]
    total = dsz // ba

    # Body level from a middle slice -- the head is the thing being judged, so
    # it must not be part of the yardstick.
    body = rms_at(raw, total // 3, min(sr * 2, total // 3), ba, ch, bits, tag, sw)

    # Trim: the first quarter-second window that reaches 80% of body level.
    win = sr // 4
    trim = 0
    while (trim + win) < total // 2:
        if rms_at(raw, trim, win, ba, ch, bits, tag, sw) >= body * 0.8:
            break
        trim += win

    L = int(xfade_sec * sr)
    N = total - trim
    M = N - L                      # loop length after the crossfade
    assert M > L * 2, path

    out = bytearray()
    base = trim * ba
    for i in range(L):
        w = i / L
        a, b = math.sqrt(w), math.sqrt(1.0 - w)      # equal power, no dip
        for c in range(ch):
            h = dec(raw, base + i * ba + c * sw, bits, tag)
            t = dec(raw, base + (M + i) * ba + c * sw, bits, tag)
            out += enc(h * a + t * b, bits, tag)
    out += raw[base + L * ba : base + M * ba]

    hdr = struct.pack('<4sI4s4sIHHIIHH4sI', b'RIFF', 36 + len(out), b'WAVE',
                      b'fmt ', 16, tag, ch, sr, sr * ba, ba, bits, b'data', len(out))
    open(out_path, 'wb').write(hdr + bytes(out))
    return trim / sr, M / sr, (total / sr)

if __name__ == '__main__':
    path, xf = sys.argv[1], float(sys.argv[2])
    t, newdur, olddur = loopify(path, xf, path + '.tmp')
    os.replace(path + '.tmp', path)
    print(f"{os.path.basename(path):32s} tagliati {t:5.2f}s  xfade {xf:.1f}s  {olddur:6.1f}s -> {newdur:6.1f}s")
