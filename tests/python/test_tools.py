"""Unit tests for the pure (no ffmpeg, no files) functions in tools/.

Run: .venv/bin/python -m pytest
"""

import numpy as np
import pytest

import analyze_engine
import make_smoke_textures
import match_spectrum
import synth_om617

# ---------------------------------------------------------------- analyze_engine


def pulse_train(rate_hz: float, esr: int, seconds: float = 2.0) -> np.ndarray:
    """Smooth (2 ms) pulses, like the 1 ms-smoothed envelope main() builds.

    Single-sample spikes are not realistic input: with a non-integer period their
    autocorrelation favours whole multiples of the period and firing() reports a
    sub-harmonic.
    """
    t = np.arange(int(esr * seconds)) / esr
    return sum(np.exp(-(((t - p) / 0.002) ** 2)) for p in np.arange(0, seconds, 1 / rate_hz))


@pytest.mark.parametrize("rate_hz", [12.0, 29.0, 60.0, 100.0])
def test_firing_pulse_train_returns_its_rate(rate_hz):
    # Arrange
    esr = 1000
    env = pulse_train(rate_hz, esr)

    # Act
    hz, confidence = analyze_engine.firing(env, esr)

    # Assert
    assert hz == pytest.approx(rate_hz, rel=0.05)
    assert confidence > 0.5


def test_firing_silent_envelope_returns_zero():
    assert analyze_engine.firing(np.zeros(1000), 1000) == (0.0, 0.0)


# ---------------------------------------------------------------- smoke textures


def test_value_noise_any_seed_stays_within_unit_range():
    noise = make_smoke_textures.value_noise(64, 4, np.random.default_rng(0))

    assert noise.shape == (64, 64)
    assert noise.min() >= -1.0
    assert noise.max() <= 1.0


def test_puff_returns_rgba_uint8_square():
    tex = make_smoke_textures.puff(32, seed=1)

    assert tex.shape == (32, 32, 4)
    assert tex.dtype == np.uint8


def test_puff_same_seed_is_deterministic():
    assert np.array_equal(make_smoke_textures.puff(32, 5), make_smoke_textures.puff(32, 5))


def test_puff_corners_are_fully_transparent():
    # the game draws the whole quad; any alpha at the edge shows as a square
    alpha = make_smoke_textures.puff(64, seed=2)[..., 3]

    assert alpha[0, 0] == alpha[0, -1] == alpha[-1, 0] == alpha[-1, -1] == 0
    assert alpha.max() > 200


# ---------------------------------------------------------------- synth_om617


@pytest.mark.parametrize("rpm", [700, 2500])
def test_render_loop_length_is_whole_engine_cycles(rpm):
    # Arrange: one 4-stroke cycle is 120/rpm seconds
    cycle_samples = 120.0 / rpm * synth_om617.SR

    # Act
    y = synth_om617.render(rpm, 1.0, np.random.default_rng(0))

    # Assert: a whole number of cycles (within rounding), so the loop is seamless
    cycles = len(y) / cycle_samples
    assert abs(cycles - round(cycles)) * cycle_samples <= 1.0


def test_render_any_load_output_is_finite():
    for load in (0.0, 0.5, 1.0):
        y = synth_om617.render(1400, load, np.random.default_rng(0))
        assert np.isfinite(y).all()


def test_render_same_seed_is_deterministic():
    a = synth_om617.render(1000, 1.0, np.random.default_rng(3))
    b = synth_om617.render(1000, 1.0, np.random.default_rng(3))

    assert np.array_equal(a, b)


def test_render_idle_firing_rate_matches_five_cylinder_formula():
    # Arrange: OM617 fires every 144 deg of crank, i.e. rpm / 24 Hz
    rpm, esr = 700, 1000
    y = synth_om617.render(rpm, 1.0, np.random.default_rng(0))
    env = np.abs(y)[: len(y) // 48 * 48].reshape(-1, 48).mean(axis=1)  # 48 kHz -> 1 kHz

    # Act
    hz, _ = analyze_engine.firing(np.tile(env, 2), esr)

    # Assert
    assert hz == pytest.approx(rpm / 24, rel=0.08)


# ---------------------------------------------------------------- match_spectrum


def test_smooth_oct_flat_spectrum_stays_flat():
    f = np.linspace(20, 20000, 500)

    out = match_spectrum.smooth_oct(f, np.ones_like(f))

    assert np.allclose(out, 1.0)
