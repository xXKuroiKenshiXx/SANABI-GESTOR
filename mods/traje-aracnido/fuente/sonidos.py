"""Sonidos de telarana sintetizados (sin samples de terceros).

No usan tonos puros (eso sonaba a laser): todo sale de ruido filtrado con formantes que se mueven,
mas crujidos cortos que dan la sensacion de algo pegajoso estirandose.
"""
import math

import numpy as np

RATE = 48000


def _env(n, attack, decay):
    t = np.arange(n) / RATE
    a = np.clip(t / max(attack, 1e-4), 0, 1)
    return a * np.exp(-np.maximum(t - attack, 0) / decay)


def _bandpass_sweep(x, f_start, f_end, q, curve=1.0):
    """Pasabanda resonante (biquad) cuya frecuencia central se mueve de f_start a f_end."""
    n = len(x)
    pos = np.linspace(0, 1, n) ** curve
    freqs = f_start * (f_end / f_start) ** pos
    y = np.zeros(n)
    x1 = x2 = y1 = y2 = 0.0
    for i in range(n):
        w0 = 2 * math.pi * freqs[i] / RATE
        alpha = math.sin(w0) / (2 * q)
        cos_w0 = math.cos(w0)
        a0 = 1 + alpha
        b0, b2 = alpha / a0, -alpha / a0
        a1, a2 = -2 * cos_w0 / a0, (1 - alpha) / a0
        out = b0 * x[i] + b2 * x2 - a1 * y1 - a2 * y2
        x2, x1 = x1, x[i]
        y2, y1 = y1, out
        y[i] = out
    return y


def _lowpass(x, cutoff):
    alpha = 1 - math.exp(-2 * math.pi * cutoff / RATE)
    y = np.empty(len(x))
    acc = 0.0
    for i, v in enumerate(x):
        acc += alpha * (v - acc)
        y[i] = acc
    return y


def _dark(x, cutoff):
    """Pasabajos de 2 polos (dos de 1 polo en serie): saca el siseo agudo."""
    return _lowpass(_lowpass(x, cutoff), cutoff)


def _finish(mono):
    mono = _dark(mono, 5200)
    return mono - _lowpass(mono, 110)


def _crackle(n, rate_hz, decay, rng, cutoff=2600, start=0.0):
    """Clics cortos al azar (hilos pegajosos que se despegan)."""
    out = np.zeros(n)
    t = start
    while True:
        t += rng.exponential(1 / rate_hz)
        i = int(t * RATE)
        if i >= n:
            break
        length = int(RATE * rng.uniform(0.0015, 0.004))
        burst = rng.normal(0, 1, length) * np.exp(-np.arange(length) / (length / 3))
        gain = math.exp(-t / decay) * rng.uniform(0.4, 1.0)
        out[i:i + length] += burst[:max(0, min(length, n - i))] * gain
    return _lowpass(out, cutoff)


def _stereo(mono, seed):
    rng = np.random.default_rng(seed)
    delay = 14
    right = np.concatenate([np.zeros(delay), mono[:-delay]]) * 0.8 + mono * 0.2
    right = right + rng.normal(0, 0.003, len(mono)) * np.abs(mono)
    out = np.stack([mono, right], axis=1)
    return out / max(np.abs(out).max(), 1e-9) * 0.85


def thwip(seed=1):
    """Disparo de telarana: 'th' de aire + 'wip' que sube + crujido pegajoso."""
    rng = np.random.default_rng(seed)
    n = int(0.36 * RATE)
    noise = rng.normal(0, 1, n)
    th = _dark(noise, 2200) * _env(n, 0.002, 0.014)
    wip = _bandpass_sweep(rng.normal(0, 1, n), 600, 2400, q=4.0, curve=0.6) * _env(n, 0.01, 0.05)
    wip2 = _bandpass_sweep(rng.normal(0, 1, n), 1100, 3600, q=3.0, curve=0.6) * _env(n, 0.012, 0.035)
    sticky = _dark(_crackle(n, 380, 0.1, rng, cutoff=1600, start=0.03), 1600)
    stretch = _bandpass_sweep(rng.normal(0, 1, n), 380, 240, q=7.0) * _env(n, 0.03, 0.09)
    mono = 1.2 * th + 4.0 * wip + 1.2 * wip2 + 3.0 * sticky + 2.0 * stretch
    return _stereo(_finish(mono), seed)


def splat(seed=2):
    """La telarana se pega: golpe blando y humedo que baja de tono, con crujidos."""
    rng = np.random.default_rng(seed)
    n = int(0.3 * RATE)
    hit = _dark(rng.normal(0, 1, n), 900) * _env(n, 0.001, 0.022)
    squelch = _bandpass_sweep(rng.normal(0, 1, n), 1100, 240, q=6.0, curve=0.5) * _env(n, 0.004, 0.065)
    sticky = _dark(_crackle(n, 300, 0.08, rng, cutoff=1400, start=0.01), 1400)
    mono = 1.5 * hit + 4.0 * squelch + 3.0 * sticky
    return _stereo(_finish(mono), seed)


def zip_back(seed=3):
    """La telarana se suelta y vuelve: 'fwip' que baja, mas corto y suave."""
    rng = np.random.default_rng(seed)
    n = int(0.28 * RATE)
    swish = _bandpass_sweep(rng.normal(0, 1, n), 2000, 500, q=4.0, curve=0.8) * _env(n, 0.03, 0.06)
    peel = _dark(_crackle(n, 250, 0.05, rng, cutoff=1600), 1600)
    mono = 3.0 * swish + 2.0 * peel
    return _stereo(_finish(mono), seed) * 0.8


def to_pcm16(stereo, rate_out=RATE):
    if rate_out != RATE:
        n_out = int(len(stereo) * rate_out / RATE)
        idx = np.linspace(0, len(stereo) - 1, n_out)
        stereo = np.stack([np.interp(idx, np.arange(len(stereo)), stereo[:, c]) for c in range(2)], axis=1)
    return (np.clip(stereo, -1, 1) * 32767).astype("<i2")
