using System.Numerics;

namespace Ahjo.Miniaudio;

/// <summary>
/// A fixed set of voices over one <see cref="SoundAsset"/>, for effects that
/// overlap — footsteps, gunshots, impacts. <see cref="Play()"/> takes an idle
/// voice, or steals the least recently played one, and allocates nothing.
/// </summary>
/// <remarks>
/// The voices are ordinary <see cref="Sound"/>s on the engine; its
/// <see cref="AudioEngine.Dispose"/> disposes them, and so does this pool's.
/// </remarks>
public sealed class SoundPool : IDisposable
{
    private readonly Sound[] _voices;

    // When each voice was last played, on a pool-local clock; 0 = never.
    private readonly long[] _playedAt;
    private long _clock;

    private SoundPool(Sound[] voices)
    {
        _voices = voices;
        _playedAt = new long[voices.Length];
    }

    /// <summary>Creates <paramref name="voices"/> stopped sounds over <paramref name="asset"/>.</summary>
    /// <exception cref="MiniaudioException">miniaudio could not initialize a voice; none are left behind.</exception>
    public static SoundPool Create(AudioEngine engine, SoundAsset asset, int voices, in SoundDescription description = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(voices, 1);
        var sounds = new Sound[voices];
        var created = 0;
        try
        {
            for (; created < voices; created++)
            {
                sounds[created] = Sound.Create(engine, asset, description);
            }
        }
        catch
        {
            for (var i = 0; i < created; i++)
            {
                sounds[i].Dispose();
            }

            throw;
        }

        return new SoundPool(sounds);
    }

    /// <summary>The pool's voices.</summary>
    public ReadOnlySpan<Sound> Voices => _voices;

    /// <summary>Plays the effect from the start on a free voice and returns that voice.</summary>
    /// <remarks>The returned voice may be stolen by a later <see cref="Play()"/>; don't hold on to it past the effect.</remarks>
    public Sound Play()
    {
        var voice = Acquire();
        voice.Play();
        return voice;
    }

    /// <summary>Plays the effect at <paramref name="position"/> (the pool's description must be <see cref="SoundDescription.Spatialized"/>).</summary>
    /// <inheritdoc cref="Play()" path="/remarks"/>
    public Sound Play(Vector3 position)
    {
        var voice = Acquire();
        voice.Position = position;
        voice.Play();
        return voice;
    }

    /// <summary>Stops every voice.</summary>
    public void Stop()
    {
        foreach (var voice in _voices)
        {
            voice.Stop();
        }
    }

    // The least recently played idle voice; if every voice is busy, the
    // least recently played one is stolen. A linear scan over a handful of
    // voices, allocation-free.
    private Sound Acquire()
    {
        var idle = -1;
        var oldest = 0;
        for (var i = 0; i < _voices.Length; i++)
        {
            if (_playedAt[i] < _playedAt[oldest])
            {
                oldest = i;
            }

            if ((idle < 0 || _playedAt[i] < _playedAt[idle]) && !_voices[i].IsPlaying)
            {
                idle = i;
            }
        }

        var index = idle >= 0 ? idle : oldest;
        _playedAt[index] = ++_clock;
        return _voices[index];
    }

    /// <summary>Disposes every voice.</summary>
    public void Dispose()
    {
        foreach (var voice in _voices)
        {
            voice.Dispose();
        }
    }
}
